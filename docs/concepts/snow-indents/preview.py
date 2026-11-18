"""Render the RGB565 experiment at both of Animal Town's shadow resolutions."""
from math import exp, floor, sin
from pathlib import Path
from random import Random

from PIL import Image, ImageDraw, ImageFont

SPAN = 8
SIZE = 320
GAP = 16
PADDING = 20
TOP = 90
MARKS = (
    (2.1, 1.4, .42, .63), (2.8, 2.5, .42, .63), (2.3, 3.6, .42, .63),
    (3.0, 4.7, .42, .63), (2.5, 5.8, .42, .63), (3.2, 6.9, .42, .63),
    (6.0, 4.6, 1.55, 1.25),
)


def depth(x, z):
    depth = max(0, *(1 - ((x % SPAN - cx) / rx) ** 2 - ((z % SPAN - cz) / rz) ** 2
                     for cx, cz, rx, rz in MARKS))
    return int(depth * depth * 63 + .5)


def channels(interior):
    page = []
    step = SPAN / interior
    for row in range(interior + 2):
        z = (row - .5) * step
        line = []
        for col in range(interior + 2):
            x = (col - .5) * step
            shadow = .20 * exp(-(((x - 4.6) / 3.7) ** 2 + ((z - 3.8) / 1.1) ** 2))
            lamp = .32 * exp(-(((x - 5.6) / 1.4) ** 2 + ((z - 5.5) / 1.3) ** 2))
            red = (round(shadow * 255 / 66 * 15) * 31 + 7) // 15
            blue = (round(lamp * 15) * 31 + 7) // 15
            line.append((red << 11) | (depth(x, z) << 5) | blue)
        page.append(line)
    return tuple([[(texel >> shift & mask) / mask for texel in row] for row in page]
                 for shift, mask in ((11, 31), (5, 63), (0, 31)))


def sample(page, x, z, step):
    u, v = x / step + .5, z / step + .5
    c, r = floor(u), floor(v)
    tx, tz = u - c, v - r
    return ((page[r][c] * (1 - tx) + page[r][c + 1] * tx) * (1 - tz)
            + (page[r + 1][c] * (1 - tx) + page[r + 1][c + 1] * tx) * tz)


def render(interior, relief=True):
    shadow, snow, lamp = channels(interior)
    step = SPAN / interior
    image = Image.new("RGB", (SIZE, SIZE))
    pixels = image.load()
    grain = Random(42)
    for row in range(SIZE):
        z = (row + .5) / SIZE * SPAN
        for col in range(SIZE):
            x = (col + .5) / SIZE * SPAN
            shade = 0
            if relief:
                dx, dz = .4 * step, .3 * step
                shade = (sample(snow, x - dx, z - dz, step)
                         - sample(snow, x + dx, z + dz, step)) * 127.5
            opacity = sample(shadow, x, z, step) * (66 / 255)
            light = sample(lamp, x, z, step) * .32
            base = 219 + grain.uniform(-2.5, 2.5) + 1.5 * sin(x * 11 + z * 5)
            pixels[col, row] = tuple(
                min(255, round(min(255, base + offset + shade) * (1 - opacity)
                               + tint * opacity + light * glow))
                for offset, tint, glow in zip((-8, 3, 15), (35, 49, 66), (220, 158, 74))
            )
    return image


def main():
    canvas = Image.new("RGB", (3 * SIZE + 2 * GAP + 2 * PADDING, TOP + SIZE + PADDING),
                       "#172330")
    draw = ImageDraw.Draw(canvas)
    draw.text((PADDING, 15), "Snow relief from the shadow map", fill="#f5f8fa",
              font=ImageFont.load_default(size=25))
    for index, (interior, relief, title, subtitle) in enumerate((
        (30, False, "Flat snow", "Shadow and lamp only"),
        (30, True, "Detailed shadow page", "RGB565 depth at 27 cm per texel"),
        (8, True, "Outdoor fallback", "RGB565 depth at 1 m per texel"),
    )):
        x = PADDING + index * (SIZE + GAP)
        draw.text((x, 48), title, fill="#f5f8fa", font=ImageFont.load_default(size=18))
        draw.text((x, 70), subtitle, fill="#bed0e4", font=ImageFont.load_default(size=14))
        canvas.paste(render(interior, relief), (x, TOP))
    canvas.save(Path(__file__).with_name("comparison.png"))


if __name__ == "__main__":
    main()
