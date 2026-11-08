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
paths=[path for path in sys.argv[1:] if path not in ('--interaction-panels','--check-movement')]
for path in paths:
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
        if values[0]!=0x4154 or values[1] not in (1,2,3,4,5) or values[31]!=checksum:continue
        if '--interaction-panels' in sys.argv and values[30] in (2,3):
            # Old releases used "other" for all these panels. Classify their
            # distinct opaque panel coverage without modifying that ISO.
            def cream(x,y):
                at=(y*640+x)*3
                return all(abs(frame[at+c]-target)<=3 for c,target in enumerate((255,245,221)))
            if cream(60,200) and cream(560,200):values[30]=13
            elif cream(40,375) and cream(600,375):
                # A third body line distinguishes the longer Controls text
                # from the short world interaction in the profiling script.
                ink=sum(all(abs(frame[(y*640+x)*3+c]-target)<=3 for c,target in enumerate((49,72,62))) for y in range(389,404) for x in range(57,550))
                values[30]=14 if ink>20 else 11
            elif cream(300,120) and cream(330,360):values[30]=12
            elif values[30]==3:continue
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
if versions.intersection((3,4,5)):
    if len(versions)!=1:raise RuntimeError('Cannot mix phase/counter protocol versions')
    names[3]='ground_items';names[4]='frame_setup_terrain_submit';names[7]='structures_props';names[15]='tree_selection'
# Distinguish actual camera/terrain rebuilds from cached world frames, using
# measured work rather than assuming injected stick input always moves a player.
def rebuilt(row):return (row[23]&255)!=0 if row[1]==5 else row[6]!=0
for source,cached,changed in ((0,4,5),(6,8,9),(16,20,21)):
    groups[cached]=[r for r in groups.get(source,[])[5:] if not rebuilt(r)]
    groups[changed]=[r for r in groups.get(source,[])[5:] if rebuilt(r)]
if versions=={5}:
    world=[r for r in groups.get(0,[]) if r[1]==5]
    groups[1000]=[r for r in world if not r[23]&4096]
    groups[1001]=[r for r in world if r[23]&4096 and not r[23]&8192]
    groups[1002]=[r for r in world if r[23]&8192]
report={'units':'milliseconds; guest clock, not physical hardware or GPU duration',
        'capture_files':paths,'protocol_versions':sorted(versions),'scenarios':{}}
for scene,rows in groups.items():
    # Drop first five valid frames per scenario to reduce menu-transition effects.
    if scene in (0,1,2,3,6,7,10,11,12,13,14,16,17,18,19,22,23,26,32,33,42):rows=rows[5:]
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
    report['scenarios'][{0:'world',1:'backpack',2:'indoor',3:'other',4:'world_cached_terrain',5:'world_rebuilt_terrain',6:'world_direct',7:'backpack_direct',8:'world_direct_cached_terrain',9:'world_direct_rebuilt_terrain',10:'map',11:'dialogue',12:'item_actions',13:'interaction_list',14:'dialogue_long',16:'world_baseline',17:'backpack_baseline',20:'world_baseline_cached_terrain',21:'world_baseline_rebuilt_terrain',26:'map_baseline',32:'world_legacy_font',33:'backpack_legacy_font',42:'map_legacy_font',1000:'stationary',1001:'walking',1002:'running'}.get(scene,('runtime_'+str(scene//64)+'_scene_'+str(scene%64) if scene>=64 else str(scene)))]={
        'frames':len(rows),'timings':timings,
        'triangles':stats([r[20]+(r[21]<<16) for r in rows]),
        'terrain_triangles':stats([r[22] for r in rows]),
        **({'terrain_patch_builds':stats([r[23]&255 for r in rows]),'shadow_deferred_frames':sum(bool(r[23]&2048) for r in rows),'animation_update_calls':stats([(r[23]>>8)&7 for r in rows]),'ground_ring_enabled':stats([int(bool(r[23]&16384)) for r in rows]),'ground_ring_uploads':stats([int(bool(r[23]&32768)) for r in rows])} if versions=={5} else {'rejected_terrain_quads':stats([r[23] for r in rows])}),
        'texture_cache_kib':stats([r[25] for r in rows]),
        'culled_objects':stats([r[24] for r in rows]),
        'draw_submissions':stats([r[28] for r in rows]),
        'texture_bindings':stats([r[29] for r in rows]),
        **({'drawing_api_calls':stats([r[26] for r in rows]),'material_changes':stats([r[27] for r in rows])} if versions in ({4},{5}) else
           {'texture_cache_hits_max':max(r[26] for r in rows),'texture_cache_misses_max':max(r[27] for r in rows)})}
if not report['scenarios']:raise RuntimeError('No valid AnimalTown telemetry found')
print(json.dumps(report,indent=2))

if '--check-movement' in sys.argv:
    if versions!={5}:raise RuntimeError('Movement checks require telemetry v5')
    stationary=report['scenarios'].get('stationary')
    if stationary and stationary['terrain_patch_builds']['p50']!=0:raise RuntimeError('Terrain cache keeps rebuilding while stationary')
    for scenario in ('walking','running'):
        result=report['scenarios'].get(scenario)
        if result is None or result['frames']<30:raise RuntimeError('Not enough actual '+scenario+' frames')
        if result['timings']['frame_interval']['p95']>50:raise RuntimeError(scenario+' p95 exceeds 50 ms')
