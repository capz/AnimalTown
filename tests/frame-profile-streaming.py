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
exec(compile(ast.Module(body=[node for node in menu_source.body if isinstance(node,ast.FunctionDef) and node.name in ('testing_menu_options','testing_menu_count','testing_menu_row')],type_ignores=[]),'<debug navigation contract>','exec'),menu_namespace)
assert menu_namespace['testing_menu_count']()==22
assert menu_namespace['testing_menu_row']('VillagerVisualOption')==menu_namespace['testing_menu_row'](44)==21
assert menu_namespace['testing_menu_row']('ProfilingModeOption')==menu_namespace['testing_menu_row'](41)==19
assert menu_namespace['testing_menu_row']('ExportProfileOption')==menu_namespace['testing_menu_row'](42)==20
assert [menu_namespace['testing_menu_row'](option) for option in (22,23,34)]==[11,12,13]
for removed in ('WarmTerrainOption','SmoothSnowOption',40,30,33,43,12,13):
    try:menu_namespace['testing_menu_row'](removed)
    except AssertionError:pass
    else:raise AssertionError('A retired debug control was still navigable')

# Old capture commands may retain --warm-terrain, but automatic prefetch must
# not cause a title-menu detour, config write or extra controller input.
start_node=next(node for node in menu_source.body if isinstance(node,ast.FunctionDef) and node.name=='start_game')
for legacy_flag in (False,True):
    events=[];before={'active':0,'profiles':[{'created':True}]}
    scope={'sys':sys,'root':Path('/fixture'),'committed_card_sequence':lambda path,details:before,
           'wait_title':lambda:events.append('title'),'press':lambda button:events.append(button),
           'wait_profiles':lambda name:events.append(name),'wait_onboarding':lambda stage,name:(stage,name)}
    exec(compile(ast.Module([start_node],type_ignores=[]),'<automatic prefetch compatibility>','exec'),scope)
    with patch.object(sys,'argv',['iso-smoke.py',*(['--warm-terrain'] if legacy_flag else [])]):
        assert scope['start_game'](prefix='automatic')==(before,('world','automatic-game.png'))
    assert events==['title','A','automatic-picker.png','A']

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

# Memory pressure may intentionally retain the real preview source. Verify its
# native sampler contract without accepting unbound or unsupported dimensions.
node=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='terrain_mip_applied')
scope={};exec(compile(ast.Module([node],type_ignores=[]),'<resident ground mip state>','exec'),scope)
applied=scope['terrain_mip_applied']
def mip_marker(size=64,enabled=0,mips=1,distance=0,levels=2,height=None):
    return f'ANIMALTOWN GROUND MIP BANDS enabled {enabled} mips {mips} distance {distance} size {size}x{size if height is None else height} levels {levels}\n'
for size in (64,256):
    for enabled in (0,1):
        for mips in (0,1):
            for distance in (0,1,10):
                state=applied(mip_marker(size,enabled,mips,distance),enabled,mips,distance)
                assert state=={'enabled':bool(enabled),'mipmaps':bool(mips),'distance':distance,'width':size,'height':size,'levels':2}
for invalid in ('',mip_marker(0),mip_marker(128),mip_marker(512),mip_marker(height=256),
                mip_marker(enabled=1),mip_marker(mips=0),mip_marker(distance=1),
                mip_marker(256)+mip_marker(0),mip_marker(256)+mip_marker(distance=10)):
    assert applied(invalid,0,1,0) is None
for size in (64,256):
    for levels in (0,1,3):
        try:applied(mip_marker(size,levels=levels),0,1,0)
        except AssertionError:pass
        else:raise AssertionError('Invalid resident ground mip chain was accepted')
print('Terrain applied state: real 64/256 sources pass; unbound, unsupported, stale attributes and bad chains fail')

