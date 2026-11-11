"""Offline, original-model portraits: blender -b -t 2 --python tools/bake-villager-portraits.py -- ROOT [SPECIES].

Commit the transparent PNGs; ordinary builds only tile these small images for GX.
No Blender dependency, model, skeleton or render target is needed by the picker.
"""
import json
import hashlib
from pathlib import Path
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
root = Path(args[0]).resolve()
sources = {
    'fox': 'Meshy_AI_Rigged_Fox_All_Animations.glb',
    'bunny': 'villager_bunny.glb',
    'giraffe': 'villager_giraffe.glb',
    'cat': 'villager_mainecoon.glb',
    'hamster': 'villager_hamster.glb',
    'spaniel': 'villager_cockerspaniel.glb',
}
output = root / 'assets/textures/ui/villagers'
output.mkdir(parents=True, exist_ok=True)
manifest_path = output / 'provenance.json'
manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}

def aim(obj, point):
    obj.rotation_euler = (Vector(point) - obj.location).to_track_quat('-Z', 'Y').to_euler()

for name, filename in sources.items():
    if len(args) > 1 and name != args[1]:
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(root / 'assets/models/villagers' / filename))
    scene = bpy.context.scene
    # Use the model's idle pose when supplied; never edit the source GLB.
    for obj in scene.objects:
        if obj.type == 'ARMATURE' and obj.animation_data:
            actions = [action for action in bpy.data.actions if 'idle' in action.name.lower()]
            if actions:
                obj.animation_data.action = actions[0]
    scene.frame_set(1)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = [obj.matrix_world @ Vector(corner) for obj in scene.objects if obj.type == 'MESH'
              for corner in obj.evaluated_get(depsgraph).bound_box]
    low = Vector(tuple(min(p[axis] for p in points) for axis in range(3)))
    high = Vector(tuple(max(p[axis] for p in points) for axis in range(3)))
    center = (low + high) * .5
    size = max(high.z - low.z, high.x - low.x) * 1.10
    bpy.ops.object.camera_add(location=(center.x, center.y - size * 4, center.z + size * .035))
    camera = bpy.context.object
    aim(camera, center)
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = size
    camera.data.clip_start = .001
    camera.data.clip_end = size * 20
    scene.camera = camera
    for position, energy, scale in [((-2, -3, 4), 500, 3), ((3, -2, 1), 220, 3), ((0, 2, 3), 350, 2)]:
        bpy.ops.object.light_add(type='AREA', location=center + Vector(position) * size)
        light = bpy.context.object
        light.data.energy = energy * size * size
        light.data.shape = 'DISK'
        light.data.size = scale * size
        aim(light, center)
    scene.world = bpy.data.worlds.new('Portrait ambient')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.8, .85, .8, 1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .6
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 64
    scene.cycles.use_denoising = False
    scene.render.resolution_x = scene.render.resolution_y = 256
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.view_settings.view_transform = 'Standard'
    scene.render.filepath = str(output / (name + '.png'))
    bpy.ops.render.render(write_still=True)
    # Imported rigs can have unused/rest-pose bounds below the visible feet.
    # Fit the camera to visible alpha, then render again at native resolution.
    image = bpy.data.images.load(scene.render.filepath, check_existing=False)
    pixels = list(image.pixels)
    opaque = [(i % 256, i // 256) for i in range(256 * 256) if pixels[i * 4 + 3] > .1]
    if not opaque:
        raise RuntimeError('Empty villager render: ' + name)
    left, right = min(p[0] for p in opaque), max(p[0] for p in opaque)
    bottom, top = min(p[1] for p in opaque), max(p[1] for p in opaque)
    rotation = camera.rotation_euler.to_quaternion()
    camera.location += rotation @ Vector((((left + right + 1) / 512 - .5) * size,
                                         ((bottom + top + 1) / 512 - .5) * size, 0))
    camera.data.ortho_scale = max(right - left + 1, top - bottom + 1) / 256 * size * 1.10
    bpy.data.images.remove(image)
    bpy.ops.render.render(write_still=True)
    print('PORTRAIT', name, 'bounds', tuple(low), tuple(high), flush=True)
    source = 'assets/models/villagers/' + filename
    manifest[name] = {'source': source, 'sourceSha256': hashlib.sha256((root / source).read_bytes()).hexdigest(),
                      'pngSha256': hashlib.sha256((output / (name + '.png')).read_bytes()).hexdigest(),
                      'renderer': 'Blender ' + bpy.app.version_string + ', Cycles CPU, 64 samples'}
manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
