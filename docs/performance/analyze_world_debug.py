"""Validate stationary indoor UI/TEV evidence and marked default ground travel.

Usage: analyze_world_debug.py OUTDOOR_DIR INDOOR_DIR --capture OUTDOOR.mkv
"""
import argparse,contextlib,io,json,math,runpy,subprocess,sys
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('outdoor',type=Path);p.add_argument('indoor',type=Path);p.add_argument('--capture',type=Path,required=True);args=p.parse_args()
def pixels(root,name):return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
def marker(name):
 data=pixels(args.outdoor,name)
 for origin in range(20,36):
  for width in range(570,601):
   def word(index):
    value=0
    for bit in range(16):
     n=index*16+bit;x=origin+int((n%64+.5)*width/64);y=450+n//64*4
     if data[(y*800+x)*3]>128:value|=1<<bit
    return value
   if word(0)!=0x4154:continue
   values=[word(n) for n in range(32)];check=0
   for v in values[:31]:check^=v
   if values[1]==5 and check==values[31]:return values[2]
 raise ValueError('Invalid marker '+name)
def stats(values):
 values=sorted(values);return {'median':values[(len(values)-1)//2],'p95':values[math.ceil(len(values)*.95)-1],'max':values[-1]}
a=pixels(args.indoor,'rim-zero-world.png');b=pixels(args.indoor,'rim-reset-100-world.png');c=pixels(args.indoor,'rim-200-world.png')
brightness=[]
for data in (a,b,c):brightness.append(sum(sum(data[(y*800+x)*3:(y*800+x)*3+3]) for y in range(70,315) for x in range(200,430)))
if not brightness[0]<brightness[1]<brightness[2]:raise AssertionError('Native rim intensity is not monotonic')
def label(name):
 data=pixels(args.indoor,name);return b''.join(data[(y*800+100)*3:(y*800+390)*3] for y in range(179,201))
if label('rim-default-100-menu.png')!=label('rim-reset-100-menu.png'):raise AssertionError('Rim reset did not restore the exact default label')
if label('rim-zero-menu.png')==label('rim-200-menu.png'):raise AssertionError('Numerical label did not change')
def paper(name):
 data=pixels(args.indoor,name)
 return sum(all(abs(data[(y*800+x)*3+c]-v)<4 for c,v in enumerate((255,245,221))) for y in range(70,210) for x in range(220,440))
bubbles={'off':paper('attention-default-off.png'),'on':paper('attention-on-0.png'),'restored_off':paper('attention-restored-off.png')}
if bubbles['on']<bubbles['off']+400 or bubbles['on']<bubbles['restored_off']+400:raise AssertionError('Forced attention bubbles did not appear/restore')
low=marker('ground-fast-start.png');high=marker('ground-fast-end.png');decoder=Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py';sys.argv=[str(decoder),str(args.capture)]
with contextlib.redirect_stdout(io.StringIO()):decoded=runpy.run_path(str(decoder))
rows=[r for r in decoded['groups'][0] if low<r[2]<=high]
if len(rows)!=high-low:raise ValueError('Incomplete movement interval')
details=[decoded['details'][r[2]] for r in rows]
result={'frame_interval_ms':stats([r[16]/10 for r in rows]),'terrain_prepare_ms':stats([r[6]/10 for r in rows]),'frames':len(rows),'moving_frames':sum(bool(r[23]&4096) for r in rows),'terrain_stages':sum(r[23]&255 for r in rows),'loading_missing_recovery_frames':sum(bool(r[30]&(8192|16384|32768)) for r in rows),'mesh_creates':sum(d[10] for d in details),'mesh_updates':sum(d[11] for d in details),'mesh_releases':sum(d[12] for d in details),'texture_operations':sum(d[9]&255 for d in details),'shadow_page_updates':sum(d[9]>>8 for d in details),'gc_collections':sum(d[8]>>12 for d in details)}
if result['moving_frames']<30:raise AssertionError('Route did not walk far enough')
for key in ('terrain_stages','loading_missing_recovery_frames','mesh_creates','mesh_updates','mesh_releases'):
 if result[key]:raise AssertionError('Default optimized ground unexpectedly did '+key)
report={'method':'Single instrumented Dolphin captures; guest 0.1 ms counters, not hardware timing','native_rim_region_rgb_sums':dict(zip(('zero','default_100','maximum_200'),brightness)),'bubble_paper_pixels':bubbles,'default_ground_travel':result,'outdoor':json.loads((args.outdoor/'world-debug-scenario.json').read_text()),'indoor':json.loads((args.indoor/'world-debug-scenario.json').read_text())}
print(json.dumps(report,indent=2))
