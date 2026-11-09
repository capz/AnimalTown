"""Check logo retirement, repeated room rendering and bounded restore intervals.
Usage: analyze_asset_residency.py DIRECTORY --capture CAPTURE.avi [MORE.avi ...]
"""
import argparse,contextlib,hashlib,io,json,math,runpy,subprocess,sys
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--capture',type=Path,nargs='+',required=True);args=p.parse_args()
def rgb(stem):return subprocess.check_output(['ffmpeg','-v','error','-i',str(args.directory/(stem+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
def marker(stem):
    data=rgb(stem)
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
            if values[1]==5 and values[31]==check:return values
    raise AssertionError('Missing native marker: '+stem)
logo=marker('asset-title-logo');cold=marker('asset-outside-cold')
if logo[25]-cold[25]!=512:raise AssertionError('The 512 KiB logo payload was not retired exactly')
sys.argv=[str(Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py'),*[str(p) for p in args.capture]]
with contextlib.redirect_stdout(io.StringIO()):decoded=runpy.run_path(sys.argv[0])
def stats(values):
    values=sorted(values);return {'median':values[(len(values)-1)//2],'p95':values[math.ceil(len(values)*.95)-1],'max':values[-1]}
def interval(start,end,scene_change=False,fade=False):
    low,high=marker(start)[2],marker(end)[2]
    rows=sorted((r for scene in (0,2,2000) for r in decoded['groups'].get(scene,[]) if low<r[2]<=high),key=lambda r:r[2])
    if len(rows)!=high-low and not fade:raise AssertionError('Incomplete marked restore interval')
    details=[decoded['details'][r[2]] for r in rows]
    result={'observed_frames':len(rows),'elapsed_frames':high-low,'unreadable_frames':high-low-len(rows),'moving_frames':sum(bool(r[23]&4096) for r in rows),'frame_interval_ms':stats([r[16]/10 for r in rows]),'preparation_ms':stats([r[6]/10 for r in rows]),'terrain_stages':sum(r[23]&255 for r in rows),'mesh_creates':sum(d[10] for d in details),'mesh_updates':sum(d[11] for d in details),'mesh_releases':sum(d[12] for d in details),'loading_missing_recovery_frames':sum(bool(r[30]&(8192|16384|32768)) for r in rows),'gc_collections':sum(d[8]>>12 for d in details),'draw_api_texture_operations':sum(d[9]&255 for d in details),'shadow_page_updates':sum(d[9]>>8 for d in details)}
    forbidden=('loading_missing_recovery_frames',) if scene_change else ('terrain_stages','mesh_creates','mesh_updates','mesh_releases','loading_missing_recovery_frames')
    for key in forbidden:
        if result[key]:raise AssertionError('Unexpected terrain work while paging models: '+key)
    return result
first=rgb('asset-interior-first');second=rgb('asset-interior-second-reset')
def room(data):return b''.join(data[(y*800+170)*3:(y*800+440)*3] for y in range(80,315))
a,b=room(first),room(second);different=sum(x!=y for x,y in zip(a,b))
if different:raise AssertionError('Repeated room view differs after unloading/restoring: '+str(different)+' channels')
result={'scenario':json.loads((args.directory/'asset-residency-scenario.json').read_text()),'logo_texture_kib':{'title':logo[25],'after_retirement':cold[25],'freed':logo[25]-cold[25]},'room_region':[170,80,440,315],'room_rgb_sha256':hashlib.sha256(a).hexdigest(),'repeated_room_differing_channels':different,'inspector_early_prefetch':interval('asset-restore-start','asset-restore-end',scene_change=True),'inspector_prewarm_teleport_and_first_entry':interval('asset-restore-start','asset-interior-first',scene_change=True,fade=True),'ordinary_approach_before_entry':interval('asset-exit-cold','asset-approach-second'),'ordinary_approach_and_second_entry':interval('asset-exit-cold','asset-interior-second-reset',scene_change=True,fade=True),'method':'Single software-rendered Dolphin capture; guest counters have 0.1 ms resolution; intervals include stationary frames. Early-prefetch markers can precede complete restoration. Entry endpoints span every required restore and the scene transition, but doorway fades obscure some encoded frames. Those gaps are reported, not counted as passing timing samples. Draw API counters do not include ARAM model/texture byte copies.'}
print(json.dumps(result,indent=2))
