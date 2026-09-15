#!/usr/bin/env python3
"""Deterministically renders the Concerned Foreman Thunderstore package
icon into src/ConcernedForeman/Package/icon.png (256x256).

Pure standard library: the same tiny signed-distance-field rasterizer and
minimal PNG writer proven by tools/generate_icon_sprites.py and
tools/generate_teamster_icon.py, so the icon is reproducible from source
with no image-editing tools, no external assets, and no licensing
questions. This is the CF-SET-002 bootstrap placeholder until a
public-media pass produces final storefront art.

The palette keeps the Concerned Cat sprite language - warm parchment
glyph, near-black outline, parchment frame - on a slate background that
reads as stone and mortar rather than Teamster's leather.

The glyph is a plumb bob hanging from the middle of a beam carried by
two posts: the product's promise in one shape, which is judging whether
a structure is true and being able to say why.

Run from the repository root:
    python ./tools/generate_foreman_icon.py
"""

from __future__ import annotations

import math
import struct
import zlib
from pathlib import Path

SIZE = 256
SUPER = 2  # 2x2 samples per pixel
OUTLINE_WIDTH = 5.0

# Hand-drawn pass, identical in kind to the other two generators.
WOBBLE_AMPLITUDE = 2.6
WOBBLE_FREQUENCY = 0.065
MAX_TILT_DEGREES = 1.6
EDGE_SOFTNESS = 1.1
INK_TEXTURE = 0.05

GLYPH_RGB = (238, 232, 213)
OUTLINE_RGB = (24, 18, 12)
BACKGROUND_RGB = (44, 52, 60)
BACKGROUND_EDGE_RGB = (26, 32, 38)
FRAME_RGB = (205, 192, 164)

FRAME_INSET = 10.0
FRAME_RADIUS = 26.0
FRAME_HALF_WIDTH = 2.4

OUT_PATH = (
    Path(__file__).resolve().parents[1]
    / "src" / "ConcernedForeman" / "Package" / "icon.png"
)


# ----------------------------------------------------------------------
# Signed distance primitives (negative inside).
# ----------------------------------------------------------------------

def circle(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) - r


def capsule(x1, y1, x2, y2, r):
    def sdf(x, y):
        px, py = x - x1, y - y1
        bx, by = x2 - x1, y2 - y1
        length_sq = bx * bx + by * by or 1e-9
        t = max(0.0, min(1.0, (px * bx + py * by) / length_sq))
        return math.hypot(px - bx * t, py - by * t) - r

    return sdf


def rounded_rect(cx, cy, half_w, half_h, radius):
    def sdf(x, y):
        qx = abs(x - cx) - (half_w - radius)
        qy = abs(y - cy) - (half_h - radius)
        outside = math.hypot(max(qx, 0.0), max(qy, 0.0))
        inside = min(max(qx, qy), 0.0)
        return outside + inside - radius

    return sdf


def union(*shapes):
    return lambda x, y: min(s(x, y) for s in shapes)


# ----------------------------------------------------------------------
# The foreman glyph (256x256 canvas, y down).
# ----------------------------------------------------------------------

def lintel_and_posts():
    """The structure being judged: a beam carried by two posts."""
    beam = capsule(50.0, 60.0, 206.0, 60.0, 13.0)
    left_post = capsule(70.0, 72.0, 70.0, 204.0, 12.0)
    right_post = capsule(186.0, 72.0, 186.0, 204.0, 12.0)
    return union(beam, left_post, right_post)


def plumb():
    """The judgement: a line dropped from the middle of the beam, and the
    bob on the end of it. Deliberately the heaviest shape in the icon."""
    anchor = circle(128.0, 70.0, 6.5)
    line = capsule(128.0, 72.0, 128.0, 122.0, 2.6)
    bob_neck = capsule(128.0, 118.0, 128.0, 142.0, 8.0)
    bob_head = circle(128.0, 154.0, 21.0)
    bob_tip = capsule(128.0, 170.0, 128.0, 190.0, 4.8)
    return union(anchor, line, bob_neck, bob_head, bob_tip)


def foreman_glyph():
    return union(lintel_and_posts(), plumb())


# ----------------------------------------------------------------------
# Hand-drawn pass: deterministic per-glyph imperfection, seeded by the
# product name.
# ----------------------------------------------------------------------

def glyph_seed(name):
    """Stable across runs and Python versions (never the builtin hash)."""
    return zlib.crc32(name.encode("utf-8"))