# Real default-on residency acceptance must prove physical ownership release,
# source eviction and ready-after-floor ordering rather than only match a label.
terrain_reader=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='terrain_residency_cycles')
exec(compile(ast.Module(body=[terrain_reader],type_ignores=[]),'<terrain residency reader>','exec'),namespace)
terrain_cycles=namespace['terrain_residency_cycles']
cache_cold='ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes 0 reclaimed bytes 1048576 archive bytes 123456\n'
floor_cold='ANIMALTOWN FLOOR SUSPENDED owned bytes 6144 -> 0 source bytes 0 reclaimed bytes 8896\n'
floor_ready='ANIMALTOWN FLOOR RESTORED season 0 slots 12 owned bytes 6144 source bytes 6144\n'
cache_ready='ANIMALTOWN TERRAIN CACHE READY cache bytes 1048576 archive bytes 123456\n'
boot='ANIMALTOWN GAME START\n'
route=boot+cache_cold+floor_cold+floor_ready+cache_ready
evidence=terrain_cycles(route)
assert len(evidence['cycles'])==1 and evidence['pending_cache'] is None and evidence['pending_floor'] is None
assert evidence['cycles'][0]['floor_suspended']['source_bytes']==0
assert evidence['cycles'][0]['floor_restored']['owned_bytes']==6144
pending=terrain_cycles(boot+cache_cold+floor_cold)
assert not pending['cycles'] and pending['pending_cache']['reclaimed_bytes']==1048576 and pending['pending_floor']['owned_after']==0
for invalid in (
    route.replace('reclaimed bytes 1048576','reclaimed bytes 1048544'),
    route.replace('source bytes 0','source bytes 32'),
    route.replace('-> 0','-> 2048'),
    route.replace('reclaimed bytes 8896','reclaimed bytes 1024'),
    route.replace('owned bytes 6144 source bytes 6144','owned bytes 6144 source bytes 8192'),
    route.replace(cache_ready,cache_ready.replace('123456','654321')),
    boot+cache_cold+floor_cold+cache_ready+floor_ready,
    boot+cache_cold+cache_ready,
):
    try:terrain_cycles(invalid)
    except AssertionError:pass
    else:raise AssertionError('Unverified terrain/floor memory evidence was accepted')
# A reversal can cancel a cache that was allocated but never became drawable.
reversed_route=boot+cache_cold+floor_cold+floor_ready+cache_cold+floor_cold+floor_ready+cache_ready
reversed_evidence=terrain_cycles(reversed_route)
assert len(reversed_evidence['cycles'])==1 and reversed_evidence['cancelled_restores']==1
ignored=boot+cache_cold.replace('1048576','1')+route
assert len(terrain_cycles(ignored)['cycles'])==1
try:terrain_cycles(cache_cold+floor_cold+floor_ready+cache_ready)
except AssertionError:pass
else:raise AssertionError('Residency evidence without an identified town session was accepted')
print('Terrain residency smoke: exact MEM1 release, source eviction, doorway ordering, reversal and session isolation passed')

# Reversal input may land during a real door cooldown and open a refusal.
# A drawn exterior with that speech bubble must not acknowledge an inspector.
door_nodes=[n for n in module.body if isinstance(n,ast.FunctionDef) and n.name in ('fill_matches','region_color_count','aram_door_state','aram_inspector_arrival')]
door_scope={}
exec(compile(ast.Module(door_nodes,type_ignores=[]),'<ARAM doorway acknowledgement>','exec'),door_scope)
def doorway_frame(home=0,dialogue=False,action=True,background=(255,255,255)):
    pixels=bytearray(bytes(background)*(800*600))
    def rectangle(left,top,right,bottom,color):
        for y in range(top,bottom):pixels[(y*800+left)*3:(y*800+right)*3]=bytes(color)*(right-left)
    rectangle(217,320,424,360,(255,245,221))
    if action:rectangle(230,328,250,353,(53,105,84))
    # Distinct stable home labels, independent of the animated 3D view.
    rectangle(270+home*16,330,279+home*16,340,(49,72,62))
    if dialogue:rectangle(60,340,580,420,(255,245,221))
    return bytes(pixels)
