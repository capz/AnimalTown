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
menu_source=ast.parse(Path('tools/iso-smoke.py').read_text())
menu_namespace={'Path':Path,'__file__':str(Path('tools/iso-smoke.py').resolve())}
exec(compile(ast.Module(body=[node for node in menu_source.body if isinstance(node,ast.FunctionDef) and node.name=='testing_menu_count'],type_ignores=[]),'<debug navigation contract>','exec'),menu_namespace)
assert menu_namespace['testing_menu_count']()==27

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
runtime=decode({i:256 for i in range(1,121)})
assert runtime['scenarios']['runtime_4_scene_0']['frames']==120
print('Frame telemetry: normal travel, bounded repairs, loading interruption and missing-terrain rejection passed')

# Protocol v6 attaches every band to a CRC-protected session/32-bit sequence.
wire=runpy.run_path('tools/profile_wire.py')
def paint(frame,words,y):
    for bit in range(len(words)*16):
        if words[bit//16]&(1<<(bit%16)):
            at=((y+2+bit//64*4)*640+bit%64*10+5)*3
            frame[at:at+3]=b'\xff\xff\xff'
class V6Capture:
    def __init__(self,samples):self.samples=iter(samples);self.stdout=self
    def read(self,size):
        try:session,sequence,corrupt=next(self.samples)
        except StopIteration:return b''
        main=[0]*32;main[:3]=[0x4154,6,sequence&65535];main[16]=333
        for word in main[:31]:main[31]^=word
        detail=[0]*16;detail[:3]=[0x4144,sequence&65535,session]
        for word in detail[:15]:detail[15]^=word
        batch=[0]*16;batch[:4]=[0x4153,1,sequence&65535,session]
        for word in batch[:15]:batch[15]^=word
        memory=[0]*16;memory[:7]=[0x414d,1,session,0,1,0,(sequence//2)%8]
        memory[7]=memory[6]*4;memory[15]=wire['crc'](memory[:15])
        identity=[0]*16;identity[:8]=[0x4149,1,session,0,sequence&65535,sequence>>16,33333,0];identity[9]=1
        identity[15]=wire['crc'](main+detail+batch+memory+identity[:15])
        if corrupt:batch[3]^=1
        frame=bytearray(size)
        for words,y in ((main,448),(detail,432),(batch,416),(identity,400),(memory,384)):paint(frame,words,y)
        return bytes(frame)
    def wait(self):return 0
samples=[(7,seq,False) for seq in range(65530,65562)]+[(8,seq,False) for seq in range(65530,65562)]
samples.extend(((8,65561,False),(8,65562,True),(8,65563,False)))
output=StringIO()
with patch.object(sys,'argv',['tools/read-frame-profile.py','hardware.avi','--platform','hardware','--storage','DVD','--video-mode','480p']),patch('subprocess.Popen',return_value=V6Capture(samples)),redirect_stdout(output):
    runpy.run_path('tools/read-frame-profile.py',run_name='__main__')
v6=json.loads(output.getvalue())
assert v6['sessions']==[7,8] and v6['preparation_details']['frames']==65
assert v6['repeated_video_samples']==1 and v6['invalid_v6_samples']==1 and v6['missing_sequence_samples']==1
assert v6['scenarios']['world']['timings']['frame_interval']['p99']==33.333
assert v6['scenarios']['world']['pass_draw_submissions']['terrain_or_interior']['p50']==8
assert len(v6['memory_snapshots'])==2 and v6['platform']=='hardware' and 'console timebase' in v6['units']
assert v6['excluded_transition_samples']==0 and not v6['legacy_identity_limitations']
print('Telemetry v6: session identity, sequence wrap, raw intervals, cross-band CRC, memory assembly, repeats/gaps and hardware provenance passed')

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
readers=[n for n in module.body if isinstance(n,ast.FunctionDef) and n.name in ('committed_card_sequence','options_card_files','villager_card_banks')]
exec(compile(ast.Module(body=readers,type_ignores=[]),'<town card readers>','exec'),namespace)

def save_record(version,kind,sequence,body):
    data=struct.pack('<7I',0x50535441,version,1,kind,sequence,42,32+len(body))+body
    return data+struct.pack('<I',crc32(data))

def v11_town_body(projected=True, snow=True, custody=True, site=(40,25)):
    # Follow SplitSaveCodec.EncodeResidentPart: variable terrain/name/item
    # sections, thirteen types, fixed overlays, then snow/daylight/jobs/custody.
    body=bytearray(struct.pack('<5i',128,120,5,23,0));offsets={}
    def append(key,words):
        offsets[key]=len(body);body.extend(struct.pack('<'+str(len(words))+'I',*(word&0xffffffff for word in words)))
    append('terrain',(1,15360,1,15360))
    for npc in range(6):append('npc'+str(npc),(10+11*npc,14,3,ord('N'),ord('p'),ord('a')+npc,10240+11264*npc,14848))
    append('ground',(1,10,20,30,2,22,12))
    append('placed',(1,10,1,1,0,1,0))
    types=[0]*13;types[7]=1;types[12]=2
    append('buildings',(0 if projected else 2,*types))
    append('npc_runtime',tuple([0]*42 if projected else [26,0,0,0,0,0,0]+[0]*35))
    append('paths',(1,3840));body.extend(bytes(3840))
    append('traffic',(1,15360));body.extend(bytes(15360))
    append('snow',(4,100,0,2,(0xbbff<<16)|3) if snow else (0,))
    append('daylight',(13,))
    jobs=(40,0x31424f4a,1,6,1,2,3,-1,-1,-1,-1,*[word for npc in range(6) for word in (npc,0,0,0,1)])
    append('jobs',tuple([0]*41) if projected else jobs)
    append('site',site)
    recovery=(0x3144464c,1,7,1,0,7,0,10,20,30,0,2,22,0,0,1,2) if custody else (0x3144464c,1,7,0)
    append('recovery',(len(recovery),*recovery))
    return bytes(body),offsets

def generation(bank, town_version, runtime_words, job_clock=None, clock_marker=0x43424f4a, *, town_body=None, incremental=True, snow=True, custody=True, site=(40,25)):
    sequence=bank+1
    town=struct.pack('<5I',96,160,1,23,0)+bytes(148)
    if town_version>=10:town+=struct.pack('<5I',4,100,0,2,(0xbbff<<16)|3)
    if town_version>=8:town+=struct.pack('<I',13)
    if town_version>=9:town+=bytes(164)
    if town_version>=11:town=v11_town_body(incremental,snow,custody,site)[0]
    if town_body is not None:town=town_body
    runtime=bytearray(268)
    if town_version>=11:struct.pack_into('<ii',runtime,96,2,26)
    if runtime_words:runtime+=struct.pack('<I',runtime_words)+bytes(160)
    if job_clock is not None:runtime+=struct.pack('<Iq',clock_marker,job_clock)
    bases=('AT-Town','AT-Houses','AT-Shops','AT-Player','AT-Villager2-','AT-Villager3-','AT-Villager4-','AT-Residents')
    bodies=(town,bytes(48),bytes(56),*(bytes(268) for _ in range(4)),runtime)
    if not incremental:bases=bases[:-1];bodies=bodies[:-1]
    files={};references=[]
    for i,(base,body) in enumerate(zip(bases,bodies)):
        kind=i if i<4 else i+1
        part=save_record(town_version if i==0 else 4,kind,sequence,body)
        files[base+str(bank)]=part
        references.append(struct.pack('<5I',kind,bank,sequence,len(part),crc32(part[:-4])) if incremental else struct.pack('<2I',len(part),crc32(part[:-4])))
    files[f'AT-Commit{bank}']=save_record(5 if incremental else 4,4,sequence,b''.join(references))
    return files

def write_card(path,files):
    sector=8192
    data=bytearray(5*sector)
    for i,(name,payload) in enumerate(files.items()):
        archive=name in ('AnimalTown','AnimalTown2')
        offset=16384 if archive else 32
        count=(offset+len(payload)+sector-1)//sector
        block=len(data)//sector
        data.extend(bytes(count*sector))
        entry=sector+i*64
        data[entry:entry+6]=b'AMTE01'
        data[entry+8:entry+8+len(name)]=name.encode()
        struct.pack_into('>HH',data,entry+54,block,count)
        for page in range(block,block+count):
            struct.pack_into('>H',data,3*sector+10+(page-5)*2,page+1 if page<block+count-1 else 0xffff)
        start=block*sector
        struct.pack_into('>8sII',data,start,b'DNDSAVE1',len(payload),offset)
        if archive:data[start+16:start+24]=b'DNDREC01'
        data[start+offset:start+offset+len(payload)]=payload
    for page,begin,end,checksum in ((1,0,0x1ffc,0x1ffc),(3,4,sector,0)):
        words=struct.unpack_from('>'+str((end-begin)//2)+'H',data,page*sector+begin)
        sums=(sum(words)&65535,sum(word^65535 for word in words)&65535)
        struct.pack_into('>HH',data,page*sector+checksum,*(0 if value==65535 else value for value in sums))
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
    for incremental in (True,False):
        for snow in (True,False):
            for custody in (True,False):
                write_card(card,old|generation(1,11,40,incremental=incremental,snow=snow,custody=custody))
                current=namespace['committed_card_sequence'](card,expected_day=23,details=True)
                assert current['sequence']==2 and current['daylight_location']==13 and current['active']==2 and current['dimensions']==[128,120]
                if incremental:assert current['npc_spaces']==[26,0,0,0,0,0]
    write_card(card,old|generation(1,11,40,site=(-1,-1)))
    assert namespace['committed_card_sequence'](card)==2
    body,offsets=v11_town_body()
    def changed(key,word,value):
        bad=bytearray(body);struct.pack_into('<i',bad,offsets[key]+word*4,value);return bytes(bad)
    malformed=[
        changed('buildings',13,256),changed('paths',0,2),changed('paths',1,3836),
        changed('traffic',1,15356),changed('snow',0,-1),changed('snow',0,3),changed('snow',0,7684),
        changed('snow',2,30),changed('snow',3,15361),changed('snow',4,3),
        changed('daylight',0,27),changed('jobs',0,40),changed('site',0,-1),changed('site',1,116),
        changed('recovery',0,3),changed('recovery',0,6661),changed('recovery',1,0),changed('recovery',2,2),
        changed('recovery',3,6),changed('recovery',4,2),changed('recovery',5,1),
        changed('recovery',7,3),changed('recovery',13,23),changed('recovery',16,2),
        body[:-4],body+bytes(4),body[:offsets['buildings']+13*4]+body[offsets['buildings']+14*4:]
    ]
    for bad in malformed:
        # generation repairs the record CRC and manifest references: rejection
        # must come from the malformed layout, not an unrelated checksum error.
        bad=bytearray(bad);struct.pack_into('<i',bad,12,24)
        newest=generation(1,11,40,town_body=bytes(bad))
        write_card(card,old|newest)
        fallback=namespace['committed_card_sequence'](card,expected_day=23,details=True)
        assert fallback['sequence']==1 and fallback['daylight_location']==0
        write_card(card,newest)
        assert namespace['committed_card_sequence'](card)==0
        assert namespace['committed_card_sequence'](card,details=True) is None
    write_card(card,old|generation(1,9,39))
    assert namespace['committed_card_sequence'](card)==1
    for clock in (-(1<<63),0,1800000000):
        for version in (9,10,11):
            write_card(card,old|generation(1,version,40,clock))
            assert namespace['committed_card_sequence'](card)==2
        for version in (7,8):
            write_card(card,old|generation(1,version,0,clock))
            assert namespace['committed_card_sequence'](card)==2
    for clock,marker in ((1<<62,0x43424f4a),(0,0)):
        for version in (9,11):
            write_card(card,old|generation(1,version,40,clock,marker))
            assert namespace['committed_card_sequence'](card)==1
    # The job verifier needs the logical bank names from a mixed layout.
    archive=bytearray(6*8192)
    root=bytearray(8192)
    struct.pack_into('<8I',root,0,0x31525444,1,1,6,0x32435241,7,0,2)
    root[32:42]=b'AT-Player0'
    struct.pack_into('<4I',root,64,2,0,8,fnv(b'archived'))
    struct.pack_into('<I',root,8188,fnv(root[:-4]))
    archive[:8192]=root
    archive[2*8192:2*8192+8]=b'archived'
    physical=struct.pack('<4sII',b'ATV1',3,4)+b'oldlive'
    write_card(card,{'AnimalTown':archive,'AnimalTown-Villager1':physical,'AnimalTown-Video':b'video'})
    assert namespace['options_card_files'](card)=={'AT-Player0':b'archived','AT-Player1':b'live','options':b'video'}
print('Card smoke reader: v9 job-clock, v10 snow, v11 buildings/custody, daylight and malformed-generation fallback passed')

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

# Restored ground art must survive moving affine shadows, while an unchanged
# diagnostic mip must fail. Mere colour counts include ordinary green pixels.
node=next(n for n in ast.parse(Path('tools/iso-smoke.py').read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='terrain_restoration_pixels')
scope={};exec(compile(ast.Module([node],type_ignores=[]),'terrain-restoration','exec'),scope)
match=scope['terrain_restoration_pixels']
shadow=(35,49,66);grass=(100,170,68)
def solid(c):return bytes(c)*(800*600)
def shade(c,alpha):return tuple(round((1-alpha)*v+alpha*s) for v,s in zip(c,shadow))
reference=solid(grass)
for alpha in (0,.08,.17,.259):
    metrics=match(reference,solid(shade(grass,alpha)))
    assert metrics['ground_samples']>1000 and metrics['matching_fraction']==1
    assert match(solid(shade(grass,alpha)),reference)['matching_fraction']==1
for diagnostic in ((66,214,99),(255,231,66)):
    assert match(reference,solid(diagnostic))['matching_fraction']<.05
assert match(solid((0,0,0)),reference)['ground_samples']==0
print('Terrain restoration: moving shadows preserve original art; stale green/yellow mips and missing ground are rejected')
