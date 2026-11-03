"""Decode guest-clock telemetry from Dolphin FFV1 dumps or 640x480 video captures.

Usage: python3 tools/read-frame-profile.py CAPTURE.avi [MORE.avi ...]
Outputs JSON to stdout. Timings describe guest CPU/submission/waits, not GPU time.
The same checksummed pixels can be captured from physical console video output.
"""
import json
import math
import subprocess
import sys
from collections import defaultdict

names=['simulation','animation_skinning','terrain_prepare','frame_setup','terrain_submit',
       'actor_submit','tree_submit','props_submit','ui_submit','present_total',
       'gpu_blocking_wait','retrace_wait','frame_interval','task_delay','telemetry','ui_flush']
groups=defaultdict(list)
seen=set()
versions=set()
for path in sys.argv[1:]:
    command=['ffmpeg','-v','error','-i',path,'-vf','scale=640:480:flags=neighbor',
             '-pix_fmt','rgb24','-f','rawvideo','-']
    process=subprocess.Popen(command,stdout=subprocess.PIPE)
    while True:
        frame=process.stdout.read(640*480*3)
        if not frame:break
        if len(frame)!=640*480*3:raise RuntimeError('Truncated raw frame')
        values=[0]*32
        for bit in range(512):
            at=((450+bit//64*4)*640+bit%64*10+5)*3
            if frame[at]>128:values[bit//16]|=1<<(bit%16)
        checksum=0
        for value in values[:31]:checksum^=value
        if values[0]!=0x4154 or values[1] not in (1,2,3) or values[31]!=checksum:continue
        identity=tuple(values)
        if identity in seen:continue
        seen.add(identity)
        versions.add(values[1])
        groups[values[30]].append(values)
    if process.wait()!=0:raise RuntimeError('ffmpeg failed')

def stats(values):
    ordered=sorted(values)
    result={name:round(ordered[min(len(ordered)-1,math.ceil(q*len(ordered))-1)],3)
            for name,q in [('p50',.5),('p95',.95),('max',1)]}
    result['mean']=round(sum(values)/len(values),3)
    return result
if 3 in versions:
    if len(versions)!=1:raise RuntimeError('Cannot mix v3 phase definitions with older captures')
    names[3]='ground_items';names[4]='frame_setup_terrain_submit';names[7]='structures_props';names[15]='tree_selection'
# Distinguish actual camera/terrain rebuilds from cached world frames, using
# measured work rather than assuming injected stick input always moves a player.
groups[4]=[r for r in groups.get(0,[])[5:] if r[6]==0]
groups[5]=[r for r in groups.get(0,[])[5:] if r[6]!=0]
report={'units':'milliseconds; guest clock, not physical hardware or GPU duration',
        'capture_files':sys.argv[1:],'protocol_versions':sorted(versions),'scenarios':{}}
for scene,rows in groups.items():
    # Drop first five valid frames per scenario to reduce menu-transition effects.
    if scene<4:rows=rows[5:]
    if not rows:continue
    timings={}
    for i,name in enumerate(names):
        values=[r[4+i]/(100 if r[1]==1 else 10) for r in rows]
        result=stats(values)
        clipped=[r[4+i]==65535 and (r[3]!=0 if r[1]==1 else bool(r[3]&(1<<i))) for r in rows]
        result['censored_samples']=sum(clipped)
        # If clipping affected a percentile, its reported value is a lower bound.
        for key,q in [('p50',.5),('p95',.95),('max',1)]:
            selected=sorted(zip(values,clipped))[min(len(rows)-1,math.ceil(q*len(rows))-1)]
            result[key+'_is_lower_bound']=selected[1]
        timings[name]=result
    report['scenarios'][{0:'world',1:'backpack',2:'indoor',3:'other',4:'world_cached_terrain',5:'world_rebuilt_terrain'}.get(scene,str(scene))]={
        'frames':len(rows),'timings':timings,
        'triangles':stats([r[20]+(r[21]<<16) for r in rows]),
        'terrain_triangles':stats([r[22] for r in rows]),
        'rejected_terrain_quads':stats([r[23] for r in rows]),
        'texture_cache_kib':stats([r[25] for r in rows]),
        'culled_objects':stats([r[24] for r in rows]),
        'draw_submissions':stats([r[28] for r in rows]),
        'texture_bindings':stats([r[29] for r in rows]),
        'texture_cache_hits_max':max(r[26] for r in rows),'texture_cache_misses_max':max(r[27] for r in rows)}
if not report['scenarios']:raise RuntimeError('No valid AnimalTown telemetry found')
print(json.dumps(report,indent=2))
