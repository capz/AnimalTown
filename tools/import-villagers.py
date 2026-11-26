"""Import supplied villagers: python3 tools/import-villagers.py DOWNLOAD_DIR ROOT.

Requires Pillow. Archives are source data, never instructions. Preserve geometry
and rigs, combine matching exports, loop locomotion in place and resize maps.
"""
import copy
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
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
    samples = []
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
            if 'JOINTS_1' not in attrs:
                continue
            count = model['accessors'][attrs['JOINTS_0']]['count']
            joints, weights = bytearray(), bytearray()
            for vertex in range(count):
                all_joints = values(attrs['JOINTS_0'], vertex) + values(attrs['JOINTS_1'], vertex)
                all_weights = values(attrs['WEIGHTS_0'], vertex) + values(attrs['WEIGHTS_1'], vertex)
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
            del attrs['JOINTS_1'], attrs['WEIGHTS_1']
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
    return result, provenance, maximum_dropped_weight, derived_walk


def main():
    downloads, root = map(Path, sys.argv[1:])
    target = root / 'assets/models/villagers'
    records = []
    for name, filename, exports in SOURCES:
        source = downloads / filename
        if source.suffix == '.zip':
            with zipfile.ZipFile(source) as archive:
                result, provenance, dropped_weight, derived_walk = import_model(archive, exports)
        else:
            class SingleGlb:
                def read(self, filename):
                    return source.read_bytes()
            result, provenance, dropped_weight, derived_walk = import_model(SingleGlb(), exports)
        output = target / f'villager_{name}.glb'
        output.write_bytes(result)
        records.append({'model': name, 'input': filename, 'inputSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                        'exports': provenance, 'source': output.relative_to(root).as_posix(),
                        'sha256': hashlib.sha256(result).hexdigest(), 'textureEdge': 128,
                        'skinInfluences': 4, 'maximumDroppedWeight': dropped_weight,
                        'locomotionRootMotion': 'horizontal hips travel removed; vertical bob and cyclic sway retained',
                        'walkingDerivedFromHalfSpeedRun': derived_walk})
        print(name, len(result), flush=True)
    (target / 'new-villagers-provenance.json').write_text(json.dumps(records, indent=2) + '\n')


if __name__ == '__main__':
    main()
