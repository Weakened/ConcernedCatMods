#!/usr/bin/env python3
"""Derived measurements from Claude's native animtrace CSV; never a visual/baseline PASS."""
from __future__ import annotations
import argparse
import csv
import hashlib
import io
import json
import math
import statistics
from collections import Counter, defaultdict
from pathlib import Path


def number(row, key):
    value = float(row[key])
    if not math.isfinite(value):
        raise ValueError(f"Non-finite {key}")
    return value


def clip_names(text):
    # The harness records names/weights, not unique clip identities; duplicate names merge here.
    totals = defaultdict(float)
    for part in text.split("+"):
        if not part:
            continue
        name, weight = part.rsplit("@", 1)
        value = float(weight)
        if not math.isfinite(value) or value < 0:
            raise ValueError("Invalid clip blend weight")
        totals[name] += value
    return dict(totals)


def summarize(path):
    before = path.stat()
    if before.st_size > 64 * 1024 * 1024:
        raise ValueError("Trace exceeds 64 MiB")
    raw = path.read_bytes()
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError("Trace changed during read; wait for delivery")
    rows = list(csv.DictReader(io.StringIO(raw.decode("utf-8-sig"))))
    if not 2 <= len(rows) <= 20000:
        raise ValueError("Require 2..20000 complete trace samples")
    frames = [number(r, "frame") for r in rows]
    times = [number(r, "t") for r in rows]
    delta = [number(r, "dt") for r in rows]
    if any(b <= a for a, b in zip(frames, frames[1:])) or any(b <= a for a, b in zip(times, times[1:])):
        raise ValueError("Duplicate/reversed frame or time")
    if any(d <= 0 for d in delta):
        raise ValueError("Nonpositive frame delta")
    seconds = times[-1] - times[0]
    layers = sorted({key[:-4] for key in rows[0] if key.startswith("L") and key.endswith("clip")})
    state_rates = []
    for layer in layers:
        groups = defaultdict(lambda: [0.0, 0.0, 0, []])
        for previous, current in zip(rows, rows[1:]):
            if current[layer + "trans"] != "0" or previous[layer + "trans"] != "0":
                continue
            if number(current, "frame") != number(previous, "frame") + 1:
                continue
            clips = clip_names(current[layer + "clip"])
            old_clips = clip_names(previous[layer + "clip"])
            if not clips or set(clips) != set(old_clips):
                continue
            dn = number(current, layer + "norm") - number(previous, layer + "norm")
            if dn < 0 or dn > 1:
                continue
            key = tuple(sorted(clips))
            group = groups[key]
            group[0] += dn
            group[1] += number(current, "t") - number(previous, "t")
            group[2] += 1
            group[3].append(number(current, layer + "spd"))
        for names, (travel, elapsed, count, speeds) in groups.items():
            state_rates.append({
                "layer": layer, "clip_names": names, "eligible_intervals": count,
                "eligible_seconds": elapsed, "normalized_state_travel": travel,
                "normalized_state_units_per_wall_second": travel / elapsed,
                "reported_harness_speed_median": statistics.median(speeds),
                "limit": "No state hashes/clip IDs in this CSV. Same names do not prove same asset/state. "
                         "Includes zero-change render intervals; not foot-contact cadence."
            })

    bones = sorted({key[:-1] for key in rows[0] if key.endswith("X") and key[0].isupper()
                    and key[:-1] + "Y" in rows[0] and key[:-1] + "Z" in rows[0]})
    pose = {}
    for bone in bones:
        vectors = [tuple(number(row, bone + axis) for axis in "XYZ") for row in rows]
        changes = sum(a != b for a, b in zip(vectors, vectors[1:]))
        pose[bone] = {
            "root_local_min": [min(v[i] for v in vectors) for i in range(3)],
            "root_local_max": [max(v[i] for v in vectors) for i in range(3)],
            "observed_changed_intervals_per_second": changes / seconds,
            "limit": "Evaluated pose, not rest pose or planted world contact."
        }

    segments = {}
    for side in ("Left", "Right"):
        for limb in ("Arm", "Leg"):
            names = (side + "Upper" + limb, side + "Lower" + limb,
                     side + ("Hand" if limb == "Arm" else "Foot"))
            if not all(n in bones for n in names):
                continue
            lengths = [[], []]
            bends = []
            for row in rows:
                points = [tuple(number(row, n + a) for a in "XYZ") for n in names]
                a = tuple(points[0][i] - points[1][i] for i in range(3))
                b = tuple(points[2][i] - points[1][i] for i in range(3))
                la = math.sqrt(sum(x*x for x in a))
                lb = math.sqrt(sum(x*x for x in b))
                lengths[0].append(la)
                lengths[1].append(lb)
                if la > 0 and lb > 0:
                    angle = math.degrees(math.acos(max(-1, min(1, sum(x*y for x,y in zip(a,b)) / (la*lb)))))
                    bends.append(180 - angle)
            segments[side + limb] = {
                "median_upper_segment_root_local_units": statistics.median(lengths[0]),
                "median_lower_segment_root_local_units": statistics.median(lengths[1]),
                "observed_joint_flexion_degrees_min": min(bends) if bends else None,
                "observed_joint_flexion_degrees_max": max(bends) if bends else None,
                "limit": "Bone-pivot segments, not sole/palm contact dimensions. Verify root scale before metres."
            }

    changed_states = sum(a.get("L0norm") != b.get("L0norm") for a,b in zip(rows,rows[1:]))
    return {
        "status": "DERIVED_MEASUREMENTS_NOT_BASELINE_ACCEPTANCE",
        "input": str(path), "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw),
        "rows": len(rows), "seconds_first_to_last": seconds,
        "render_update": {
            "observed_callbacks_per_second": (len(rows)-1)/seconds,
            "frame_indices_per_second": (frames[-1]-frames[0])/seconds,
            "missing_frame_indices": sum(int(b-a-1) for a,b in zip(frames,frames[1:])),
            "median_unity_dt": statistics.median(delta), "maximum_unity_dt": max(delta),
            "changed_base_state_time_intervals_per_second": changed_states/seconds,
            "limit": "Instrumented LateUpdate observations, not authored sampling, GPU display or recorder FPS."
        },
        "dominant_base_clip_field_counts": Counter(r.get("L0clip", "") for r in rows).most_common(12),
        "inferred_playback": state_rates,
        "observed_bone_ranges": pose,
        "derived_limb_segments": segments,
        "unavailable": ["source sampling metadata (requires actual clip inventory)",
                        "raw curves/rest pose", "visual contact/blend quality",
                        "native-isolation proof", "recording FPS/drops and video synchronization",
                        "presented/GPU FPS", "unique clip/state identity absent from CSV"]
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    args = parser.parse_args()
    print(json.dumps(summarize(args.trace), indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
