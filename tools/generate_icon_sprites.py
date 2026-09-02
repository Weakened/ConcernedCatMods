#!/usr/bin/env python3
"""Deterministically renders the Concerned Cartographer cc:* map-marker
sprites into src/ConcernedCartographer/Assets/Icons/*.png.

Pure standard library: a tiny signed-distance-field rasterizer plus a
minimal PNG writer, so the sprites are reproducible from source with no
image-editing tools, no external assets, and no licensing questions.
Style matches vanilla pin readability: warm parchment glyph, near-black
outline, transparent background, 48x48 with 3x3 supersampling.

RC10 (feedback 13): the glyphs lean toward Valheim's hand-drawn map-icon
language — every shape passes through a deterministic per-glyph domain
wobble (low-frequency sine displacement seeded from the sprite name), a
tiny seeded rotation, soft antialiased edges, and a faint ink-texture
modulation of the fill. Silhouettes are unchanged and stay mutually
distinct; regeneration is byte-for-byte reproducible.

Run from the repository root:
    python ./tools/generate_icon_sprites.py
"""

from __future__ import annotations

import math
import struct
import zlib
from pathlib import Path

SIZE = 48
SUPER = 3  # 3x3 samples per pixel
OUTLINE_WIDTH = 2.3

# Hand-drawn pass tuning (RC10): keep amplitudes small enough that every
# silhouette reads exactly as before at map size.
WOBBLE_AMPLITUDE = 0.85
WOBBLE_FREQUENCY = 0.33
MAX_TILT_DEGREES = 2.4
EDGE_SOFTNESS = 0.55
INK_TEXTURE = 0.055

GLYPH_RGB = (238, 232, 213)
OUTLINE_RGB = (24, 18, 12)

OUT_DIR = Path(__file__).resolve().parents[1] / "src" / "ConcernedCartographer" / "Assets" / "Icons"


# ----------------------------------------------------------------------
# Signed distance primitives (negative inside).
# ----------------------------------------------------------------------

def circle(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) - r


def ring(cx, cy, r, half_width):
    return lambda x, y: abs(math.hypot(x - cx, y - cy) - r) - half_width


def capsule(x1, y1, x2, y2, r):
    def sdf(x, y):
        px, py = x - x1, y - y1
        bx, by = x2 - x1, y2 - y1
        length_sq = bx * bx + by * by or 1e-9
        t = max(0.0, min(1.0, (px * bx + py * by) / length_sq))
        return math.hypot(px - bx * t, py - by * t) - r

    return sdf


def arc(cx, cy, r, half_width, deg_from, deg_to):
    """Ring restricted to [deg_from, deg_to] (degrees, 0=east, CCW, y up)."""

    def sdf(x, y):
        dx, dy = x - cx, cy - y  # flip to y-up for the angle test
        ang = math.degrees(math.atan2(dy, dx)) % 360.0
        lo, hi = deg_from % 360.0, deg_to % 360.0
        inside = lo <= ang <= hi if lo <= hi else (ang >= lo or ang <= hi)
        band = abs(math.hypot(dx, dy) - r) - half_width
        if inside:
            return band
        # Distance to the nearest arc endpoint keeps ends rounded.
        best = 1e9
        for deg in (deg_from, deg_to):
            ex = cx + r * math.cos(math.radians(deg))
            ey = cy - r * math.sin(math.radians(deg))
            best = min(best, math.hypot(x - ex, y - ey) - half_width)
        return best

    return sdf


def polygon(points):
    """Filled polygon SDF (even-odd), points in draw order."""

    def sdf(x, y):
        n = len(points)
        dist = 1e9
        inside = False
        j = n - 1
        for i in range(n):
            xi, yi = points[i]
            xj, yj = points[j]
            ex, ey = xj - xi, yj - yi
            px, py = x - xi, y - yi
            length_sq = ex * ex + ey * ey or 1e-9
            t = max(0.0, min(1.0, (px * ex + py * ey) / length_sq))
            dist = min(dist, math.hypot(px - ex * t, py - ey * t))
            if (yi > y) != (yj > y) and x < xi + ex * (y - yi) / (yj - yi or 1e-9):
                inside = not inside
            j = i
        return -dist if inside else dist

    return sdf


def star(cx, cy, r_outer, r_inner, points=5, rotation_deg=-90.0):
    verts = []
    for i in range(points * 2):
        r = r_outer if i % 2 == 0 else r_inner
        ang = math.radians(rotation_deg + i * 180.0 / points)
        verts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    return polygon(verts)


def union(*shapes):
    return lambda x, y: min(s(x, y) for s in shapes)


def subtract(base, cut):
    return lambda x, y: max(base(x, y), -cut(x, y))