home_a,home_b=doorway_frame(),doorway_frame(1)
speech=doorway_frame(dialogue=True)
assert door_scope['aram_door_state'](speech)['dialogue']
assert door_scope['aram_door_state'](speech)['action_signature'] is None
assert door_scope['aram_door_state'](home_a)['action_signature'] is not None
door_scope['aram_inspector_arrival'](home_a,home_b)
for unacknowledged in (home_a,doorway_frame(background=(80,120,60)),speech,doorway_frame(1,action=False)):
    try:door_scope['aram_inspector_arrival'](home_a,unacknowledged)
    except AssertionError:pass
    else:raise AssertionError('Ignored inspector input or a transient door view was counted as arrival')

# Execute the production controller helpers: clear only visible speech with B,
# settle in guest time, and increment the home route only after visual proof.
scenario=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='aram_residency_scenario')
controller_nodes=[n for n in scenario.body if isinstance(n,ast.FunctionDef) and n.name in ('release_controls','clear_door_refusal','inspect')]
wrapper=ast.parse('def doorway_helpers():\n    building_inspections=0\n').body[0]
wrapper.body+=controller_nodes+ast.parse('return inspect,lambda:building_inspections').body
ast.fix_missing_locations(wrapper)
for ignored,stuck in ((False,False),(True,False),(False,True)):
    events=[];images={};state={'pixels':speech if not ignored else home_a,'dismissals':0}
    def send(button,clock):
        events.append(button)
        if button=='B':
            state['dismissals']+=1
            if state['dismissals']>=2 and not stuck:state['pixels']=home_a
        elif button=='A' and not ignored:state['pixels']=home_b
    def capture(name,indoors):images[name+'.png']=state['pixels']
    scope={**door_scope,'os':SimpleNamespace(write=lambda controller,data:events.append(data.decode())),
           'controller':0,'clock':object(),'view':capture,'rgb':lambda name:images[name],
           'press_presented':send,'guest':lambda name,seconds:events.append(('guest',seconds)),'report':{}}
    exec(compile(ast.Module([wrapper],type_ignores=[]),'<ARAM controller normalization>','exec'),scope)
    inspect,count=scope['doorway_helpers']()
    try:inspect()
    except AssertionError:
        assert ignored or stuck
        assert count()==0 and not scope['report'].get('acknowledged_home_inspections')
    else:
        assert not ignored and not stuck and count()==1
        assert scope['report']['acknowledged_home_inspections']==[1]
        assert events.count('B')==2 and events.index(('guest',3))<events.index('A')
        assert len(scope['report']['cleared_door_refusals'])==1
    if stuck:assert 'A' not in events
    assert events[0].startswith('RELEASE A\nRELEASE B\nRELEASE L\nRELEASE R\n')
print('ARAM doorway smoke: refusal dismissal, neutral controls, guest cooldown and acknowledged inspector routes passed')

