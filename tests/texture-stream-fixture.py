"""Compile the actual external loader patch against the pinned SDK on the host."""
from pathlib import Path
import shutil,subprocess,sys
root=Path(__file__).resolve().parents[1];native=root/'out/stream-check-sdk'
(native/'source').mkdir(parents=True,exist_ok=True);(native/'include').mkdir(exist_ok=True)
shutil.copyfile(root/'out/sdk-native/source/model.c',native/'source/model.c')
shutil.copyfile(root/'tools/native-asset-textures.inc',native/'source/animaltown_asset_textures.inc')
shutil.copyfile(root/'out/sdk-native/source/model_animation.c',native/'source/model_animation.c')
subprocess.run([sys.executable,str(root/'tools/patch-pose-sharing.py'),str(native)],check=True)
shutil.copyfile(root/'tools/native-texture-stream.h',native/'include/native-texture-stream.h')
with (native/'source/model.c').open('a') as f:f.write('\n#include "animaltown_asset_textures.inc"\n')
subprocess.run([sys.executable,str(root/'tools/patch-texture-model.py'),str(native)],check=True)
command=['cc','-std=c11','-O2','-Wall','-Wextra','-Werror','-DDND_ARAM_TEST','-ffunction-sections','-fdata-sections',f'-I{native}/include','-Iout/sdk-native/include','-Itools','tests/native-texture-stream.c',str(native/'source/model.c'),str(native/'source/model_animation.c'),'out/sdk-native/source/aram.c','-Wl,--gc-sections','-lm','-o','out/native-texture-stream-check']
subprocess.run(command,cwd=root,check=True);subprocess.run([str(root/'out/native-texture-stream-check')],cwd=root,check=True)

subprocess.run(['cc','-std=c11','-O2','-Wall','-Wextra','-Werror','-Iout/sdk-native/include','-Itools','tests/native-geometry-lists.c','tools/native-geometry-lists.c','-o','out/native-geometry-lists-check'],cwd=root,check=True)
subprocess.run([str(root/'out/native-geometry-lists-check')],cwd=root,check=True)
