"""Import supplied villagers: python3 tools/import-villagers.py DOWNLOAD_DIR ROOT.

Requires Pillow. Archives are source data, never instructions. Preserve geometry
and rigs, combine matching exports, loop locomotion in place and resize maps.
"""
import copy
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import zipfile
from PIL import Image

SOURCES = [
    ('penguin', '4c8507cd-d3f8-44c4-a471-ec562a57d5fe.zip', {
        'base_basic_shaded.glb': {'human_000024': 'Running', 'human_000088': 'Walking', 'human_000092': 'Idle'}}),
    ('whitetiger', '4073a55c-6aeb-4f37-a34b-6143d24d1684.zip', {
        'base_basic_shaded.glb': {'human_000024': 'Running', 'human_000088': 'Walking', 'human_000013': 'Idle'}}),
    ('lion', 'adb5cd87-ad82-4891-ab6e-c7135ce35371.zip', {
        'animations_1/base_basic_shaded.glb': {'520ecc97-7a14-41be-af31-bf2f03d7fd5b': 'Walking'},
        'animations_2/base_basic_shaded.glb': {'human_000024': 'Running', 'human_000093': 'Idle'}}),
    ('robin', '1f2f9207-6ad5-4d27-a311-d1d2c1d689d4.zip', {
        'animations_1/base_basic_shaded.glb': {'94c95627-1aa6-4887-888d-9f072bbb7edd': 'Idle'},
        'animations_2/base_basic_shaded.glb': {'human_000046': 'Running', 'human_000089': 'Walking'}}),
    ('raccoon', 'f8e7f757-1bf9-4cf9-81cb-948b3801a14b (1).zip', {
        'base_basic_shaded.glb': {'human_000024': 'Running', 'human_000088': 'Walking', 'human_000093': 'Idle'}}),
    ('pugofficer', 'base_basic_shaded (1).glb', {
        'base_basic_shaded (1).glb': {'human_000024': 'Running', 'human_000050': 'Bow', 'human_000057': 'Sit', 'human_000093': 'Idle'}}),
    ('crocodile', 'base_basic_shaded (2).glb', {
        'base_basic_shaded (2).glb': {'human_000029': 'Running', 'human_000089': 'Walking', 'human_000093': 'Idle'}}),
    ('capybara', 'base_basic_shaded (3).glb', {
        'base_basic_shaded (3).glb': {'human_000013': 'Idle', 'human_000029': 'Running', 'human_000089': 'Walking'}}),
    ('panda', 'b8428be7-a427-4f57-9b8e-265bf4da9e30.zip', {
        'animations_1/base_basic_shaded.glb': {'b0c0eb12-b9ee-4939-9148-740ea5925e5b': 'Idle'},
        'animations_2/base_basic_shaded.glb': {'human_000029': 'Running', 'human_000089': 'Walking'}}),
    ('pig', 'faf2e7ad-7a5d-4c7b-9d5f-742636af8e13.zip', {
        'animations_1/base_basic_shaded.glb': {'90cf6a96-490c-4f7d-9404-53ce0d37beff': 'RunningInPlace'},
        'animations_2/base_basic_shaded.glb': {'human_000046': 'Running', 'human_000089': 'Walking'}}),
    ('axolotl', '74687edf-fc6f-495b-91c9-1c1134fbc57a.zip', {
        'animations_1/base_basic_shaded.glb': {'dbb8755c-7676-4526-abc1-9c6c3fea4343': 'Walking'},
        'animations_2/base_basic_shaded.glb': {'human_000013': 'Idle', 'human_000029': 'Running'}}),
    ('foxjacket', 'c9c5fd54-5e32-4927-b323-fa891b19ed5f.zip', {
        'animations_1/base_basic_shaded.glb': {'214895d9-cee4-4669-81c5-aea80f3d67a3': 'Idle'},
        'animations_2/base_basic_shaded.glb': {'human_000049': 'Running', 'human_000086': 'Walking'}}),
]


def read_glb(data):
    assert struct.unpack_from('<III', data) == (0x46546c67, 2, len(data))
    length, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4e4f534a
    model = json.loads(data[20:20 + length])
    size, kind = struct.unpack_from('<II', data, 20 + length)
    assert kind == 0x004e4942 and 28 + length + size == len(data)
    return model, data[28 + length:]