# Restore coverage survives another legitimate retirement. The parser still
# reports current cold residency separately and only pairs within one session.
model_parser=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='aram_model_cycles')
population_parser=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='aram_model_population')
coverage_node=next(n for n in scenario.body if isinstance(n,ast.FunctionDef) and n.name=='uncovered_models')
coverage_scope={}
exec(compile(ast.Module([model_parser,population_parser,coverage_node],type_ignores=[]),'<ARAM model restore coverage>','exec'),coverage_scope)
model_cycles=coverage_scope['aram_model_cycles'];uncovered=coverage_scope['uncovered_models']
model_population=coverage_scope['aram_model_population']
population_marker='ANIMALTOWN MODEL POPULATION mask 3 actors 2\n'
ready='ANIMALTOWN READY version native bytes 123\n'
population=model_population(boot+population_marker+ready,0)
assert population['species']==[0,1] and population['actors']==2
model_cold='ANIMALTOWN MODEL COLD 1 aram bytes 4096 native budget bytes 64 max advance us 838\n'
model_warm='ANIMALTOWN MODEL WARM 1 aram bytes 4096 max advance us 838 readiness waits 0 portal wait ticks 0\n'
cycled=boot+model_cold+model_warm+model_cold
model_evidence=model_cycles(cycled,len(boot))
assert uncovered(model_evidence,population)==[] and model_evidence['restored_species']==[1]
assert model_evidence['cold_species']==model_evidence['unrestored_species']==[1]
startup=boot+model_cold+model_warm
assert uncovered(model_cycles(startup,len(startup)),population)==[1]
assert uncovered(model_cycles(boot+model_cold,len(boot)),population)==[1]
next_session=cycled+boot+model_cold
assert uncovered(model_cycles(next_session,0),population)==[1]
assert not model_cycles(next_session,0)['cycles']
assert not model_cycles(boot+model_cold+boot+model_warm,0)['cycles']
unused_cold=model_cold.replace('COLD 1','COLD 5')
unused_warm=model_warm.replace('WARM 1','WARM 5')
assert uncovered(model_cycles(cycled+unused_cold,len(boot)),population)==[]
# An unused package's genuine restore cannot cover a present actor's cold source.
assert uncovered(model_cycles(boot+model_cold+unused_cold+unused_warm,len(boot)),population)==[1]
legacy=model_population(boot+'ANIMALTOWN MODEL POPULATION mask 35 actors 3\n'+ready,0)
assert legacy['species']==[0,1,5]
assert uncovered(model_cycles(cycled+unused_cold,len(boot)),legacy)==[5]
assert model_population(boot+'ANIMALTOWN MODEL POPULATION mask 3 actors 8\n'+ready,0)['actors']==8
for invalid in (
    boot+ready,
    boot+population_marker+population_marker+ready,
    boot+population_marker.replace('mask 3','mask 0')+ready,
    boot+population_marker.replace('mask 3','mask 64')+ready,
    boot+population_marker.replace('mask 3','mask -1')+ready,
    boot+population_marker.replace('actors 2','actors 1')+ready,
    boot+population_marker.replace('actors 2','actors 0')+ready,
    boot+population_marker.replace('actors 2','actors 9')+ready,
    boot+population_marker.replace('mask 3','mask 2')+ready,
    boot+population_marker.replace('mask 3','mask unknown')+ready,
    boot+ready+population_marker,
    boot+population_marker+ready+boot+ready,
):
    try:model_population(invalid,0)
    except AssertionError:pass
    else:raise AssertionError('Malformed, stale or incomplete actual actor roster was accepted')
assert model_population(boot+population_marker+ready+boot+'ANIMALTOWN MODEL POPULATION mask 33 actors 2\n'+ready,0)['species']==[0,5]
print('ARAM model coverage: current actor roster, legacy/visitor cohorts, rereclamation, startup/session isolation and unused packages passed')

actor_node=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='aram_actor_view')
exec(compile(ast.Module([actor_node],type_ignores=[]),'<actual live actor routing>','exec'),coverage_scope)
actor_view=coverage_scope['aram_actor_view']
header='ANIMALTOWN JOB VIEW world 0 10240 20480 -1 0 0 0 0 0 0\n'
homes=''.join(f'ANIMALTOWN JOB NPC {i} 0 10240 20480 0 {10+i*11} 10\n' for i in range(6))
end='ANIMALTOWN JOB VIEW END\n'
fresh_population={'mask':31,'actors':6,'species':[0,1,2,3,4]}
fresh=actor_view(header+homes+end,fresh_population,True)
assert sorted(fresh['actors'])==list(range(5)) and 5 not in fresh['actors']
assert fresh['source']=='guarded fresh defaults'
actual='ANIMALTOWN JOB ACTOR 0 1 17 2048 3072 0\nANIMALTOWN JOB ACTOR 6 5 0 56320 61440 1\n'
visitors={'mask':35,'actors':3,'species':[0,1,5]}
live=actor_view(header+homes+actual+end,visitors)
assert live['actors'][0]['species']==1 and live['actors'][0]['room']==17
assert live['actors'][6]['species']==5 and live['actors'][6]['x']==55 and live['actors'][6]['busy']
for text,roster,defaults in (
    (header+homes+end,fresh_population,False),(header+homes+end,visitors,True),
    (header+actual,visitors,False),(header+actual+actual+end,visitors,False),
    (header+actual.replace('ACTOR 6','ACTOR 7')+end,visitors,False),
    (header+actual.replace('6 5 0','6 4 0')+end,visitors,False),
    (header+actual.replace('56320','131072')+end,visitors,False),
    (header+actual.replace('0 1 17','0 1 99')+end,visitors,False),
    (header+actual+end,{**visitors,'actors':4},False),
    (header.replace('world 0','world 99')+actual+end,visitors,False),
    (header.replace('10240 20480','-1 20480')+actual+end,visitors,False),
):
    try:actor_view(text,roster,defaults)
    except AssertionError:pass
    else:raise AssertionError('Ambiguous, absent or malformed live actor route was accepted')

