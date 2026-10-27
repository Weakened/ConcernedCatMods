#!/usr/bin/env python3
"""Render the original Concerned Ladders package mark. No imported game art."""
from pathlib import Path
from PIL import Image, ImageDraw


def main() -> None:
    scale = 4
    image = Image.new("RGB", (256 * scale, 256 * scale), "#172b2a")
    draw = ImageDraw.Draw(image)

    def points(values):
        return [(int(x * scale), int(y * scale)) for x, y in values]

    draw.ellipse((32 * scale, 26 * scale, 224 * scale, 218 * scale), fill="#23423b")
    draw.polygon(points([(45, 213), (80, 30), (99, 30), (64, 213)]), fill="#c79358")
    draw.polygon(points([(147, 213), (182, 30), (201, 30), (166, 213)]), fill="#c79358")
    for x1, y, x2 in [(73, 58, 185), (67, 92, 179), (60, 126, 172), (54, 160, 166), (47, 194, 159)]:
        draw.line(points([(x1, y), (x2, y)]), fill="#e5c490", width=13 * scale)
    draw.ellipse((169 * scale, 174 * scale, 209 * scale, 213 * scale), fill="#111d20")
    draw.polygon(points([(170, 185), (171, 162), (189, 175), (207, 162), (208, 185)]), fill="#111d20")
    for p in [[(178, 186), (184, 189)], [(194, 189), (200, 186)]]:
        draw.line(points(p), fill="#e2bb72", width=3 * scale)
    target = Path(__file__).resolve().parents[1] / "src/ConcernedLadders/Package/icon.png"
    image.resize((256, 256), Image.Resampling.LANCZOS).save(target)


if __name__ == "__main__":
    main()
