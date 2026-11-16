#!/usr/bin/env python3
"""Offline road-art study; calls AnimalTown's real GX floor encoder, no SDK.

Requires Python/Pillow and a .NET SDK with the .NET 8 targeting pack.
Writes measurements, never game assets.
Run from anywhere: python3 docs/concepts/roads/measure-textures.py --dotnet dotnet
"""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

from PIL import Image, __version__ as pillow_version


ROOT = Path(__file__).resolve().parents[3]
SOURCES = [
    "shared/AnimalTown.Domain/FloorTexture.cs",
    "shared/AnimalTown.Domain/TexturePixels.cs",
    "shared/AnimalTown.Domain/TextureResolution.cs",
]

PROGRAM = r'''
using System.Text.Json;
using AnimalTown;

using var input = JsonDocument.Parse(File.ReadAllText(args[0]));
var results = new List<object>();
foreach (var sample in input.RootElement.EnumerateArray()) {
    int edge = sample.GetProperty("edge").GetInt32();
    byte[] rgba = File.ReadAllBytes(sample.GetProperty("raw").GetString()!);
    if (sample.GetProperty("neutral").GetBoolean()) {
        int min = 255, max = 0;
        for (int p = 0; p < rgba.Length; p += 4) {
            int l = (rgba[p]*77+rgba[p+1]*150+rgba[p+2]*29+128)>>8;
            min = Math.Min(min, l); max = Math.Max(max, l);
        }
        float floor = sample.GetProperty("brightness_floor").GetSingle();
        for (int p = 0; p < rgba.Length; p += 4) {
            int l = (rgba[p]*77+rgba[p+1]*150+rgba[p+2]*29+128)>>8;
            float t = max == min ? 1 : (l-min)/(float)(max-min);
            byte v = (byte)Math.Clamp((int)MathF.Round((floor+(1-floor)*t)*255),0,255);
            rgba[p] = rgba[p+1] = rgba[p+2] = v;
        }
    }
    byte[] package = FloorTexture.Encode(rgba, edge, edge, fullMipChain:true);
    TexturePixels.Validate(package);
    long error = 0;
    for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++) {
        uint pixel = TexturePixels.Pixel(package,x,y);
        int p = (y*edge+x)*4;
        long r = (int)(pixel>>24)-rgba[p];
        long g = (int)((pixel>>16)&255)-rgba[p+1];
        long b = (int)((pixel>>8)&255)-rgba[p+2];
        error += r*r+g*g+b*b;
    }
    results.Add(new {
        id = sample.GetProperty("id").GetString(),
        format = TexturePixels.Int(package,8)==14 ? "CMPR" : "RGB565",
        mip_levels = TexturePixels.Int(package,24),
        resident_pixel_bytes = package.Length-32,
        package_bytes = package.Length,
        base_level_rgb_mse_per_channel = error/(edge*edge*3.0),
        package_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)).ToLowerInvariant()
    });
}
File.WriteAllText(args[1],JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
'''


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def resident_bytes(edge, block_width, block_height):
    total = 0
    while edge >= 4:
        total += ((edge + block_width - 1) // block_width) * (
            (edge + block_height - 1) // block_height
        ) * 32
        edge //= 2
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("texture-study.json"))
    args = parser.parse_args()
    config = json.loads((ROOT / "assets/terrain/terrain.json").read_text())
    samples = []
    provenance = []
    with tempfile.TemporaryDirectory(prefix="animaltown-road-study-") as directory:
        scratch = Path(directory)

        def add(identifier, source, edge, crop=None, neutral=False):
            path = ROOT / source
            with Image.open(path) as image:
                pixels = image.convert("RGBA")
                if crop:
                    pixels = pixels.crop(crop)
                if pixels.size != (edge, edge):
                    pixels = pixels.resize((edge, edge), Image.Resampling.BOX)
                raw = scratch / (identifier + ".rgba")
                raw.write_bytes(pixels.tobytes())
            samples.append({"id":identifier,"raw":str(raw),"edge":edge,"neutral":neutral,
                            "brightness_floor":config["neutralFloor"]["brightnessFloor"]})
            provenance.append({"id":identifier,"source":source,"source_sha256":digest(path),
                               "crop_xyxy":crop,"edge":edge,"neutral_brightness_remap":neutral})

        add("current-neutral-256", config["neutralFloor"]["source"], 256, neutral=True)
        for material in ("stone_pavement", "terracotta"):
            for season in ("spring", "fall", "winter"):
                source = config["seasons"][season][material]["sources"][0]["file"]
                for edge in (64, 128):
                    add(f"{material}-{season}-{edge}",source,edge,[256,256,512,512])

        (scratch / "inputs.json").write_text(json.dumps(samples))
        (scratch / "Program.cs").write_text(PROGRAM)
        includes = "".join(f'<Compile Include="{escape(str(ROOT / source))}" />' for source in SOURCES)
        (scratch / "Study.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
            '<Nullable>enable</Nullable></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>'
        )
        # No packages, console build, repository global.json or game SDK required.
        (scratch / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        subprocess.run([args.dotnet,"run","--project",str(scratch / "Study.csproj"),"-c","Release",
                        "--",str(scratch / "inputs.json"),str(scratch / "results.json")],cwd=scratch,check=True)
        results = json.loads((scratch / "results.json").read_text())
    report = {
        "scope":"Offline encoding of existing artwork; no in-game roads or hardware measurements.",
        "method":"Pillow RGBA center crop, BOX reduction; production FloorTexture.Encode full chain to 4x4. Neutral remap matches NeutralFloorAssets; RGB MSE is base-level only.",
        "pillow_version":pillow_version,
        "dotnet_sdk":subprocess.check_output([args.dotnet,"--version"],cwd=tempfile.gettempdir(),text=True).strip(),
        "encoder_sources":{source:digest(ROOT/source) for source in SOURCES},
        "inputs":provenance,
        "results":results,
        "layout_bounds":{str(edge):{"CMPR":resident_bytes(edge,8,8),"RGB565":resident_bytes(edge,4,4)} for edge in (64,128,256)},
        "limitations":["Crops are not validated as seamless tiles or approved road art.",
                        "Sizes exclude native object metadata, leases, geometry and transient decode arrays.",
                        "No runtime pruning, frame-time, shadow, snow or physical-console validation."]
    }
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+"\n")
    print(f"Wrote {len(results)} samples to {args.output}")
    for item in results:
        print(f'{item["id"]}: {item["format"]}, {item["mip_levels"]} levels, {item["resident_pixel_bytes"]:,} pixel bytes')


if __name__ == "__main__":
    main()
