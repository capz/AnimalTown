#!/usr/bin/env python3
"""Verify cooked closed buildings equal exterior plus the zero-angle door.

Usage: python3 tests/building-closed-equivalence.py [cooked-directory] [repo]
The default cooked directory is out/disc under the repository containing this
script. This reads packages only; no assets or generated outputs are changed.
"""
import json
from pathlib import Path
import struct
import sys


def read_model(path):
    data = path.read_bytes()
    assert len(data) >= 160, f"{path}: truncated DNMD header"
    magic, version, length, count = struct.unpack_from(">4I", data)
    assert magic == 0x444E444D and version in (1, 2), f"{path}: invalid DNMD format"
    assert length == len(data) and count == 6, f"{path}: invalid DNMD envelope"
    sections = {}
    for index in range(6):
        kind, start, size, _ = struct.unpack_from(">4I", data, 16 + index * 16)
        assert kind == index + 1 and start >= 160 and start % 32 == 0, (
            f"{path}: invalid section {kind} offset"
        )
        assert size >= 4 and start + size <= len(data), f"{path}: truncated section {kind}"
        sections[kind] = data[start:start + size]

    geometry = sections[2]
    assert struct.unpack_from(">I", geometry)[0] == 1, f"{path}: expected one static mesh"
    assert len(geometry) >= 48, f"{path}: truncated mesh header"
    node, skin, material, vertices, indices = struct.unpack_from(">5I", geometry, 4)
    assert node == 0 and skin == 0xFFFFFFFF, f"{path}: expected one unskinned root mesh"
    assert vertices > 0 and indices > 0 and indices % 3 == 0, f"{path}: invalid mesh counts"
    assert 48 + vertices * 80 + indices * 4 == len(geometry), f"{path}: invalid mesh spans"
    records = [geometry[48 + i * 80:48 + (i + 1) * 80] for i in range(vertices)]
    order = struct.unpack_from(f">{indices}I", geometry, 48 + vertices * 80)
    assert all(index < vertices for index in order), f"{path}: out-of-range mesh index"
    return [records[index] for index in order], sections, material


def float32(value):
    return struct.unpack(">f", struct.pack(">f", value))[0]


def socket(value):
    # Match JSON GetSingle followed by the catalog/cooker's 1/1024 m truncation.
    return int(float32(value) * 1024) / 1024


def check(cooked, repo):
    definitions = sorted((repo / "assets/definitions/buildings").glob("*.json"))
    assert definitions, "No building definitions found"
    total = 0
    for definition in definitions:
        building = json.loads(definition.read_text())
        prefix = building["prefix"]
        hinge_x, hinge_z = socket(building["hinge"][0]), socket(building["hinge"][2])
        body, body_sections, body_material = read_model(cooked / f"{prefix}-exterior-tex128.dndmodel")
        door, door_sections, door_material = read_model(cooked / f"{prefix}-door-tex128.dndmodel")
        closed, closed_sections, closed_material = read_model(cooked / f"{prefix}-exterior-closed-tex128.dndmodel")
        translated = []
        for vertex in door:
            x, y, z = struct.unpack_from(">3f", vertex)
            # Packing rounds each translated position exactly as the float32
            # cooker does; all other vertex attributes must remain byte exact.
            translated.append(struct.pack(">3f", x + hinge_x, y, z + hinge_z) + vertex[12:])
        assert closed == body + translated, f"{prefix}: expanded closed geometry differs from exterior + door"
        assert body_material == door_material == closed_material, f"{prefix}: mesh material indices differ"
        for kind, name in ((3, "materials"), (4, "textures")):
            assert body_sections[kind] == door_sections[kind] == closed_sections[kind], (
                f"{prefix}: cooked {name} differ"
            )
        triangles = len(closed) // 3
        total += triangles
        print(f"{prefix}: {triangles} closed triangles preserve every vertex byte, material and texture")
    print(f"Closed building equivalence passed: {len(definitions)} kits, {total} triangles, quantized hinges")


if __name__ == "__main__":
    if len(sys.argv) > 3:
        raise SystemExit(__doc__.strip())
    root = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else Path(__file__).resolve().parents[1]
    directory = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else root / "out/disc"
    check(directory, root)
