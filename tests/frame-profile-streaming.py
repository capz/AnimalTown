"""Exercise checksummed telemetry and streaming gates without an emulator."""
import ast
from contextlib import redirect_stdout
from io import StringIO
import json
from pathlib import Path
import runpy
import sys
from unittest.mock import patch

ast.parse(Path('tools/iso-smoke.py').read_text())

class Capture:
    def __init__(self, flags, detail=False):
        self.flags = flags
        self.detail = detail
        self.frame = 0
        self.stdout = self

    def read(self, size):
        assert size == 640 * 480 * 3
        if self.frame == 120:
            return b''
        self.frame += 1
        words = [0] * 32
        words[0:3] = [0x4154, 5, self.frame]
        words[16] = 333
        words[30] = self.flags.get(self.frame, 0)
        for value in words[:31]:
            words[31] ^= value
        frame = bytearray(size)
        for bit in range(512):
            if words[bit // 16] & (1 << (bit % 16)):
                at = ((450 + bit // 64 * 4) * 640 + bit % 64 * 10 + 5) * 3
                frame[at:at + 3] = b'\xff\xff\xff'
        if self.detail:
            words=[0x4144,self.frame,1,2,3,4,5,17,4098,768,2,3,1,4,1,0]
            for value in words[:15]:words[15]^=value
            for bit in range(256):
                if words[bit//16] & (1 << (bit%16)):
                    at=((434+bit//64*4)*640+bit%64*10+5)*3
                    frame[at:at+3]=b'\xff\xff\xff'
        return bytes(frame)

    def wait(self):
        return 0

def decode(flags, failure=None, detail=False):
    output = StringIO()
    caught = None
    with patch.object(sys, 'argv', ['tools/read-frame-profile.py', 'capture.avi', '--check-streaming']), \
            patch('subprocess.Popen', return_value=Capture(flags, detail)), redirect_stdout(output):
        try:
            runpy.run_path('tools/read-frame-profile.py', run_name='__main__')
        except RuntimeError as error:
            caught = str(error)
    assert (caught is None) if failure is None else (caught is not None and failure in caught), caught
    return json.loads(output.getvalue())

normal = decode({60: 8192, 61: 8192, 62: 8192})
assert normal['landscape_loading_frames'] == 0
assert normal['scenarios']['world']['terrain_repair_frames'] == 3
assert normal['scenarios']['world']['missing_terrain_frames'] == 0
loading = decode({60: 32768}, 'Landscape loading interrupted travel')
assert loading['landscape_loading_frames'] == 1
missing = decode({60: 16384}, 'Visible terrain was missing during travel')
assert missing['scenarios']['world']['missing_terrain_frames'] == 1
extra=decode({},detail=True)['preparation_details']
assert extra['frames']==120 and extra['managed_allocations']['p50']==17
assert extra['gc_collections']==120 and extra['free_block_searches']==240
assert extra['texture_operations']==0 and extra['shadow_page_updates']==360
assert extra['mesh_updates']['p50']==3 and extra['generation_ms']['p50']==.2
print('Frame telemetry: normal travel, bounded repairs, loading interruption and missing-terrain rejection passed')

# The smoke reader must follow the consolidated SDK archive, including fallback
# from an interrupted/corrupt newest checkpoint, rather than read freed sectors.
import struct
module=ast.parse(Path('tools/iso-smoke.py').read_text())
namespace={}
exec(compile(ast.Module(body=[n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='archive_records'],type_ignores=[]),'<archive reader>','exec'),namespace)
def fnv(data):
    value=2166136261
    for byte in data:value=((value^byte)*16777619)&0xffffffff
    return value
image=bytearray(6*8192)
for bank,value in enumerate((b'old',b'new')):
    page=bank+2;image[page*8192:page*8192+len(value)]=value
    root=bytearray(8192);struct.pack_into('<4I',root,0,0x31525444,1,bank+1,6)
    root[32:39]=b'options';struct.pack_into('<4I',root,64,page,0,len(value),fnv(value))
    struct.pack_into('<I',root,8188,fnv(root[:-4]));image[bank*8192:(bank+1)*8192]=root
assert namespace['archive_records'](image)=={'options':b'new'}
# Root publication survives a completely full record directory and selects
# the correct backing epoch during growth; old one-byte markers still work.
root=bytearray(image[8192:16384]);struct.pack_into('<4I',root,16,0x32435241,7,64,3);struct.pack_into('<I',root,8188,fnv(root[:-4]));image[8192:16384]=root
records,phase,epoch=namespace['archive_records'](image,True)
assert records=={'options':b'new'} and (phase,epoch)==(3,7)
root=bytearray(image[8192:16384]);struct.pack_into('<I',root,28,4);struct.pack_into('<I',root,8188,fnv(root[:-4]));image[8192:16384]=root
assert namespace['archive_records'](image)=={'options':b'old'}
struct.pack_into('<I',root,28,2);struct.pack_into('<I',root,8188,fnv(root[:-4]));image[8192:16384]=root
image[3*8192]^=1
assert namespace['archive_records'](image)=={'options':b'old'}
image[8188]^=1
try:namespace['archive_records'](image)
except AssertionError:pass
else:raise AssertionError('Unverified archive was accepted')
print('Card smoke reader: named records, newest checkpoint, CRC fallback and rejection passed')

# The native job clock extends both the town projection and resident runtime.
# A running town can therefore advance from a v7 save to v9 on Save & Quit.
from tempfile import TemporaryDirectory
from zlib import crc32
reader=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='committed_card_sequence')
exec(compile(ast.Module(body=[reader],type_ignores=[]),'<town card reader>','exec'),namespace)

def save_record(version,kind,sequence,body):
    data=struct.pack('<7I',0x50535441,version,1,kind,sequence,42,32+len(body))+body
    return data+struct.pack('<I',crc32(data))

def generation(bank, town_version, runtime_words):
    sequence=bank+1
    town=struct.pack('<5I',96,160,1,23,0)+bytes(148)
    if town_version>=10:town+=struct.pack('<5I',4,100,0,2,(0xbbff<<16)|3)
    if town_version>=8:town+=struct.pack('<I',13)
    if town_version>=9:town+=bytes(164)
    runtime=bytes(268)
    if runtime_words:runtime+=struct.pack('<I',runtime_words)+bytes(160)
    bases=('AT-Town','AT-Houses','AT-Shops','AT-Player','AT-Villager2-','AT-Villager3-','AT-Villager4-','AT-Residents')
    bodies=(town,bytes(48),bytes(56),*(bytes(268) for _ in range(4)),runtime)
    files={};references=[]
    for i,(base,body) in enumerate(zip(bases,bodies)):
        kind=i if i<4 else i+1
        part=save_record(town_version if i==0 else 4,kind,sequence,body)
        files[base+str(bank)]=part
        references.append(struct.pack('<5I',kind,bank,sequence,len(part),crc32(part[:-4])))
    files[f'AT-Commit{bank}']=save_record(5,4,sequence,b''.join(references))
    return files

def write_card(path,files):
    sector=8192
    data=bytearray((5+len(files))*sector)
    for i,(name,payload) in enumerate(files.items()):
        block=i+5
        entry=sector+i*64
        data[entry:entry+6]=b'AMTE01'
        data[entry+8:entry+8+len(name)]=name.encode()
        struct.pack_into('>HH',data,entry+54,block,1)
        struct.pack_into('>H',data,3*sector+10+i*2,0xffff)
        offset=block*sector
        data[offset:offset+8]=b'DNDSAVE1'
        struct.pack_into('>II',data,offset+8,len(payload),32)
        data[offset+32:offset+32+len(payload)]=payload
    path.write_bytes(data)

with TemporaryDirectory() as fixture:
    card=Path(fixture)/'MemoryCardA.USA.raw'
    old=generation(0,7,0)
    write_card(card,old|generation(1,9,40))
    current=namespace['committed_card_sequence'](card,expected_day=23,details=True)
    assert current['sequence']==2 and current['daylight_location']==13
    write_card(card,old|generation(1,10,40))
    current=namespace['committed_card_sequence'](card,expected_day=23,details=True)
    assert current['sequence']==2 and current['daylight_location']==13
    write_card(card,old|generation(1,9,39))
    assert namespace['committed_card_sequence'](card)==1
print('Card smoke reader: v9 job-clock and v10 snow generations, daylight offset and malformed-extension fallback passed')

# Xvfb's number and newline arrive in separate writes. A short read must not
# close the pipe before the child completes its readiness protocol.
import os
import subprocess
from types import SimpleNamespace
node=next(n for n in ast.parse(Path('tools/iso-smoke.py').read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='start_display')
with TemporaryDirectory() as fixture:
    def split_display(arguments,**options):
        fd=int(arguments[arguments.index('-displayfd')+1])
        child='import os,time;fd='+str(fd)+';os.write(fd,b"17");time.sleep(.03);os.write(fd,b"\\n")'
        return subprocess.Popen([sys.executable,'-c',child],**options)
    scope={'root':Path(fixture),'os':os,'subprocess':SimpleNamespace(Popen=split_display,TimeoutExpired=subprocess.TimeoutExpired,STDOUT=subprocess.STDOUT)}
    exec(compile(ast.Module([node],type_ignores=[]),'display-ready','exec'),scope)
    server,environment=scope['start_display']()
    assert environment['DISPLAY']==':17' and server.wait(timeout=5)==0
print('Display readiness: split number/newline writes complete before pipe closure')