# ----------------------------------------------------------------------
# The twelve glyphs (48x48 canvas, y down, centered on 24,24).
# ----------------------------------------------------------------------

def glyph_road():
    # Four-way junction: bold crossing with rounded arms.
    return union(
        capsule(24, 8, 24, 40, 4.4),
        capsule(8, 24, 40, 24, 4.4),
    )


def glyph_harbor():
    # Anchor: top ring, shaft, stock, bottom arc with upturned flukes.
    return union(
        ring(24, 11, 4.2, 2.2),
        capsule(24, 15, 24, 35, 2.4),
        capsule(15.5, 20.5, 32.5, 20.5, 2.2),
        arc(24, 26, 11.5, 2.4, 200, 340),
        capsule(12.8, 29.5, 12.8, 25.5, 2.2),
        capsule(35.2, 29.5, 35.2, 25.5, 2.2),
    )


def glyph_resource():
    # Cut gem: flat top, pointed bottom, girdle line.
    gem = polygon([(14.5, 11.5), (33.5, 11.5), (41.5, 21.0), (24.0, 41.0), (6.5, 21.0)])
    return subtract(gem, capsule(5.0, 21.0, 43.0, 21.0, 1.3))


def glyph_danger():
    # Warning triangle with an exclamation mark.
    tri = polygon([(24, 7.0), (43.0, 40.0), (5.0, 40.0)])
    tri_hole = polygon([(24, 13.6), (38.2, 37.2), (9.8, 37.2)])
    frame = subtract(tri, tri_hole)
    return union(
        frame,
        capsule(24, 19.5, 24, 28.5, 2.5),
        circle(24, 34.2, 2.6),
    )


def glyph_farm():
    # Sprout: stem with two round teardrop leaves.
    left_leaf = union(
        circle(14.5, 16.5, 6.0),
        polygon([(24.0, 24.0), (10.5, 20.0), (15.5, 11.5)]),
    )
    right_leaf = union(
        circle(33.5, 16.5, 6.0),
        polygon([(24.0, 24.0), (37.5, 20.0), (32.5, 11.5)]),
    )
    return union(
        capsule(24, 41, 24, 21.5, 2.4),
        left_leaf,
        right_leaf,
    )


def glyph_mine():
    # Pickaxe: vertical handle, curved head across the top.
    return union(
        capsule(24, 15, 24, 41, 2.7),
        arc(24, 33, 19.0, 3.0, 42, 138),
    )


def glyph_fishing():
    # Fish: body, tail, eye.
    body = polygon([
        (8.5, 24), (14, 17.5), (21, 14.5), (28, 15.5), (33, 19.5),
        (35.5, 24), (33, 28.5), (28, 32.5), (21, 33.5), (14, 30.5),
    ])
    tail = polygon([(33.5, 24), (43.5, 15.5), (41.0, 24), (43.5, 32.5)])
    return subtract(union(body, tail), circle(15.5, 22.3, 1.9))


def glyph_camp():
    # Tent: two slanted sides, ground line, door poles.
    return union(
        capsule(24, 9.5, 7.5, 38.5, 2.6),
        capsule(24, 9.5, 40.5, 38.5, 2.6),
        capsule(6.5, 38.5, 41.5, 38.5, 2.2),
        capsule(24, 25, 18.5, 38.5, 1.7),
        capsule(24, 25, 29.5, 38.5, 1.7),
    )


def glyph_travel():
    # Bold compass-style arrow to the north-east.
    return union(
        capsule(11.5, 36.5, 27.0, 21.0, 3.2),
        polygon([(19.5, 11.0), (37.0, 11.0), (37.0, 28.5), (29.8, 21.3), (26.8, 18.2)]),
    )


def glyph_trader():
    # Coin purse: round bag with a tied neck and a dark coin slot.
    bag = union(
        circle(24, 29, 11.8),
        polygon([(17.5, 13.5), (30.5, 13.5), (27.0, 19.5), (21.0, 19.5)]),
        capsule(16.5, 18.5, 31.5, 18.5, 2.4),
    )
    return subtract(bag, capsule(19.0, 29.5, 29.0, 29.5, 1.7))


def glyph_dungeon():
    # Cave mouth: arch with legs and a ground line.
    return union(
        arc(24, 27, 12.0, 2.7, 0, 180),
        capsule(12.0, 27, 12.0, 37.5, 2.7),
        capsule(36.0, 27, 36.0, 37.5, 2.7),
        capsule(7.0, 38.5, 41.0, 38.5, 2.1),
        capsule(24, 27.5, 24, 34.0, 2.1),
    )


def glyph_objective():
    # Five-point star.
    return star(24, 25, 17.5, 7.0)