# Execute the production fallback: an actor visiting another resident's house
# routes to that actual space, and only a paired runtime WARM completes it.
fallback=next(n for n in scenario.body if isinstance(n,ast.FunctionDef) and n.name=='restore_live_actor')
for restore in (True,False):
    events=[];trace=[boot+model_cold.replace('COLD 1','COLD 2')]
    actor={'slot':2,'species':2,'room':17 if restore else 0,'x':55,'z':60,'busy':False}
    current={'screen':'world','room':0,'x':10,'z':20,'actors':{2:actor},'source':'actual actors'}
    def enter_actual(name,target_species):
        events.append(('home',target_species));assert target_species==1 and current['room']==0
        trace.append(model_warm.replace('WARM 1','WARM '+str(actor['species'])));current['room']=17
    def leave_actual(name):
        events.append(('exit',current['room']));assert current['room']==17;current['room']=0
    scope={**coverage_scope,'actor_view':lambda:current,'report':{},'baseline':len(boot),
           'population':fresh_population,'logs':lambda:''.join(trace),'enter':enter_actual,
           'leave':leave_actual,
           'os':SimpleNamespace(write=lambda fd,data:events.append(data.decode())), 'controller':0,
           'guest':lambda name,seconds:None,'release_controls':lambda:events.append('release'),
           'screenshot':lambda name:events.append('screenshot')}
    exec(compile(ast.Module([fallback],type_ignores=[]),'<live actor fallback>','exec'),scope)
    try:scope['restore_live_actor'](2,'missing-actor')
    except AssertionError:
        assert not restore and not scope['report']['live_actor_routes'][0].get('restored')
        assert len(scope['report']['live_actor_routes'][0]['observations'])<20
    else:
        assert restore and scope['report']['live_actor_routes'][0]['restored']
        assert ('home',1) in events and 'screenshot' in events and ('exit',17) in events and current['room']==0
        # The next missing cohort must begin outdoors after a real exit.
        actor['species']=3;trace.append(model_cold.replace('COLD 1','COLD 3'))
        scope['restore_live_actor'](3,'next-missing-actor')
        assert current['room']==0 and events.count(('exit',17))==2
print('ARAM live routing: actual/remapped/visitor identities, absent actors, guarded old traces and genuine restore-only completion passed')

config_reader=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='card_dev_config')
config_namespace=dict(namespace)
exec(compile(ast.Module(body=[config_reader],type_ignores=[]),'<developer config reader>','exec'),config_namespace)
with TemporaryDirectory() as fixture:
    card=Path(fixture)/'MemoryCardA.USA.raw';card.touch()
    for version in (5,6,7):
        for retired in (0,1):
            for retired_warm in (0,1):
                checksum_at=40 if version>=6 else 36
                payload=bytearray(checksum_at+4);payload[:4]=b'ATD'+bytes([version]);payload[33]=retired
                if version>=6:payload[36]=retired_warm
                struct.pack_into('<I',payload,checksum_at,fnv(payload[:checksum_at]))
                config_namespace['options_card_files']=lambda path:{'dev_config':payload}
                decoded=config_namespace['card_dev_config'](card)
                assert decoded['retired_terrain_cache_value']==retired and decoded['values'][29]==retired
                assert decoded['retired_warm_prefetch_value']==(retired_warm if version>=6 else None)
                assert not decoded['full_resolution_aa']
                assert 'discard_terrain_cache_when_indoors' not in decoded and 'warm_terrain_prefetch' not in decoded
