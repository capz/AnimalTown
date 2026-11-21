"""Bake the helipad SVG's axis-aligned polygons into non-overlapping GX quads."""

import argparse
from dataclasses import dataclass
from pathlib import Path
import xml.etree.ElementTree as ET


@dataclass(frozen=True)
class Panel:
    left: int
    top: int
    right: int
    bottom: int
    color: str


def read_art(path):
    root = ET.parse(path).getroot()
    if root.get("viewBox") != "0 0 400 300":
        raise ValueError("The helipad art must match the four-by-three tile footprint")
    panels = []
    for group in root.findall("{*}g"):
        for quad in group.findall("{*}polygon"):
            a, b, c, d = [
                tuple(map(int, point.split(","))) for point in quad.attrib["points"].split()
            ]
            left, top = a
            right, bottom = c
            if b != (right, top) or d != (left, bottom) or left >= right or top >= bottom:
                raise ValueError(f"Expected a clockwise rectangle, got {quad.attrib['points']}")
            color = quad.get("fill", group.get("fill"))
            if color is None or len(color) != 7 or not color.startswith("#"):
                raise ValueError(f"Expected a six-digit SVG fill, got {color}")
            panels.append(Panel(left, top, right, bottom, color))
    return panels


def flatten(panels):
    # Split the deck at each horizontal edge, then resolve fills from back to front.
    # Neighboring strips of the same color share a quad wherever their edges match.
    levels = sorted({y for p in panels for y in (p.top, p.bottom)})
    active = {}
    output = []
    for top, bottom in zip(levels, levels[1:]):
        covering = [p for p in panels if p.top <= top and bottom <= p.bottom]
        edges = sorted({x for p in covering for x in (p.left, p.right)})
        runs = []
        for left, right in zip(edges, edges[1:]):
            color = next(p.color for p in reversed(covering) if p.left <= left and right <= p.right)
            if runs and runs[-1][2] == color:
                runs[-1] = (runs[-1][0], right, color)
            else:
                runs.append((left, right, color))
        continued = {run: active.pop(run, top) for run in runs}
        output.extend(Panel(left, start, right, top, color) for (left, right, color), start in active.items())
        active = continued
    output.extend(Panel(left, start, right, levels[-1], color) for (left, right, color), start in active.items())
    return sorted(output, key=lambda p: (p.top, p.left))


def bake(root):
    panels = flatten(read_art(root / "assets/models/helipad/deck.svg"))
    colors = {color: i for i, color in enumerate(sorted({p.color for p in panels}))}
    lines = [
        "// Generated from assets/models/helipad/deck.svg by tools/helipad-vector.py.",
        "using Dolphin.Graphics;",
        "",
        "internal sealed partial class HelipadRenderer",
        "{",
        "    private static void AddDeck(Geometry geometry)",
        "    {",
    ]
    for color, index in colors.items():
        rgb = ", ".join(str(int(color[start:start + 2], 16)) for start in (1, 3, 5))
        lines.append(f"        var color{index} = new Color32({rgb});")
    lines.append("")
    for p in panels:
        lines.append(f"        geometry.DeckQuad({p.left}, {p.top}, {p.right}, {p.bottom}, color{colors[p.color]});")
    lines.extend(["    }", "}", ""])
    return "\n".join(lines)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify the checked-in game geometry")
    options = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    destination = root / "game/AnimalTown.GameCube/HelipadRenderer.Deck.cs"
    geometry = bake(root)
    if options.check:
        if destination.read_text(encoding="utf-8") != geometry:
            raise SystemExit("Run python3 tools/helipad-vector.py to update the game geometry")
    else:
        destination.write_text(geometry, encoding="utf-8", newline="\n")
