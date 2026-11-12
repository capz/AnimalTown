#!/usr/bin/env python3
"""Host native allocation/restore verification of real cooked AnimalTown models.

This builds no console executable. Input packages must already be cooked with
the selected Assets tool and AnimalTown's prepare/native-winding steps.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SPECIES = ("fox", "bunny", "giraffe", "cat", "hamster", "spaniel")
SOURCES = dict(zip(SPECIES, (
    "Meshy_AI_Rigged_Fox_All_Animations.glb", "villager_bunny.glb", "villager_giraffe.glb",
    "villager_mainecoon.glb", "villager_hamster.glb", "villager_cockerspaniel.glb")))
RUNTIME = ("model.c", "model_animation.c", "model_residency.inc", "aram.c", "verified_copy.c")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", *args], cwd=root, text=True).strip()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--sdk-root", type=Path, required=True)
    p.add_argument("--assets", type=Path, default=ROOT / "out/disc")
    p.add_argument("--assets-version", required=True, help="Actual Assets converter version used for these input packages")
    p.add_argument("--assets-package", type=Path, help="Optional published Assets nupkg to record its checksum and source commit")
    p.add_argument("--edges", type=int, nargs="+", choices=(128, 256), default=(256, 128))
    p.add_argument("--output", type=Path, default=ROOT / "out/aram-model-validation.json")
    args = p.parse_args()
    sdk, assets = args.sdk_root.resolve(), args.assets.resolve()
    cc = os.environ.get("CC", "cc")
    result = {
        "schema": 1,
        "scope": "Real cooked model cohort; production SDK native allocation/free interception",
        "hardwareMeasurement": False,
        "gameRevision": git(ROOT, "rev-parse", "HEAD"),
        "gameDirty": bool(git(ROOT, "status", "--porcelain")),
        "sdkRevision": git(sdk, "rev-parse", "HEAD"),
        "sdkDirty": bool(git(sdk, "status", "--porcelain")),
        "host": platform.platform(),
        "scenario": {
            "models": 6, "instances": 7, "activePlayerSpecies": "fox",
            "coldSpecies": list(SPECIES[1:]), "audioReservationBytes": 2 * 1024 * 1024,
            "terrainAramReservationBytes": 8 * 1024 * 1024,
            "modelBackingBudgetBytes": 4 * 1024 * 1024,
            "stepBytes": 4096, "stepWork": 32,
        },
        "recipe": {
            "assetsVersion": args.assets_version,
            "prepare": "AnimalTown.Tools prepare: source GLB -> normalized prepared GLB",
            "convert": "dnd-assets model --texture-limit EDGE --mip-levels 2 --compile-gx-materials; approximation allowed for non-fox imports",
            "finalize": "AnimalTown.Tools model-native: normalize native face winding",
        },
        "cohorts": [],
        "caveats": [
            "Counts actual production calloc/aligned_alloc/free calls, not Model.NativeBytes budget sums.",
            "Native requested bytes are allocator request sizes; host usable bytes include host allocator rounding and are not GameCube measurements.",
            "Source/input/expectation buffers, simulated external ARAM, SDK static storage and the managed heap are outside the allocation scope.",
            "No rendering or GX texture-cache behavior is verified by this CPU-only test; use Dolphin/hardware rendering checks separately.",
            "These are maximum cold-group savings in the stated fox-player scenario, not a guarantee that ordinary gameplay reaches this residency set.",
        ],
    }
    if args.assets_package:
        with zipfile.ZipFile(args.assets_package) as package:
            nuspec = next(name for name in package.namelist() if name.endswith(".nuspec"))
            metadata = ET.fromstring(package.read(nuspec)).find("{*}metadata")
            version = metadata.findtext("{*}version")
            if version != args.assets_version:
                raise RuntimeError("Assets nupkg version differs from --assets-version")
            repository = metadata.find("{*}repository")
        result["recipe"]["assetsPackage"] = {
            "sha256": sha(args.assets_package), "version": version,
            "repositoryCommit": repository.get("commit") if repository is not None else None,
        }
    result["verificationSourceSha256"] = {
        file: sha(HERE / file)
        for file in ("verify_model_residency.c", "model_residency_alloc.h", "measure_model_residency.py")
    }
    result["sourceAssetsSha256"] = {
        "assets/models/villagers/" + file: sha(ROOT / "assets/models/villagers" / file)
        for file in SOURCES.values()
    }
    result["preparedAssetsSha256"] = {
        "out/prepared/" + species + ".glb": sha(ROOT / "out/prepared" / (species + ".glb"))
        for species in SPECIES if (ROOT / "out/prepared" / (species + ".glb")).exists()
    }
    with tempfile.TemporaryDirectory(prefix="town-model-residency-") as tmp:
        work = Path(tmp)
        shutil.copytree(sdk / "include", work / "include")
        (work / "source").mkdir()
        for name in RUNTIME:
            shutil.copyfile(sdk / "source" / name, work / "source" / name)
        result["runtimeSourceSha256"] = {
            str(path.relative_to(work)): sha(path)
            for path in sorted([*(work / "include").rglob("*"), *(work / "source").rglob("*")])
            if path.is_file()
        }
        flags = ["-O2", "-g", "-std=c11", "-Wall", "-Wextra", "-Werror", "-ffunction-sections", "-fdata-sections", "-I" + str(work / "include")]
        objects = []
        for name in ("model", "model_animation"):
            obj = work / (name + ".o")
            subprocess.run([cc, *flags, "-include", str(HERE / "model_residency_alloc.h"),
                            "-Dcalloc=town_audit_calloc", "-Daligned_alloc=town_audit_aligned_alloc", "-Dfree=town_audit_free",
                            "-c", str(work / "source" / (name + ".c")), "-o", str(obj)], check=True)
            objects.append(str(obj))
        binary = work / "verify-model-residency"
        dead = "-Wl,-dead_strip" if sys.platform == "darwin" else "-Wl,--gc-sections"
        subprocess.run([cc, *flags, str(HERE / "verify_model_residency.c"), *objects,
                        str(work / "source/aram.c"), str(work / "source/verified_copy.c"),
                        dead, "-lm", "-o", str(binary)], check=True)
        for edge in args.edges:
            raw = subprocess.check_output([str(binary), str(assets), str(edge)], text=True)
            cohort = json.loads(raw)
            for entry in cohort["models"]:
                file = assets / (entry["species"] + ("-tex128" if edge == 128 else "") + ".dndmodel")
                if sha(file) != entry["sha256"]:
                    raise RuntimeError("Native input SHA differs: " + str(file))
            if cohort["coldBackingBytes"] > result["scenario"]["modelBackingBudgetBytes"]:
                raise RuntimeError("Cold backing exceeds configured budget")
            result["cohorts"].append(cohort)
            print(f"{edge}px: {cohort['requestedBytesFreed']:,} native requested bytes freed; package/texture/material/pose parity passed", flush=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print("Report:", args.output)


if __name__ == "__main__":
    main()