print('Developer config smoke: retired terrain slots remain decodable without controlling automatic memory policy')

# Dolphin's live raw-card writer is not an atomic snapshot. The final job
# export must use the bytes flushed by a clean exit, and a failed controller
# must never become a successful export just because cleanup succeeded.
jobs=runpy.run_path('tools/jobs_smoke.py')
job_scope={'subprocess':subprocess}
nodes=[n for n in module.body if isinstance(n,ast.FunctionDef) and n.name in ('stop_dolphin','run_jobs')]
exec(compile(ast.Module(nodes,type_ignores=[]),'job-shutdown','exec'),job_scope)

class JobProcess:
    def __init__(self,flush=lambda:None,code=0,timeout=False,forced_code=-9):
        self.returncode=None;self.flush=flush;self.code=code;self.timeout=timeout;self.forced_code=forced_code;self.events=[]
    def poll(self):return self.returncode
    def terminate(self):self.events.append('terminate')
    def wait(self,timeout=None):
        self.events.append('wait')
        if self.timeout and self.returncode is None:raise subprocess.TimeoutExpired('dolphin',timeout)
        if self.returncode is None:self.flush();self.returncode=self.code
        return self.returncode
    def kill(self):self.events.append('kill');self.returncode=self.forced_code

def job_report(mode='helipad',expected=2,completed=True):
    return dict(scenario='offline-jobs',mode=mode,controls_completed=completed,
                expected_saved_sequence=expected,passed=False)

def job_context(folder,process,exports):
    def current_files(path):
        assert process.poll()==0 and process.returncode==0,'Export read a running or abnormal Dolphin card'
        exports.append(path.name)
        return namespace['options_card_files'](path)
    return dict(root=folder,p=process,check_runtime_failure=lambda:None,
                options_card_files=current_files,committed_card_sequence=namespace['committed_card_sequence'])

def run_job(context,report,error=None):
    def drive(unused):
        if error is not None:raise error
        return report
    with patch.dict(sys.modules,{'jobs_smoke':SimpleNamespace(run=drive,finish=jobs['finish'])}):
        job_scope['run_jobs'](context)

def job_rejected(action,message):
    try:action()
    except AssertionError as error:assert message in str(error),str(error)
    else:raise AssertionError('Invalid final job export was accepted: '+message)

with TemporaryDirectory() as fixture:
    folder=Path(fixture);card=folder/'MemoryCardA.USA.raw';other=folder/'MemoryCardB.USA.raw'
    old=generation(0,7,0);latest=old|generation(1,11,40)
    write_card(card,old)
    for name in ('records','records-b','records-mid','records-guest'):
        (folder/name).mkdir();(folder/name/'stale').write_bytes(b'intermediate')
    def final_flush():
        write_card(card,latest);write_card(other,old)
    process=JobProcess(final_flush);exports=[];report=job_report()
    run_job(job_context(folder,process,exports),report)
    assert process.events==['terminate','wait'] and exports==[card.name,other.name]
    assert {p.name:p.read_bytes() for p in (folder/'records').iterdir()}==latest
    assert {p.name:p.read_bytes() for p in (folder/'records-b').iterdir()}==old
    assert (folder/'records-mid'/'stale').read_bytes()==(folder/'records-guest'/'stale').read_bytes()==b'intermediate'
    saved=json.loads((folder/'jobs-report.json').read_text())
    assert saved['shutdown']==dict(returncode=0,forced_kill=False,clean_exit=True)
    assert saved['expected_saved_sequence']==2 and saved['final_export']['complete'] and saved['controls_completed']
    assert saved['passed'] is False # Only the independent host state verifier can qualify the saved game.
    cards=saved['final_export']['cards']
    assert cards['A']['sequence']==2 and cards['B']['sequence']==1
    assert cards['A']['sha256']==__import__('hashlib').sha256(card.read_bytes()).hexdigest()
    assert {row['name']:row['sha256'] for row in cards['A']['records']}=={name:__import__('hashlib').sha256(data).hexdigest() for name,data in latest.items()}

