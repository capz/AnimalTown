"""Validate ground mip controls, static texture detail and marked native travel.

Usage: analyze_ground_mips.py OUTPUT_DIR --capture CAPTURE.avi [MORE.avi ...]
"""
import argparse,contextlib,io,json,math,runpy,subprocess,sys
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory',type=Path);parser.add_argument('--capture',type=Path,nargs='+',required=True);args=parser.parse_args()
def pixels(stem):
    return subprocess.check_output(['ffmpeg','-v','error','-i',str(args.directory/(stem+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
def region(data,box):
    left,top,right,bottom=box
    return b''.join(data[(y*800+left)*3:(y*800+right)*3] for y in range(top,bottom))
auto=pixels('mip-default-auto-menu');one=pixels('mip-one-menu');ten=pixels('mip-ten-menu');reset=pixels('mip-reset-auto-menu')
label=(90,251,410,273)
if region(auto,label)!=region(reset,label):raise AssertionError('Z did not restore the default Auto label')
if len({region(p,label) for p in (auto,one,ten)})!=3:raise AssertionError('Numeric mip labels did not change')
if region(pixels('mip-default-on-menu'),(90,227,350,249))==region(pixels('mip-off-menu'),(90,227,350,249)):raise AssertionError('Ground mip enable label did not change')
def marker(stem):
    data=pixels(stem)
    for origin in range(20,36):
        for width in range(570,601):
            values=[]
            for index in range(32):
                value=0
                for bit in range(16):
                    n=index*16+bit;x=origin+int((n%64+.5)*width/64);y=450+n//64*4
                    if data[(y*800+x)*3]>128:value|=1<<bit
                values.append(value)
                if index==0 and value!=0x4154:break
            if len(values)!=32:continue
            check=0
            for value in values[:31]:check^=value
            if values[1]==5 and check==values[31]:return values[2]
    raise AssertionError('Missing marker: '+stem)
def stats(values):
    values=sorted(values);return {'median':values[(len(values)-1)//2],'p95':values[math.ceil(len(values)*.95)-1],'max':values[-1]}
low,high=marker('mip-travel-start'),marker('mip-travel-end')
sys.argv=[str(Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py'),*[str(p) for p in args.capture]]
with contextlib.redirect_stdout(io.StringIO()):decoded=runpy.run_path(sys.argv[0])
rows=[r for r in decoded['groups'][0] if low<r[2]<=high]
if len(rows)!=high-low:raise AssertionError('Incomplete marked travel capture')
details=[decoded['details'][r[2]] for r in rows]
travel={'frames':len(rows),'moving_frames':sum(bool(r[23]&4096) for r in rows),'frame_interval_ms':stats([r[16]/10 for r in rows]),'terrain_prepare_ms':stats([r[6]/10 for r in rows]),'terrain_stages':sum(r[23]&255 for r in rows),'loading_missing_recovery_frames':sum(bool(r[30]&(8192|16384|32768)) for r in rows),'mesh_creates':sum(d[10] for d in details),'mesh_updates':sum(d[11] for d in details),'mesh_releases':sum(d[12] for d in details),'texture_operations':sum(d[9]&255 for d in details),'shadow_page_updates':sum(d[9]>>8 for d in details),'gc_collections':sum(d[8]>>12 for d in details)}
if travel['moving_frames']<30:raise AssertionError('Travel did not move the player')
for key in ('terrain_stages','loading_missing_recovery_frames','mesh_creates','mesh_updates','mesh_releases','texture_operations','gc_collections'):
    if travel[key]:raise AssertionError('Unexpected movement work: '+key)
# Fixed ground-only foreground region; no actor, menu, water or date panel.
box=(400,300,540,350)
def energy(stem):
    data=pixels(stem);left,top,right,bottom=box
    return sum(abs(data[(y*800+x)*3]-data[(y*800+x+1)*3]) for y in range(top,bottom) for x in range(left,right-1))
contrast={name:energy('mip-'+name+'-world') for name in ('auto','off','one','ten','reset-auto')}
if not contrast['off']>contrast['auto'] or not contrast['ten']>contrast['one']:raise AssertionError('Native mip settings did not change foreground detail as expected')
result={'scenario':json.loads((args.directory/'ground-mip-scenario.json').read_text()),'auto_reset_and_numeric_labels_verified':True,'ground_region':box,'ground_horizontal_red_edge_energy':contrast,'default_ground_travel':travel,'method':'Single Dolphin software-rendered capture; guest counters at 0.1 ms resolution, not hardware timing'}
print(json.dumps(result,indent=2))
