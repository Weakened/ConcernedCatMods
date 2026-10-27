#!/usr/bin/env python3
"""Summarize read-only native probe metadata without inventing visual or baseline PASSes.

Input comes from NativeAnimationBaselineProbe schema 1. Video metadata is optional
ffprobe JSON obtained separately by the lead. This tool never runs/changes the game.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import statistics
from collections import defaultdict
from fractions import Fraction
from pathlib import Path

MAX_BYTES = 64 * 1024 * 1024
MAX_RECORDS = 20000


def finite(value, name):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"{name} must be a finite number")
    return float(value)


def reject_constant(value):
    raise ValueError(f"Non-finite JSON constant: {value}")


def load_records(path):
    if path.stat().st_size > MAX_BYTES:
        raise ValueError("Probe exceeds 64 MiB analysis limit")
    records = []
    with path.open(encoding="utf-8-sig") as stream:
        for line in stream:
            if not line.strip():
                continue
            item = json.loads(line, parse_constant=reject_constant)
            if not isinstance(item, dict):
                raise ValueError("Each probe record must be an object")
            records.append(item)
            if len(records) > MAX_RECORDS:
                raise ValueError("Probe exceeds record limit")
    return records


def summarize(records):
    headers = [r for r in records if r.get("kind") == "header"]
    ends = [r for r in records if r.get("kind") == "end"]
    if len(headers) != 1 or headers[0].get("schema") != 1 or len(ends) != 1:
        raise ValueError("Require exactly one schema-1 header and one end record")
    header, end = headers[0], ends[0]
    if records[0] is not header or records[-1] is not end:
        raise ValueError("Header and end must bound one complete capture")
    if set(header.get("nativeIKPatchOwners", [])) & {
        "com.theconcernedcat.valheim.concernedladders.pose",
        "com.theconcernedcat.valheim.concernedladders.presentation",
        "com.theconcernedcat.valheim.concernedladders.original-ik-cleanup",
    }:
        raise ValueError("Known ladder IK presentation/cleanup is present; this is not an isolated native capture")
    if not header.get("isolationEvidenceClaim"):
        raise ValueError("Missing external native-isolation evidence reference")

    clips = {}
    for clip in (r for r in records if r.get("kind") == "clip"):
        clip_id = clip["id"]
        if clip_id in clips:
            raise ValueError("Duplicate clip metadata identity")
        length = finite(clip["lengthSeconds"], "clip length")
        rate = finite(clip["authoredSamplingRate"], "authored sampling metadata")
        if length < 0 or rate < 0:
            raise ValueError("Negative clip metadata")
        clips[clip_id] = clip

    frames = [r for r in records if r.get("kind") == "frame"]
    if len(frames) < 2 or end.get("sampledFrames") != len(frames):
        raise ValueError("Require two or more samples and matching end count")
    if len(frames) > 3600:
        raise ValueError("Frame count exceeds probe limit")
    times = [finite(f["elapsedSeconds"], "elapsed seconds") for f in frames]
    numbers = [finite(f["frame"], "frame index") for f in frames]
    if any(b <= a for a, b in zip(times, times[1:])):
        raise ValueError("Capture clock must increase")
    if any(b <= a for a, b in zip(numbers, numbers[1:])):
        raise ValueError("Duplicate or reversed frame indices")
    elapsed = times[-1] - times[0]
    observed_intervals = [b - a for a, b in zip(times, times[1:])]
    unity_deltas = [finite(f["unityUnscaledDeltaSeconds"], "Unity frame delta") for f in frames]
    if any(d <= 0 for d in unity_deltas):
        raise ValueError("Unscaled frame intervals must be positive")
    gaps = sum(max(0, int(b - a) - 1) for a, b in zip(numbers, numbers[1:]))
    reads = [finite(f["readMilliseconds"], "probe read milliseconds") for f in frames]
    if any(d < 0 for d in reads):
        raise ValueError("Negative probe overhead")

    state_segments = defaultdict(list)
    active_clip_frames = defaultdict(int)
    for index, frame in enumerate(frames):
        finite(frame["animatorSpeed"], "Animator speed")
        finite(frame["timeScale"], "time scale")
        for layer in frame.get("layers", []):
            state = layer["current"]
            finite(state["normalizedTime"], "state normalized time")
            finite(state["speed"], "state speed")
            finite(state["speedMultiplier"], "state speed multiplier")
            finite(state["lengthSeconds"], "state length")
            for info in layer.get("currentClips", []) + layer.get("nextClips", []):
                finite(info["weight"], "clip blend weight")
                if info["id"] not in clips:
                    raise ValueError("Observed clip is absent from the clip inventory")
                if info["weight"] > 0:
                    active_clip_frames[info["id"]] += 1
            # Only adjacent, unblended frames with the same state are eligible for rate inference.
            if layer.get("transitioning") or not index:
                continue
            previous_frame = frames[index - 1]
            if frame["frame"] - previous_frame["frame"] != 1:
                continue
            previous = next((x for x in previous_frame.get("layers", [])
                             if x["index"] == layer["index"]), None)
            if not previous or previous.get("transitioning"):
                continue
            old = previous["current"]
            if state["fullPathHash"] != old["fullPathHash"]:
                continue
            dt = finite(frame["unityUnscaledDeltaSeconds"], "unscaled delta")
            dn = state["normalizedTime"] - old["normalizedTime"]
            # Negative values indicate restart/reversal; keep raw records, do not call them loop cadence.
            if dn <= 0:
                continue
            key = (layer["index"], layer["name"], state["fullPathHash"])
            state_segments[key].append((dn, dt))

    rates = []
    for (layer, name, state_hash), intervals in sorted(state_segments.items()):
        seconds = sum(dt for _, dt in intervals)
        normalized_travel = sum(dn for dn, _ in intervals)
        rates.append({
            "layer_index": layer, "layer_name": name, "state_full_path_hash": state_hash,
            "readable_state_name": None,
            "adjacent_nontransition_intervals": len(intervals),
            "sampled_seconds": seconds,
            "normalized_state_travel": normalized_travel,
            "normalized_state_cycles_per_unscaled_second": normalized_travel / seconds,
            "inference_limit": "State normalized-time rate; not a measured foot-contact cycle. "
                "Blend trees, state changes and insufficient repeats require visual review."
        })

    return {
        "status": "MEASUREMENTS_ONLY_BASELINE_REQUIRES_REVIEW",
        "source": header,
        "end": end,
        "observed_clip_metadata": [
            {**clip, "active_blend_entries": active_clip_frames[clip_id]}
            for clip_id, clip in sorted(clips.items())
        ],
        "observed_render_update_cadence": {
            "sampling_stage": sorted(set(f.get("stage", "unspecified") for f in frames)),
            "frames_observed": len(frames),
            "clock_seconds_between_first_last_sample": elapsed,
            "callbacks_per_second": (len(frames) - 1) / elapsed,
            "unity_frame_indices_per_second": (numbers[-1] - numbers[0]) / elapsed,
            "missing_frame_indices": gaps,
            "median_callback_interval_seconds": statistics.median(observed_intervals),
            "median_unity_unscaled_frame_interval_seconds": statistics.median(unity_deltas),
            "max_unity_unscaled_frame_interval_seconds": max(unity_deltas),
            "median_read_overhead_ms_excluding_serialization": statistics.median(reads),
            "limit": "CPU LateUpdate observation; not a GPU/display presentation or capture-FPS measurement."
        },
        "inferred_state_playback": rates,
        "unavailable_from_this_probe": [
            "visual cadence/contact quality, pelvis/torso movement and blend aesthetics",
            "actual recorder FPS, dropped/duplicate video frames and synchronization",
            "GPU/display-presented FPS",
            "readable controller state names from state hashes alone",
            "raw curves, exact key counts, rest/T-pose and authoring import settings",
            "proof of native isolation, compiler provenance or actual installed hash from caller claims"
        ]
    }


def rational_rate(value):
    try:
        rate = float(Fraction(value))
    except (ValueError, ZeroDivisionError, TypeError):
        return None
    return rate if math.isfinite(rate) and rate > 0 else None


def video_metadata(path):
    raw = json.loads(path.read_text(encoding="utf-8-sig"), parse_constant=reject_constant)
    return [{
        "codec_type": s.get("codec_type"),
        "codec_name": s.get("codec_name"),
        "width": s.get("width"), "height": s.get("height"),
        "container_avg_frame_rate": s.get("avg_frame_rate"),
        "container_avg_fps": rational_rate(s.get("avg_frame_rate")),
        "container_nominal_frame_rate": s.get("r_frame_rate"),
        "container_nominal_fps": rational_rate(s.get("r_frame_rate")),
        "time_base": s.get("time_base"),
        "duration": s.get("duration"), "nb_frames": s.get("nb_frames"),
        "nb_read_frames": s.get("nb_read_frames"),
        "limit": "Container/decoded-frame metadata only. No dropped/duplicated-frame or capture-setting proof."
    } for s in raw.get("streams", []) if s.get("codec_type") == "video"]


def write_csv(report, path):
    with path.open("w", encoding="utf-8", newline="") as stream:
        fields = ["action", "clip_id", "clip_name", "clip_length_seconds",
                  "authored_sampling_rate_metadata_hz", "looping", "observed_in_blend_entries",
                  "native_contact_observation", "native_body_observation", "capture_fps",
                  "raw_curve_access", "baseline_gate"]
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        for clip in report["observed_clip_metadata"]:
            writer.writerow({
                "action": report["source"]["action"],
                "clip_id": clip["id"], "clip_name": clip["name"],
                "clip_length_seconds": clip["lengthSeconds"],
                "authored_sampling_rate_metadata_hz": clip["authoredSamplingRate"],
                "looping": clip["looping"], "observed_in_blend_entries": clip["active_blend_entries"],
                "native_contact_observation": "UNREVIEWED_VIDEO_REQUIRED",
                "native_body_observation": "UNREVIEWED_VIDEO_REQUIRED",
                "capture_fps": "UNAVAILABLE_FROM_PROBE",
                "raw_curve_access": "NOT_ATTEMPTED_BY_RUNTIME_PROBE",
                "baseline_gate": "NOT_AUTOMATICALLY_ACCEPTED"
            })


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("probe", type=Path)
    parser.add_argument("--video-metadata", type=Path, help="Lead-produced ffprobe -show_streams JSON")
    parser.add_argument("--csv", type=Path, help="Derived clip inventory, not a completed baseline")
    args = parser.parse_args()
    report = summarize(load_records(args.probe))
    if args.video_metadata:
        report["observed_video_metadata"] = video_metadata(args.video_metadata)
    if args.csv:
        write_csv(report, args.csv)
    print(json.dumps(report, indent=2, allow_nan=False))


if __name__ == "__main__":
    main()