with TemporaryDirectory() as fixture:
    folder=Path(fixture);exports=[];process=JobProcess()
    # A claimed clean status cannot substitute for observing process exit.
    job_rejected(lambda:jobs['finish'](job_context(folder,process,exports),job_report(),
                 dict(returncode=0,forced_kill=False,clean_exit=True)),'clean Dolphin exit')
    assert not exports and process.poll() is None
    for process in (JobProcess(code=17),JobProcess(timeout=True,forced_code=0)):
        report=job_report()
        job_rejected(lambda:run_job(job_context(folder,process,exports),report),'clean Dolphin exit')
        assert not exports and not report['final_export']['complete'] and not report['shutdown']['clean_exit']
    assert process.events==['terminate','wait','kill','wait'] and report['shutdown']['forced_kill']

with TemporaryDirectory() as fixture:
    folder=Path(fixture);exports=[];process=JobProcess();failure=AssertionError('Controller waypoint failure')
    try:run_job(job_context(folder,process,exports),job_report(),failure)
    except AssertionError as error:assert error is failure
    else:raise AssertionError('Controller failure was promoted after clean shutdown')
    assert process.poll()==0 and not exports and not (folder/'jobs-report.json').exists()
    report=job_report(completed=False)
    job_rejected(lambda:run_job(job_context(folder,JobProcess(),exports),report),'controls did not complete')
    assert not exports and report['controls_completed'] is False and not report['final_export']['complete']

with TemporaryDirectory() as fixture:
    folder=Path(fixture);card=folder/'MemoryCardA.USA.raw';write_card(card,generation(0,7,0))
    exports=[];report=job_report()
    job_rejected(lambda:run_job(job_context(folder,JobProcess(),exports),report),'older than the observed explicit save')
    assert report['final_export']['cards']['A']['sequence']==1 and not report['final_export']['complete']
    # The template intentionally contains no town, only a real console setting.
    write_card(card,{'AnimalTown-Debug':bytes(16)})
    (folder/'records-b'/'stale').write_bytes(b'removed-card')
    report=job_report(mode='template',expected=None)
    run_job(job_context(folder,JobProcess(),exports),report)
    assert report['final_export']['complete'] and report['final_export']['cards']['A']['sequence']==0
    assert {p.name for p in (folder/'records').iterdir()}=={'dev_config'}
    assert not list((folder/'records-b').iterdir()) and not report['final_export']['cards']['B']['present']

# Exercise the actual controller save helper: retain wait_saved_after's
# observed generation but leave routine final exports to the shutdown barrier.
run_node=next(n for n in ast.parse(Path('tools/jobs_smoke.py').read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='run')
save_node=next(n for n in run_node.body if isinstance(n,ast.FunctionDef) and n.name=='save')
observed=[];report=job_report(expected=None)
def saved_after(before,timeout_seconds):
    observed.append((before['sequence'],timeout_seconds));return {'sequence':7}
save_scope=dict(c=SimpleNamespace(root=Path('.'),controller=123,runtime_log_text=lambda:'',
                committed_card_sequence=lambda *a,**k:{'sequence':6},wait_saved_after=saved_after),
                mode='baseline',report=report,os=SimpleNamespace(write=lambda *a:None),press=lambda *a:None,
                wait_log=lambda *a:observed.append(a[0]),presentations=lambda *a:None,shot=lambda *a:None)
exec(compile(ast.Module([save_node],type_ignores=[]),'job-save-observation','exec'),save_scope)
save_scope['save']()
assert report['expected_saved_sequence']==7 and observed==[(6,240),'ANIMALTOWN SAVE TOTAL:']
print('Job final export: observed clean exit, forced/abnormal/controller failure rejection, stable dual cards, stale-file removal, save sequence and no-town template passed')