def import_model(archive, exports):
    model = None
    provenance = []
    for filename, aliases in exports.items():
        data = archive.read(filename)
        source, binary = read_glb(data)
        provenance.append({'entry': filename, 'sha256': hashlib.sha256(data).hexdigest(), 'clips': aliases})
        if model is None:
            model = copy.deepcopy(source)
            model['animations'] = []
            # Store each view independently, then emit only referenced views.
            views = [binary[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']] for v in source['bufferViews']]
        else:
            # Never retarget clips to a merely similar rig.
            for key in ('nodes', 'skins', 'meshes'):
                if source[key] != source_model[key]:
                    raise ValueError(f'Split exports have different {key}: {filename}')
        source_model = source
        for animation in source.get('animations', []):
            if animation['name'] not in aliases:
                raise ValueError('Unmapped supplied animation: ' + animation['name'])
            animation = copy.deepcopy(animation)
            animation['name'] = aliases[animation['name']]
            for sampler in animation['samplers']:
                for field in ('input', 'output'):
                    accessor = copy.deepcopy(source['accessors'][sampler[field]])
                    view = copy.deepcopy(source['bufferViews'][accessor['bufferView']])
                    offset = view.get('byteOffset', 0)
                    accessor['bufferView'] = len(model['bufferViews'])
                    model['bufferViews'].append(view)
                    views.append(binary[offset:offset + view['byteLength']])
                    sampler[field] = len(model['accessors'])
                    model['accessors'].append(accessor)
            model['animations'].append(animation)
    derived_walk = not any(a['name'] == 'Walking' for a in model['animations'])
    if derived_walk:
        # The officer has run, bow, sit and idle, but no authored walk. A slower
        # copy of his own run provides locomotion without borrowing another rig.
        walking = copy.deepcopy(next(a for a in model['animations'] if a['name'] == 'Running'))
        walking['name'] = 'Walking'
        for sampler in walking['samplers']:
            accessor = copy.deepcopy(model['accessors'][sampler['input']])
            view = model['bufferViews'][accessor['bufferView']]
            data = views[accessor['bufferView']]
            offset, stride = accessor.get('byteOffset', 0), view.get('byteStride', 4)
            times = [struct.unpack_from('<f', data, offset + i * stride)[0] * 2 for i in range(accessor['count'])]
            packed = struct.pack('<' + 'f' * len(times), *times)
            accessor.update(bufferView=len(views), byteOffset=0, min=[min(times)], max=[max(times)])
            views.append(packed)
            model['bufferViews'].append({'buffer': 0, 'byteLength': len(packed)})
            sampler['input'] = len(model['accessors'])
            model['accessors'].append(accessor)
        model['animations'].append(walking)
    derived_idle = not any(a['name'] == 'Idle' for a in model['animations'])
    if derived_idle:
        # The pig's custom export still runs its legs. Keep it as an extra
        # source clip, and stand on bind hips/legs with its own relaxed walk
        # upper-body pose. Key every previously animated TRS channel so a run
        # cannot leave stale limb transforms behind when entering Idle.
        defaults = {'translation': (0, 0, 0), 'rotation': (0, 0, 0, 1), 'scale': (1, 1, 1)}
        lower = {'Hips', 'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase',
                 'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase'}
        walking = next(a for a in model['animations'] if a['name'] == 'Walking')
        pose = {}
        for channel in walking['channels']:
            key = (channel['target']['node'], channel['target']['path'])
            if key[1] not in defaults:
                continue
            sampler = walking['samplers'][channel['sampler']]
            if sampler.get('interpolation', 'LINEAR') not in ('LINEAR', 'STEP'):
                raise ValueError('Standing idle needs linear/step source channels')
            accessor = model['accessors'][sampler['output']]
            width = len(defaults[key[1]])
            pose[key] = struct.unpack_from('<' + 'f' * width, views[accessor['bufferView']], accessor.get('byteOffset', 0))
        targets = sorted({(c['target']['node'], c['target']['path'])
                          for a in model['animations'] for c in a['channels'] if c['target']['path'] in defaults})
        def append_values(values, kind):
            packed = b''.join(struct.pack('<' + 'f' * len(value), *value) for value in values)
            accessor = {'bufferView': len(views), 'componentType': 5126, 'count': len(values), 'type': kind}
            if kind == 'SCALAR':
                accessor.update(min=[values[0][0]], max=[values[-1][0]])
            views.append(packed)
            model['bufferViews'].append({'buffer': 0, 'byteLength': len(packed)})
            index = len(model['accessors'])
            model['accessors'].append(accessor)
            return index
        times = append_values([(0.0,), (1.0,)], 'SCALAR')
        idle = {'name': 'Idle', 'samplers': [], 'channels': []}
        for node_id, path in targets:
            node = model['nodes'][node_id]
            bind = tuple(node.get(path, defaults[path]))
            value = bind if node.get('name') in lower else pose.get((node_id, path), bind)
            output = append_values([value, value], 'VEC4' if path == 'rotation' else 'VEC3')
            idle['channels'].append({'sampler': len(idle['samplers']), 'target': {'node': node_id, 'path': path}})
            idle['samplers'].append({'input': times, 'output': output, 'interpolation': 'LINEAR'})
        model['animations'].append(idle)
    if not {'Idle', 'Running', 'Walking'} <= {a['name'] for a in model['animations']}:
        raise ValueError('Each imported character needs Idle, Running and Walking')
    # World navigation owns movement. Remove horizontal travel from the hips
    # while preserving cyclic sway, vertical bob, rotations and key times.
    for animation in model['animations']:
        if animation['name'] not in ('Walking', 'Running'):
            continue
        for channel in animation['channels']:
            node = model['nodes'][channel['target']['node']]
            if channel['target']['path'] != 'translation' or node.get('name') != 'Hips':
                continue
            sampler = animation['samplers'][channel['sampler']]
            positions = model['accessors'][sampler['output']]
            times = model['accessors'][sampler['input']]
            def samples(accessor, width):
                view = model['bufferViews'][accessor['bufferView']]
                offset = accessor.get('byteOffset', 0)
                stride = view.get('byteStride', width * 4)
                return [struct.unpack_from('<' + 'f' * width, views[accessor['bufferView']], offset + i * stride)
                        for i in range(accessor['count'])]
            keys, timeline = samples(positions, 3), samples(times, 1)
            first, last = keys[0], keys[-1]
            duration = timeline[-1][0] - timeline[0][0]
            if duration <= 0:
                raise ValueError('Locomotion clip has no duration')
            bind = node.get('translation', (0, 0, 0))
            corrected = []
            for key, time in zip(keys, timeline):
                t = (time[0] - timeline[0][0]) / duration
                corrected.append((bind[0] + key[0] - first[0] - (last[0] - first[0]) * t,
                                  key[1], bind[2] + key[2] - first[2] - (last[2] - first[2]) * t))
            data = b''.join(struct.pack('<3f', *key) for key in corrected)
            positions.update(bufferView=len(views), byteOffset=0)
            for bound in ('min', 'max'):
                positions.pop(bound, None)
            views.append(data)
            model['bufferViews'].append({'buffer': 0, 'byteLength': len(data)})
    maximum_dropped_weight = 0.0
    def values(accessor_id, index):
        accessor = model['accessors'][accessor_id]
        view = model['bufferViews'][accessor['bufferView']]
        code = {5121: 'B', 5123: 'H', 5126: 'f'}[accessor['componentType']]
        width = struct.calcsize(code) * 4
        offset = accessor.get('byteOffset', 0) + index * view.get('byteStride', width)
        result = struct.unpack_from('<' + code * 4, views[accessor['bufferView']], offset)
        if accessor.get('normalized'):
            result = tuple(v / (255 if code == 'B' else 65535) for v in result)
        return result
    def attribute(data, count, component):
        view = len(views)
        views.append(data)
        model['bufferViews'].append({'buffer': 0, 'byteLength': len(data)})
        index = len(model['accessors'])
        model['accessors'].append({'bufferView': view, 'componentType': component, 'count': count, 'type': 'VEC4'})
        return index
    for mesh in model['meshes']:
        for primitive in mesh['primitives']:
            attrs = primitive['attributes']
            joint_sets = sorted(int(key.removeprefix('JOINTS_')) for key in attrs if key.startswith('JOINTS_'))
            weight_sets = sorted(int(key.removeprefix('WEIGHTS_')) for key in attrs if key.startswith('WEIGHTS_'))
            if joint_sets != weight_sets:
                raise ValueError('Joint and weight attribute sets do not match')
            if len(joint_sets) <= 1:
                continue
            count = model['accessors'][attrs['JOINTS_0']]['count']
            joints, weights = bytearray(), bytearray()
            for vertex in range(count):
                all_joints = sum((values(attrs[f'JOINTS_{group}'], vertex) for group in joint_sets), ())
                all_weights = sum((values(attrs[f'WEIGHTS_{group}'], vertex) for group in joint_sets), ())
                combined = {}
                for joint, weight in zip(all_joints, all_weights):
                    combined[joint] = combined.get(joint, 0) + weight
                strongest = sorted(combined.items(), key=lambda pair: -pair[1])[:4]
                total = sum(weight for _, weight in strongest)
                if total <= 0:
                    raise ValueError('Vertex has no skin weights')
                maximum_dropped_weight = max(maximum_dropped_weight, max(0, sum(all_weights) - total))
                strongest += [(0, 0)] * (4 - len(strongest))
                joints.extend(struct.pack('<4H', *(joint for joint, _ in strongest)))
                weights.extend(struct.pack('<4f', *(weight / total for _, weight in strongest)))
            attrs['JOINTS_0'] = attribute(joints, count, 5123)
            attrs['WEIGHTS_0'] = attribute(weights, count, 5126)
            for group in joint_sets:
                if group != 0:
                    del attrs[f'JOINTS_{group}'], attrs[f'WEIGHTS_{group}']
    for image in model.get('images', []):
        original = Image.open(io.BytesIO(views[image['bufferView']]))
        original.thumbnail((128, 128), Image.Resampling.LANCZOS)
        out = io.BytesIO()
        original.save(out, format='PNG', optimize=True)
        image['bufferView'] = len(views)
        image['mimeType'] = 'image/png'
        views.append(out.getvalue())
        model['bufferViews'].append({'buffer': 0, 'byteLength': len(out.getvalue())})
    # Repack referenced accessors/views, discarding duplicate source images and
    # animation storage. Attribute data and retained samples remain byte-exact.
    refs = []
    for mesh in model['meshes']:
        for primitive in mesh['primitives']:
            refs.extend((primitive['attributes'], key) for key in primitive['attributes'])
            if 'indices' in primitive:
                refs.append((primitive, 'indices'))
    refs.extend((skin, 'inverseBindMatrices') for skin in model.get('skins', []) if 'inverseBindMatrices' in skin)
    for animation in model['animations']:
        refs.extend((s, k) for s in animation['samplers'] for k in ('input', 'output'))
    accessors, mapping = [], {}
    for parent, key in refs:
        old = parent[key]
        if old not in mapping:
            mapping[old] = len(accessors)
            accessors.append(copy.deepcopy(model['accessors'][old]))
        parent[key] = mapping[old]
    model['accessors'] = accessors
    refs = [(a, 'bufferView') for a in accessors] + [(i, 'bufferView') for i in model.get('images', [])]
    binary, selected, mapping = bytearray(), [], {}
    for parent, key in refs:
        old = parent[key]
        if old not in mapping:
            mapping[old] = len(selected)
            binary.extend(b'\0' * (-len(binary) % 4))
            view = copy.deepcopy(model['bufferViews'][old])
            view.update(buffer=0, byteOffset=len(binary), byteLength=len(views[old]))
            selected.append(view)
            binary.extend(views[old])
        parent[key] = mapping[old]
    model['bufferViews'] = selected
    model['buffers'] = [{'byteLength': len(binary)}]
    text = json.dumps(model, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    binary.extend(b'\0' * (-len(binary) % 4))
    result = struct.pack('<IIIII', 0x46546c67, 2, 28 + len(text) + len(binary), len(text), 0x4e4f534a)
    result += text + struct.pack('<II', len(binary), 0x004e4942) + binary
    return result, provenance, maximum_dropped_weight, derived_walk, derived_idle


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('downloads', type=Path)
    parser.add_argument('root', type=Path)
    parser.add_argument('--only', nargs='+', choices=[name for name, _, _ in SOURCES],
                        help='Import only these characters, preserving other prepared sources and provenance')
    args = parser.parse_args()
    downloads, root = args.downloads, args.root
    target = root / 'assets/models/villagers'
    manifest = target / 'new-villagers-provenance.json'
    records = {r['model']: r for r in json.loads(manifest.read_text())} if args.only and manifest.exists() else {}
    for name, filename, exports in SOURCES:
        if args.only and name not in args.only:
            continue
        source = downloads / filename
        if source.suffix == '.zip':
            with zipfile.ZipFile(source) as archive:
                result, provenance, dropped_weight, derived_walk, derived_idle = import_model(archive, exports)
        else:
            class SingleGlb:
                def read(self, filename):
                    return source.read_bytes()
            result, provenance, dropped_weight, derived_walk, derived_idle = import_model(SingleGlb(), exports)
        output = target / f'villager_{name}.glb'
        output.write_bytes(result)
        records[name] = {'model': name, 'input': filename, 'inputSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                        'exports': provenance, 'source': output.relative_to(root).as_posix(),
                        'sha256': hashlib.sha256(result).hexdigest(), 'textureEdge': 128,
                        'skinInfluences': 4, 'maximumDroppedWeight': dropped_weight,
                        'locomotionRootMotion': 'horizontal hips travel removed; vertical bob and cyclic sway retained',
                        'walkingDerivedFromHalfSpeedRun': derived_walk,
                        'idleDerivedFromStandingPose': derived_idle}
        print(name, len(result), flush=True)
    manifest.write_text(json.dumps(list(records.values()), indent=2) + '\n')


if __name__ == '__main__':
    main()