def hand_drawn(sdf, name):
    seed = glyph_seed(name)
    phases = [((seed >> (i * 5)) % 977) / 977.0 * 2.0 * math.pi for i in range(6)]
    tilt = math.radians((((seed >> 11) % 1009) / 1009.0 * 2.0 - 1.0) * MAX_TILT_DEGREES)
    cos_t, sin_t = math.cos(tilt), math.sin(tilt)

    def warped(x, y):
        cx, cy = x - 128.0, y - 128.0
        rx = 128.0 + cx * cos_t - cy * sin_t
        ry = 128.0 + cx * sin_t + cy * cos_t

        dx = WOBBLE_AMPLITUDE * (
            0.62 * math.sin(ry * WOBBLE_FREQUENCY + phases[0])
            + 0.38 * math.sin((rx + ry) * WOBBLE_FREQUENCY * 0.71 + phases[1]))
        dy = WOBBLE_AMPLITUDE * (
            0.62 * math.sin(rx * WOBBLE_FREQUENCY + phases[2])
            + 0.38 * math.sin((rx - ry) * WOBBLE_FREQUENCY * 0.83 + phases[3]))
        return sdf(rx + dx, ry + dy)

    return warped, phases


def ink_shade(x, y, phases):
    wave = (
        math.sin(x * 0.115 + phases[4]) * math.sin(y * 0.099 + phases[5])
        + 0.5 * math.sin((x + y) * 0.054 + phases[0]))
    return 1.0 + INK_TEXTURE * wave


# ----------------------------------------------------------------------
# Rasterization + PNG writing.
# ----------------------------------------------------------------------

def soft_coverage(d):
    if d <= -EDGE_SOFTNESS:
        return 1.0
    if d >= EDGE_SOFTNESS:
        return 0.0
    return 0.5 - d / (2.0 * EDGE_SOFTNESS)


def background_color(x, y):
    """Opaque slate background with a soft radial vignette."""
    distance = math.hypot(x - 128.0, y - 128.0) / 181.0
    blend = min(1.0, distance * distance * 1.35)
    return tuple(
        BACKGROUND_RGB[i] + (BACKGROUND_EDGE_RGB[i] - BACKGROUND_RGB[i]) * blend
        for i in range(3)
    )


def render():
    glyph, phases = hand_drawn(foreman_glyph(), "concerned-foreman")
    half = 128.0 - FRAME_INSET
    frame_rect = rounded_rect(128.0, 128.0, half, half, FRAME_RADIUS)
    step = 1.0 / SUPER
    offset = step / 2.0
    rows = []
    for py in range(SIZE):
        row = bytearray()
        for px in range(SIZE):
            fill = 0.0
            outline = 0.0
            frame = 0.0
            for sy in range(SUPER):
                for sx in range(SUPER):
                    sample_x = px + offset + sx * step
                    sample_y = py + offset + sy * step
                    d = glyph(sample_x, sample_y)
                    fill_cov = soft_coverage(d)
                    band_cov = soft_coverage(d - OUTLINE_WIDTH)
                    fill += fill_cov
                    outline += band_cov - fill_cov
                    frame += soft_coverage(abs(frame_rect(sample_x, sample_y)) - FRAME_HALF_WIDTH)
            total = SUPER * SUPER
            fill /= total
            outline /= total
            frame /= total

            r, g, b = background_color(px, py)
            if frame > 0.0:
                r += (FRAME_RGB[0] - r) * frame
                g += (FRAME_RGB[1] - g) * frame
                b += (FRAME_RGB[2] - b) * frame
            if outline > 0.0:
                r += (OUTLINE_RGB[0] - r) * outline
                g += (OUTLINE_RGB[1] - g) * outline
                b += (OUTLINE_RGB[2] - b) * outline
            if fill > 0.0:
                shade = ink_shade(px, py, phases)
                r += (min(255.0, GLYPH_RGB[0] * shade) - r) * fill
                g += (min(255.0, GLYPH_RGB[1] * shade) - g) * fill
                b += (min(255.0, GLYPH_RGB[2] * shade) - b) * fill
            row.extend((round(r), round(g), round(b), 255))
        rows.append(bytes(row))
    return rows


def write_png(path, rows):
    raw = b"".join(b"\x00" + row for row in rows)

    def chunk(tag, data):
        payload = tag + data
        return struct.pack(">I", len(data)) + payload + struct.pack(">I", zlib.crc32(payload))

    header = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


def main():
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    write_png(OUT_PATH, render())
    print(f"wrote {OUT_PATH} ({SIZE}x{SIZE})")


if __name__ == "__main__":
    main()