GLYPHS = {
    "cc-road": glyph_road,
    "cc-harbor": glyph_harbor,
    "cc-resource": glyph_resource,
    "cc-danger": glyph_danger,
    "cc-farm": glyph_farm,
    "cc-mine": glyph_mine,
    "cc-fishing": glyph_fishing,
    "cc-camp": glyph_camp,
    "cc-travel": glyph_travel,
    "cc-trader": glyph_trader,
    "cc-dungeon": glyph_dungeon,
    "cc-objective": glyph_objective,
}


# ----------------------------------------------------------------------
# Hand-drawn pass (RC10): deterministic per-glyph imperfection.
# ----------------------------------------------------------------------

def glyph_seed(name):
    """Stable across runs and Python versions (never the builtin hash)."""
    return zlib.crc32(name.encode("utf-8"))


def hand_drawn(sdf, name):
    """Wraps an SDF in a seeded tiny rotation plus a low-frequency domain
    wobble, so edges undulate gently like brush strokes while the shape
    and its silhouette stay put."""
    seed = glyph_seed(name)
    phases = [((seed >> (i * 5)) % 977) / 977.0 * 2.0 * math.pi for i in range(6)]
    tilt = math.radians((((seed >> 11) % 1009) / 1009.0 * 2.0 - 1.0) * MAX_TILT_DEGREES)
    cos_t, sin_t = math.cos(tilt), math.sin(tilt)

    def warped(x, y):
        # Seeded rotation around the canvas center.
        cx, cy = x - 24.0, y - 24.0
        rx = 24.0 + cx * cos_t - cy * sin_t
        ry = 24.0 + cx * sin_t + cy * cos_t

        # Two-octave sine displacement: irregular, but smooth and small.
        dx = WOBBLE_AMPLITUDE * (
            0.62 * math.sin(ry * WOBBLE_FREQUENCY + phases[0])
            + 0.38 * math.sin((rx + ry) * WOBBLE_FREQUENCY * 0.71 + phases[1]))
        dy = WOBBLE_AMPLITUDE * (
            0.62 * math.sin(rx * WOBBLE_FREQUENCY + phases[2])
            + 0.38 * math.sin((rx - ry) * WOBBLE_FREQUENCY * 0.83 + phases[3]))
        return sdf(rx + dx, ry + dy)

    return warped, phases


def ink_shade(x, y, phases):
    """Faint parchment-ink unevenness for the glyph fill."""
    wave = (
        math.sin(x * 0.61 + phases[4]) * math.sin(y * 0.53 + phases[5])
        + 0.5 * math.sin((x + y) * 0.29 + phases[0]))
    return 1.0 + INK_TEXTURE * wave


# ----------------------------------------------------------------------
# Rasterization + PNG writing.
# ----------------------------------------------------------------------

def soft_coverage(d):
    """0..1 coverage with a soft edge band around d = 0."""
    if d <= -EDGE_SOFTNESS:
        return 1.0
    if d >= EDGE_SOFTNESS:
        return 0.0
    return 0.5 - d / (2.0 * EDGE_SOFTNESS)


def render(sdf, name):
    warped, phases = hand_drawn(sdf, name)
    rows = []
    step = 1.0 / SUPER
    offset = step / 2.0
    for py in range(SIZE):
        row = bytearray()
        for px in range(SIZE):
            fill = 0.0
            outline = 0.0
            for sy in range(SUPER):
                for sx in range(SUPER):
                    d = warped(px + offset + sx * step, py + offset + sy * step)
                    fill_cov = soft_coverage(d)
                    band_cov = soft_coverage(d - OUTLINE_WIDTH)
                    fill += fill_cov
                    outline += band_cov - fill_cov
            total = SUPER * SUPER
            fill /= total
            outline /= total
            alpha = fill + outline
            if alpha <= 0.003:
                row.extend((0, 0, 0, 0))
                continue
            shade = ink_shade(px, py, phases)
            fr = min(255.0, GLYPH_RGB[0] * shade)
            fg = min(255.0, GLYPH_RGB[1] * shade)
            fb = min(255.0, GLYPH_RGB[2] * shade)
            r = (fr * fill + OUTLINE_RGB[0] * outline) / alpha
            g = (fg * fill + OUTLINE_RGB[1] * outline) / alpha
            b = (fb * fill + OUTLINE_RGB[2] * outline) / alpha
            row.extend((round(r), round(g), round(b), round(min(1.0, alpha) * 255)))
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
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name, factory in sorted(GLYPHS.items()):
        write_png(OUT_DIR / f"{name}.png", render(factory(), name))
        print(f"wrote {name}.png")
    print(f"{len(GLYPHS)} sprites in {OUT_DIR}")


if __name__ == "__main__":
    main()
