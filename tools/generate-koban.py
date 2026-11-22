#!/usr/bin/env python3
"""Rebuild AnimalTown's approved, original low-poly koban kit.

    python tools/generate-koban.py [ROOT]
    blender --background --python tools/generate-koban.py -- --render ROOT OUTPUT

Authoring uses only Python's standard library. The optional preview command uses
Blender to import the actual GLBs. Models follow BuildingAssets' narrow static
glTF contract: one mesh, node, primitive, material and embedded opaque PNG; no
indices, transforms, animations or skins. Coordinates are metres, +Y up, +Z
front. Interior X/Z coordinates are normalized to the shared 4m shell and are
scaled to 6m by BuildingRenderer. Fixtures below are in real room metres.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import struct
import sys
import zlib

PALETTE = {
    "stone": (166, 158, 153), "stone_dark": (132, 130, 132),
    "cream": (238, 219, 183), "cream_shadow": (207, 185, 151),
    "roof": (76, 89, 112), "roof_light": (88, 102, 126),
    "teal": (53, 112, 111), "teal_light": (79, 141, 132),
    "wood": (151, 98, 52), "wood_light": (187, 132, 72),
    "wood_dark": (112, 75, 45), "glass": (231, 181, 99),
    "gold": (218, 161, 55), "gold_light": (250, 205, 100),
    "red": (202, 39, 46), "red_light": (244, 67, 65),
    "tile": (232, 210, 173), "tile_dark": (209, 180, 135),
    "paper": (255, 242, 209), "ink": (61, 74, 66),
    "leaf": (117, 147, 90), "wainscot": (115, 151, 136),
    "blue": (137, 167, 177), "parcel": (213, 169, 101),
}
SWATCHES = {name: (i % 8 * 16, i // 8 * 16, 16, 16)
            for i, name in enumerate(PALETTE)}
DECALS = {"sign": (0, 48, 128, 32), "book": (0, 80, 48, 24),
          "map": (48, 80, 48, 48), "clock": (96, 80, 32, 32),
          "notice": (0, 104, 32, 24), "tiles": (96, 112, 32, 16),
          "facade": (32, 104, 16, 24)}
FIXTURES = [
    {"x": .35, "z": 1.45, "width": 2.2, "depth": .95, "height": 1.12, "kind": 0},
    {"x": 3.65, "z": .3, "width": 2., "depth": .8, "height": 2.35, "kind": 0},
    {"x": .35, "z": .3, "width": .75, "depth": .75, "height": 1.35, "kind": 0},
    {"x": 4.9, "z": 3.35, "width": .7, "depth": 1.25, "height": .65, "kind": 0},
]


def atlas_png():
    width = height = 128
    pixels = bytearray(PALETTE["cream"] * (width * height))

    def pixel(x, y, color):
        if 0 <= x < width and 0 <= y < height:
            p = (y * width + x) * 3
            pixels[p:p + 3] = bytes(color)

    def rect(x, y, w, h, color):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                pixel(xx, yy, color)

    def line(x0, y0, x1, y1, color, thick=1):
        count = max(abs(x1 - x0), abs(y1 - y0), 1)
        for k in range(count + 1):
            x, y = round(x0 + (x1-x0)*k/count), round(y0 + (y1-y0)*k/count)
            rect(x, y, thick, thick, color)

    def disc(cx, cy, r, color):
        for y in range(cy-r, cy+r+1):
            for x in range(cx-r, cx+r+1):
                if (x-cx)**2 + (y-cy)**2 <= r*r:
                    pixel(x, y, color)

    for name, (x, y, w, h) in SWATCHES.items():
        rect(x, y, w, h, PALETTE[name])
    # Deliberately drawn glyphs avoid an external font dependency and remain
    # legible in the console's 64px and 32px texture variants.
    glyphs = {"K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
              "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
              "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
              "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
              "N": ["10001", "11001", "11001", "10101", "10011", "10011", "10001"]}
    rect(0, 48, 128, 32, PALETTE["cream"])
    for j, character in enumerate("KOBAN"):
        for y, row in enumerate(glyphs[character]):
            for x, bit in enumerate(row):
                if bit == "1":
                    rect(4 + j*24 + x*4, 50+y*4, 4, 4, PALETTE["ink"])
    # Open service ledger.
    rect(0, 80, 48, 24, PALETTE["teal"])
    rect(2, 82, 44, 20, PALETTE["paper"])
    line(24, 83, 24, 100, PALETTE["cream_shadow"])
    for y in (86, 90, 94, 98):
        line(5, y, 20, y, PALETTE["cream_shadow"])
        line(28, y, 42, y, PALETTE["cream_shadow"])
    # Original miniature town map, not a screenshot or copied game asset.
    rect(48, 80, 48, 48, PALETTE["cream"])
    rect(51, 83, 42, 42, (173, 190, 126))
    for y in range(84, 125):
        river = 77 + round(4 * math.sin(y / 8))
        rect(river, y, 4, 1, PALETTE["blue"])
    line(57, 111, 87, 111, PALETTE["paper"], 3)
    line(65, 89, 65, 119, PALETTE["paper"], 3)
    for x, y in ((58, 92), (70, 100), (82, 117)):
        rect(x, y, 5, 5, PALETTE["cream"])
        rect(x-1, y-2, 7, 3, PALETTE["wood_light"])
    for x, y in ((56, 86), (86, 89), (55, 119), (87, 105)):
        disc(x, y, 3, PALETTE["leaf"])
    # Fixed decorative clock; no implication that it is the live game clock.
    rect(96, 80, 32, 32, PALETTE["cream"])
    disc(112, 96, 15, PALETTE["wood_dark"])
    disc(112, 96, 13, PALETTE["wood_light"])
    disc(112, 96, 11, PALETTE["paper"])
    for tick in range(12):
        angle = tick * math.tau / 12
        x, y = round(112+9*math.sin(angle)), round(96-9*math.cos(angle))
        pixel(x, y, PALETTE["ink"])
    line(112, 96, 106, 92, PALETTE["ink"], 1)
    line(112, 96, 114, 87, PALETTE["ink"], 1)
    # Noticeboard sheets.
    rect(0, 104, 32, 24, PALETTE["leaf"])
    rect(3, 106, 14, 18, PALETTE["paper"])
    rect(18, 111, 11, 15, PALETTE["cream"])
    disc(10, 111, 3, PALETTE["gold"])
    for y in (117, 120):
        line(6, y, 14, y, PALETTE["cream_shadow"])
    for y in (116, 119, 122):
        line(20, y, 26, y, PALETTE["cream_shadow"])
    # A painted base band replaces a second ring of wall polygons.
    rect(32, 104, 16, 24, PALETTE["stone"])
    rect(32, 125, 16, 3, PALETTE["stone_dark"])
    # A 2x2 tile patch retains clear centres after the console downsamples it.
    # Nine repeated quads produce the complete 6x6 room checkerboard.
    for y in range(2):
        for x in range(2):
            rect(96+x*16,112+y*8,16,8,PALETTE["tile_dark" if (x+y)%2 else "tile"])
    rows = b"".join(b"\x00" + pixels[y*width*3:(y+1)*width*3] for y in range(height))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind+data) & 0xffffffff)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def uv_rect(name, inset=3):
    x, y, w, h = SWATCHES[name] if name in SWATCHES else DECALS[name]
    if name in DECALS:
        inset = .5
    return ((x+inset)/128, (y+inset)/128, (x+w-inset)/128, (y+h-inset)/128)


class Mesh:
    def __init__(self):
        self.positions, self.normals, self.uvs = [], [], []

    def triangle(self, a, b, c, color, uvs=None):
        ab, ac = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
        n = (ab[1]*ac[2]-ab[2]*ac[1], ab[2]*ac[0]-ab[0]*ac[2], ab[0]*ac[1]-ab[1]*ac[0])
        length = math.sqrt(sum(v*v for v in n))
        if length < 1e-9:
            raise ValueError("Degenerate koban triangle")
        # Coplanar authored faces share exact normals, including +0, so the
        # lossless derived cooker can share their vertices across triangles.
        n = tuple(0.0 if abs(v/length)<1e-10 else round(v/length,10) for v in n)
        if uvs is None:
            u0, v0, u1, v1 = uv_rect(color)
            uvs = (((u0+u1)/2, (v0+v1)/2),)*3 if color in PALETTE else ((u0, v1), (u1, v1), (u1, v0))
        self.positions.extend((a, b, c))
        self.normals.extend((n, n, n))
        self.uvs.extend(uvs)

    def quad(self, a, b, c, d, color):
        u0, v0, u1, v1 = uv_rect(color)
        uv = (((u0+u1)/2, (v0+v1)/2),)*4 if color in PALETTE else ((u0, v1), (u1, v1), (u1, v0), (u0, v0))
        self.triangle(a, b, c, color, uv[:3])
        self.triangle(a, c, d, color, (uv[0], uv[2], uv[3]))

    def box(self, x, y, z, w, h, d, color, bottom=True, hidden=()):
        a,b,c,e=(x,y,z),(x+w,y,z),(x+w,y+h,z),(x,y+h,z)
        f,g,j,k=(x,y,z+d),(x+w,y,z+d),(x+w,y+h,z+d),(x,y+h,z+d)
        faces=((a,e,c,b),(f,g,j,k),(a,f,k,e),(b,c,j,g),(e,k,j,c))
        for index,face in enumerate(faces+(((a,b,g,f),) if bottom else ())):
            if index not in hidden:
                self.quad(*face, color)

    def front(self, x, y, z, w, h, color):
        self.quad((x,y,z),(x+w,y,z),(x+w,y+h,z),(x,y+h,z),color)

    def ring(self, points, lower, upper, color, cap=False):
        for a,b in zip(points, points[1:]+points[:1]):
            self.quad((a[0],lower,a[1]),(b[0],lower,b[1]),(b[0],upper,b[1]),(a[0],upper,a[1]),color)
        if cap:
            center = (sum(p[0] for p in points)/len(points),upper,sum(p[1] for p in points)/len(points))
            for a,b in zip(points, points[1:]+points[:1]):
                self.triangle(center,(a[0],upper,a[1]),(b[0],upper,b[1]),color)

    def append(self, other, transform):
        for i in range(0, len(other.positions), 3):
            self.triangle(*(transform(p) for p in other.positions[i:i+3]), "cream", other.uvs[i:i+3])

    def validate(self, part):
        triangles = len(self.positions)//3
        limit = 800 if part == "exterior" else 600
        if not 0 < triangles <= limit:
            raise ValueError(f"{part}: {triangles} exceeds {limit} triangles")
        minimum = [min(p[i] for p in self.positions) for i in range(3)]
        maximum = [max(p[i] for p in self.positions) for i in range(3)]
        edge, height = (3.3,4) if part == "exterior" else ((1.2,3) if part == "door" else (4.2,3))
        limits = [edge, height, 4 if part == "exterior" else edge]
        for low, high, maximum_allowed in zip(minimum, maximum, limits):
            if low < -.3-1e-5 or high > maximum_allowed+1e-5:
                raise ValueError(f"{part}: invalid bounds {minimum} .. {maximum}")
        return {"triangles": triangles, "vertices": len(self.positions), "min": minimum, "max": maximum}


def circle(x,z,r,n=12):
    return [(x+r*math.sin(i*math.tau/n),z+r*math.cos(i*math.tau/n)) for i in range(n)]


def window():
    m=Mesh()
    m.front(0,0,.055,.48,1.12,"teal")
    m.front(.055,.065,.058,.37,.99,"glass")
    m.front(.22,.065,.064,.04,.99,"teal_light")
    m.front(.055,.51,.066,.37,.055,"teal_light")
    m.box(-.045,-.045,-.005,.57,.10,.105,"cream",bottom=False,hidden=(0,))
    m.box(-.045,1.11,-.005,.57,.10,.105,"cream",bottom=False,hidden=(0,))
    return m


def exterior():
    m=Mesh()
    perimeter=[(.55,3),(2.45,3),(3,2.45),(3,.55),(2.45,0),(.55,0),(0,.55),(0,2.45)]
    # Front opening remains unobstructed when the shared animated door opens.
    for i,(a,b) in enumerate(zip(perimeter,perimeter[1:]+perimeter[:1])):
        if i == 0:
            for lo,hi in ((.55,.95),(2.05,2.45)):
                m.front(lo,0,3,hi-lo,2.26,"facade")
            m.front(.95,2.15,3,1.1,.11,"stone")
        else:
            m.quad((a[0],0,a[1]),(b[0],0,b[1]),(b[0],2.26,b[1]),(a[0],2.26,a[1]),"facade")
    roof_edge=[(1.5+(x-1.5)*1.075,1.5+(z-1.5)*1.075) for x,z in perimeter]
    m.ring(roof_edge,2.24,2.48,"cream")
    # Two faceted courses retain the shallow slate dome within the combined
    # three-building resident budget; no hidden interior roof geometry.
    rings=[(1,2.48),(.64,2.90),(.17,3.035)]
    for (sa,ya),(sb,yb) in zip(rings,rings[1:]):
        for i in range(len(roof_edge)):
            a,b=roof_edge[i],roof_edge[(i+1)%len(roof_edge)]
            outer=lambda p:(1.5+(p[0]-1.5)*sa,ya,1.5+(p[1]-1.5)*sa)
            inner=lambda p:(1.5+(p[0]-1.5)*sb,yb,1.5+(p[1]-1.5)*sb)
            m.quad(outer(a),outer(b),inner(b),inner(a),"roof_light" if i%3==1 else "roof")
    top=[(1.5+(x-1.5)*.17,1.5+(z-1.5)*.17) for x,z in roof_edge]
    for a,b in zip(top,top[1:]+top[:1]):
        m.triangle((1.5,3.035,1.5),(a[0],3.035,a[1]),(b[0],3.035,b[1]),"roof")
    m.ring(circle(1.5,1.5,.25,6),3.035,3.15,"cream",True)
    beacon_lower=circle(1.5,1.5,.185,6)
    beacon_upper=circle(1.5,1.5,.165,6)
    for i in range(6):
        a,b=beacon_lower[i],beacon_lower[(i+1)%6]
        c,d=beacon_upper[(i+1)%6],beacon_upper[i]
        m.quad((a[0],3.15,a[1]),(b[0],3.15,b[1]),(c[0],3.49,c[1]),(d[0],3.49,d[1]),"red_light" if i<3 else "red")
        m.triangle((1.5,3.54,1.5),(d[0],3.49,d[1]),(c[0],3.49,c[1]),"red_light")
    # Cream sign plaque, gold star and atlas lettering.
    outline=[(.80,2.16),(2.20,2.16),(2.20,2.56),(1.75,2.77),
             (1.5,2.82),(1.25,2.77),(.80,2.56)]
    for a,b in zip(outline,outline[1:]+outline[:1]):
        m.triangle((1.5,2.45,3.145),(a[0],a[1],3.145),(b[0],b[1],3.145),"cream")
        m.quad((a[0],a[1],3.05),(b[0],b[1],3.05),(b[0],b[1],3.145),(a[0],a[1],3.145),"cream_shadow")
    m.front(.90,2.20,3.15,1.2,.30,"sign")
    star=[]
    for i in range(10):
        angle=math.pi/2+i*math.pi/5
        radius=.15 if i%2==0 else .071
        star.append((1.5+radius*math.cos(angle),2.64+radius*math.sin(angle),3.18))
    for i in range(10):
        m.triangle((1.5,2.64,3.215),star[i],star[(i+1)%10],"gold_light" if i%2==0 else "gold")
    # The flanking windows follow the rounded front corners instead of floating
    # in a flat facade plane. Their local front is the face's outward normal.
    tangent_x=tangent_z=1/math.sqrt(2)
    m.append(window(),lambda p:(.065+p[0]*tangent_x-p[2]*tangent_z,.66+p[1],2.515+p[0]*tangent_z+p[2]*tangent_x))
    m.append(window(),lambda p:(2.515+p[0]*tangent_x+p[2]*tangent_z,.66+p[1],2.935-p[0]*tangent_z+p[2]*tangent_x))
    m.front(.89,0,3.10,.06,2.16,"cream")
    m.front(2.05,0,3.10,.06,2.16,"cream")
    m.box(.93,-.015,3.10,1.14,.065,.48,"stone",bottom=False,hidden=(0,))
    # A small wall-mounted notice board stays clear of the door approach.
    m.box(.55,.61,3.006,.32,.43,.055,"wood_light",bottom=False,hidden=(0,))
    m.front(.575,.635,3.067,.27,.38,"notice")
    return m


def door():
    m=Mesh()
    m.box(0,0,0,1.1,2.15,.075,"teal",bottom=False)
    m.front(.11,.86,.078,.88,1.10,"glass")
    m.front(.525,.86,.084,.05,1.10,"teal_light")
    m.front(.11,1.36,.086,.88,.065,"teal_light")
    m.front(.12,.14,.078,.86,.53,"teal_light")
    m.front(.16,.18,.082,.78,.45,"teal")
    # Six-sided brass handle, with no separate material/texture allocation.
    for i in range(6):
        a,b=i*math.tau/6,(i+1)*math.tau/6
        m.triangle((.96,.75,.13),(.96+.055*math.cos(a),.75+.055*math.sin(a),.09),
                   (.96+.055*math.cos(b),.75+.055*math.sin(b),.09),"gold_light" if i<4 else "gold")
    return m


def floor():
    m=Mesh()
    m.box(0,-.08,0,4,.08,4,"cream_shadow",bottom=False,hidden=(4,))
    for z in range(3):
        for x in range(3):
            a,b=x*4/3,z*4/3
            m.quad((a,.002,b),(a,.002,b+4/3),(a+4/3,.002,b+4/3),(a+4/3,.002,b),"tiles")
    # Helpers take actual room metres; only X/Z are normalized for the shell.
    def box(x,y,z,w,h,d,c,hidden=()):
        m.box(x/1.5,y,z/1.5,w/1.5,h,d/1.5,c,bottom=False,hidden=hidden)
    # Service counter: every geometric extent stays inside FIXTURES[0].
    box(.41,0,1.50,2.08,.92,.84,"wood",hidden=(4,))
    box(.35,.92,1.45,2.2,.10,.95,"wood_light")
    for x in (1.04,1.56):
        m.front(x/1.5,.12,2.342/1.5,.018/1.5,.70,"wood_dark")
    m.quad((.73/1.5,1.025,1.79/1.5),(.73/1.5,1.025,2.15/1.5),
           (1.56/1.5,1.025,2.15/1.5),(1.56/1.5,1.025,1.79/1.5),"book")
    # Low brass bell is intentionally within the declared 1.12m counter height.
    bell=circle(2.10/1.5,1.96/1.5,.12/1.5,8)
    for i,(a,b) in enumerate(zip(bell,bell[1:]+bell[:1])):
        m.triangle((2.10/1.5,1.12,1.96/1.5),(a[0],1.03,a[1]),(b[0],1.03,b[1]),"gold_light" if i<4 else "gold")
    # Shelving. Stock bins are scenery; real lost items remain domain state.
    box(3.65,0,.30,.10,2.35,.80,"wood")
    box(5.55,0,.30,.10,2.35,.80,"wood")
    box(3.75,.08,.30,1.80,2.17,.075,"wood_dark",hidden=(2,3,4))
    for y in (.08,.77,1.46,2.25):
        box(3.75,y,.30,1.80,.10,.80,"wood_light",hidden=(2,3) if y in (.08,2.25) else (0,2,3))
    for i,(x,y,c) in enumerate(((3.91,.18,"cream"),(4.84,.18,"parcel"),
                               (3.91,.87,"teal_light"),(4.84,.87,"cream"),
                               (3.91,1.56,"parcel"),(4.84,1.56,"blue"))):
        box(x,y,.52,.55,.40,.48,c,hidden=(0,))
    # Filing cabinet and a quiet waiting bench.
    box(.35,0,.30,.75,1.05,.73,"stone")
    for y in (.15,.60):
        m.front(.40/1.5,y,1.035/1.5,.65/1.5,.36,"stone_dark")
        m.front(.63/1.5,y+.19,1.044/1.5,.19/1.5,.035,"gold")
    box(.42,1.05,.42,.53,.298,.45,"teal")
    for x,c in ((.61,"cream"),(.80,"blue")):
        m.front(x/1.5,1.05,.872/1.5,.15/1.5,.30,c)
        m.quad((x/1.5,1.35,.42/1.5),(x/1.5,1.35,.87/1.5),
               ((x+.15)/1.5,1.35,.87/1.5),((x+.15)/1.5,1.35,.42/1.5),c)
    box(4.94,.37,3.35,.66,.10,1.25,"wood_light")
    box(5.52,.47,3.35,.08,.18,1.25,"wood")
    for z in (3.43,4.40):
        box(4.96,0,z,.61,.37,.12,"wood")
    return m


def wall(which):
    m=Mesh()
    if which == "north":
        m.box(0,0,-.12,4,2.8,.12,"cream",bottom=False)
        m.front(0,0,.026,4,.86,"wainscot")
        m.front(0,.86,.040,4,.06,"teal_light")
        m.front(0,2.68,.041,4,.12,"cream_shadow")
        m.box(1.15,1.25,.035,1.05,1.10,.045,"wood_light",bottom=False,hidden=(0,))
        m.front(1.20,1.30,.084,.95,1.0,"map")
        # Clock face on an eight-sided disc, mapped to the same tiny atlas.
        cx,cy,r=.65,2.04,.29
        rx=r/1.5
        u0,v0,u1,v1=uv_rect("clock")
        for i in range(8):
            a,b=i*math.tau/8,(i+1)*math.tau/8
            points=((cx,cy,.055),(cx+rx*math.cos(a),cy+r*math.sin(a),.055),
                    (cx+rx*math.cos(b),cy+r*math.sin(b),.055))
            uv=tuple((u0+(x-cx+rx)/(2*rx)*(u1-u0),v1-(y-cy+r)/(2*r)*(v1-v0)) for x,y,_ in points)
            m.triangle(*points,"clock",uv)
    elif which in ("east","west"):
        x=4 if which=="east" else -.12
        m.box(x,0,0,.12,2.8,4,"cream",bottom=False)
        x=3.975 if which=="east" else .001
        for y,h,c in ((0,.86,"wainscot"),(.86,.06,"teal_light"),(2.68,.12,"cream_shadow")):
            face=((x,y,0),(x,y,4),(x,y+h,4),(x,y+h,0))
            m.quad(*(face if which=="east" else tuple(reversed(face))),c)
        w=window()
        # Widen the same exterior window for the room, with its glazing facing
        # inward. A 90 degree transform preserves winding and normals.
        if which=="west":
            m.append(w,lambda p:(.03+p[2],1.16+p[1],2.7-p[0]*2.0))
        else:
            m.append(w,lambda p:(3.97-p[2],1.16+p[1],1.74+p[0]*2.0))
    else:
        m.box(0,0,4,1.45,2.8,.12,"cream",bottom=False)
        m.box(2.55,0,4,1.45,2.8,.12,"cream",bottom=False)
        m.box(1.45,2.15,4,1.10,.65,.12,"cream",bottom=False)
        for x in (0,2.55):
            m.quad((x+1.45,0,3.975),(x,0,3.975),(x,.86,3.975),(x+1.45,.86,3.975),"wainscot")
            m.quad((x+1.45,.86,3.974),(x,.86,3.974),(x,.92,3.974),(x+1.45,.92,3.974),"teal_light")
        m.quad((4,2.68,3.973),(0,2.68,3.973),(0,2.8,3.973),(4,2.8,3.973),"cream_shadow")
    return m


def write_glb(path, mesh, png):
    chunks=[]
    for values in (mesh.positions,mesh.normals,mesh.uvs):
        flat=[v for row in values for v in row]
        chunks.append(struct.pack("<"+"f"*len(flat),*flat))
    offsets=[0,len(chunks[0]),len(chunks[0])+len(chunks[1]),sum(map(len,chunks))]
    binary=b"".join(chunks)+png
    binary+=b"\0"*((-len(binary))%4)
    count=len(mesh.positions)
    stats=mesh.validate(path.stem)
    gltf={"asset":{"version":"2.0","generator":"AnimalTown original koban kit / tools/generate-koban.py"},
          "buffers":[{"byteLength":len(binary)}],
          "bufferViews":[{"buffer":0,"byteOffset":offsets[i],"byteLength":len(data)} for i,data in enumerate(chunks+[png])],
          "accessors":[{"bufferView":0,"componentType":5126,"count":count,"type":"VEC3","min":stats["min"],"max":stats["max"]},
                       {"bufferView":1,"componentType":5126,"count":count,"type":"VEC3"},
                       {"bufferView":2,"componentType":5126,"count":count,"type":"VEC2"}],
          "images":[{"bufferView":3,"mimeType":"image/png"}],
          "samplers":[{"magFilter":9729,"minFilter":9987,"wrapS":33071,"wrapT":33071}],
          "textures":[{"sampler":0,"source":0}],
          "materials":[{"name":"Koban shared palette","pbrMetallicRoughness":{"baseColorTexture":{"index":0},"metallicFactor":0,"roughnessFactor":1}}],
          "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"material":0,"mode":4}]}],
          "nodes":[{"mesh":0,"name":path.stem}],"scenes":[{"nodes":[0]}],"scene":0}
    encoded=json.dumps(gltf,separators=(",",":"),ensure_ascii=True).encode()
    encoded+=b" "*((-len(encoded))%4)
    output=(struct.pack("<III",0x46546c67,2,28+len(encoded)+len(binary))
            +struct.pack("<II",len(encoded),0x4e4f534a)+encoded
            +struct.pack("<II",len(binary),0x004e4942)+binary)
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(output)
    return {"path":str(path),"bytes":len(output),**stats}


def generate(root):
    definitions=root/"assets/definitions/buildings"
    for path in definitions.glob("*.json"):
        data=json.loads(path.read_text())
        if data["type"]==2 and data["id"]!="koban":
            raise ValueError("Stable building type 2 is already assigned; do not renumber existing saves")
    png=atlas_png()
    tex=root/"assets/textures/buildings/koban/koban-atlas.png"
    tex.parent.mkdir(parents=True,exist_ok=True)
    tex.write_bytes(png)
    out=root/"assets/models/buildings/koban"
    inside=root/"assets/models/interiors/koban"
    meshes={out/"exterior.glb":exterior(),out/"door.glb":door(),inside/"door.glb":door(),inside/"floor.glb":floor()}
    meshes.update({inside/(name+".glb"):wall(name) for name in ("north","east","south","west")})
    # Furniture collision must enclose its actual authored geometry, including
    # counter lips, labels and handles, without blocking the centre aisle.
    for x,y,z in meshes[inside/"floor.glb"].positions:
        if y<=.02:
            continue
        if not any(f["x"]-1e-5<=x*1.5<=f["x"]+f["width"]+1e-5
                   and f["z"]-1e-5<=z*1.5<=f["z"]+f["depth"]+1e-5
                   and y<=f["height"]+1e-5 for f in FIXTURES):
            raise ValueError(f"Interior furniture outside declared collision: {x*1.5,y,z*1.5}")
    for path,mesh in meshes.items():
        mesh.validate(path.stem)
    report=[write_glb(path,mesh,png) for path,mesh in meshes.items()]
    building={"version":1,"type":2,"id":"koban","prefix":"koban","interior":"koban","footprint":[3,3],
              "hinge":[.95,0,3.02],"approach":[1.5,0,3.5],"exterior":"assets/models/buildings/koban/exterior.glb",
              "door":"assets/models/buildings/koban/door.glb"}
    interior={"version":1,"id":"koban","size":6,"units":"metres","up":"+Y","walls":["north","east","south","west"],
              "door":{"center":[3,0,6],"arrival":[3,0,5.35]},
              "camera":{"pitch":"fixed","quarterTurns":True,"heightScale":.7,"heightOffset":2.8,"backScale":.9,"backOffset":1.2},
              "activities":[[1.45,2.95],[4.65,1.65]],"fixtures":FIXTURES,
              **{name:f"assets/models/interiors/koban/{name}.glb" for name in ("floor","north","east","south","west")}}
    for path,value in ((definitions/"koban.json",building),(root/"assets/definitions/interiors/koban.json",interior)):
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(json.dumps(value,indent=2)+"\n")
    print(json.dumps({"atlas":{"width":128,"height":128,"bytes":len(png)},"parts":report},indent=2))


def render(root, output):
    import bpy
    from mathutils import Vector
    output.mkdir(parents=True,exist_ok=True)

    def load(path,location=(0,0,0),scale=(1,1,1)):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(path))
        for obj in set(bpy.data.objects)-before:
            if obj.type=="MESH":
                obj.location=location
                obj.scale=scale

    def setup():
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        scene=bpy.context.scene
        scene.render.engine="CYCLES"
        scene.cycles.samples=64
        scene.cycles.use_denoising=False
        scene.render.resolution_x=1000
        scene.render.resolution_y=909
        scene.render.resolution_percentage=100
        scene.render.image_settings.file_format="JPEG"
        scene.render.image_settings.quality=90
        scene.world.color=(.45,.43,.38)
        scene.view_settings.view_transform="Standard"
        scene.view_settings.look="Medium High Contrast" if "Medium High Contrast" in [x.name for x in bpy.types.ColorManagedViewSettings.bl_rna.properties["look"].enum_items] else "None"
        scene.view_settings.exposure=0
        scene.view_settings.gamma=1
        bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.13))
        mat=bpy.data.materials.new("Preview background")
        mat.diffuse_color=(.82,.76,.64,1)
        bpy.context.object.data.materials.append(mat)
        for location,energy,size in (((-4,-6,9),1100,7),((6,3,7),900,6),((-6,5,4),600,5)):
            bpy.ops.object.light_add(type="AREA",location=location)
            light=bpy.context.object
            light.data.energy=energy
            light.data.shape="DISK"
            light.data.size=size
            light.rotation_euler=(Vector((2,-2,1))-light.location).to_track_quat("-Z","Y").to_euler()
        return scene

    def camera(scene,position,target,scale,name,perspective=False):
        bpy.ops.object.camera_add(location=position)
        obj=bpy.context.object
        obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()
        obj.data.type="PERSP" if perspective else "ORTHO"
        obj.data.lens=50
        obj.data.ortho_scale=scale
        scene.camera=obj
        scene.render.filepath=str(output/name)
        bpy.ops.render.render(write_still=True)

    scene=setup()
    load(root/"assets/models/buildings/koban/exterior.glb")
    load(root/"assets/models/buildings/koban/door.glb",(.95,-3.02,0))
    camera(scene,(6.5,-10,5.8),(1.5,-1.5,1.65),5.3,"koban-exterior.jpg")
    scene=setup()
    for name in ("floor","north","east","west"):
        load(root/f"assets/models/interiors/koban/{name}.glb",scale=(1.5,1.5,1))
    camera(scene,(3,-12,10.8),(3,-2.5,1.0),8.9,"koban-interior.jpg",perspective=True)


if __name__=="__main__":
    args=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else sys.argv[1:]
    if args and args[0]=="--render":
        if len(args)!=3:
            raise SystemExit("--render ROOT OUTPUT")
        render(Path(args[1]).resolve(),Path(args[2]).resolve())
    else:
        parser=argparse.ArgumentParser(description=__doc__)
        parser.add_argument("root",nargs="?",type=Path,default=Path(__file__).resolve().parents[1])
        generate(parser.parse_args(args).root.resolve())
