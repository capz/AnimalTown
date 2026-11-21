"""Decode guest-clock telemetry from Dolphin FFV1 dumps or 640x480 video captures.

Usage: python3 tools/read-frame-profile.py CAPTURE.avi [MORE.avi ...]
Outputs JSON to stdout. Timings describe guest CPU/submission/waits, not GPU time.
The same checksummed pixels can be captured from physical console video output.
"""
import json
import math
import subprocess
import sys
import argparse
from pathlib import Path
from collections import defaultdict
sys.path.insert(0, str(Path(__file__).resolve().parent))
from profile_wire import crc, u32, valid_identity, memory_report

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('paths',nargs='+')
parser.add_argument('--platform',choices=('dolphin','hardware','unknown'),default='unknown')
parser.add_argument('--storage',default='unspecified')
parser.add_argument('--video-mode',default='unspecified')
parser.add_argument('--metadata',help='JSON provenance: commit, SDK, engine, artifact/fixture hashes and debug configuration')
for flag in ('interaction-panels','check-movement','check-streaming'):parser.add_argument('--'+flag,action='store_true')
args=parser.parse_args()

names=['simulation','animation_skinning','terrain_prepare','frame_setup','terrain_submit',
       'actor_submit','tree_submit','props_submit','ui_submit','present_total',
       'gpu_blocking_wait','retrace_wait','frame_interval','task_delay','telemetry','ui_flush']
groups=defaultdict(list)
seen=set()
versions=set()
details={}
submissions={}
frame_keys={};intervals={};memory_parts={};sessions=set();last_sequences={};configurations=defaultdict(int);queued=[]
repeated=missing=invalid_v6=0
paths=args.paths
def band(frame,y,count):
    words=[0]*count
    for bit in range(count*16):
        at=((y+2+bit//64*4)*640+bit%64*10+5)*3
        if frame[at]>128:words[bit//16]|=1<<(bit%16)
    return words
for path_index,path in enumerate(paths):
    legacy_epoch=0;legacy_previous=None;last_legacy=None
    command=['ffmpeg','-v','error','-i',path,'-vf','scale=640:480:flags=neighbor',
             '-pix_fmt','rgb24','-f','rawvideo','-']
    process=subprocess.Popen(command,stdout=subprocess.PIPE)
    while True:
        frame=process.stdout.read(640*480*3)
        if not frame:break
        if len(frame)!=640*480*3:raise RuntimeError('Truncated raw frame')
        values=band(frame,448,32)
        checksum=0
        for value in values[:31]:checksum^=value
        if values[0]!=0x4154 or values[1] not in (1,2,3,4,5,6) or values[31]!=checksum:continue
        extra=band(frame,432,16);batch=band(frame,416,16)
        if values[1]==6:
            ident=band(frame,400,16);mem=band(frame,384,16)
            if not valid_identity(values,extra,batch,mem,ident):invalid_v6+=1;continue
            session=u32(ident,2);seq=u32(ident,4);identity=(session,seq);sessions.add(session)
            if identity in seen:repeated+=1;continue
            seen.add(identity)
            if session in last_sequences:
                delta=(seq-last_sequences[session])&0xffffffff
                if 1<delta<0x80000000:missing+=delta-1
            last_sequences[session]=seq
            intervals[identity]=u32(ident,6)/1000
            configurations[(ident[8]&3,bool(ident[8]&256),bool(ident[13]&1))]+=1;queued.append(ident[12])
            part=memory_parts.setdefault((session,u32(mem,4)),{})
            part[mem[6]]=[u32(mem,7+i*2) for i in range(4)]
        else:
            wire=tuple(values)
            if wire==last_legacy:repeated+=1;continue
            last_legacy=wire
            if legacy_previous is not None and values[2]<legacy_previous and legacy_previous-values[2]>32768:legacy_epoch+=1
            legacy_previous=values[2];identity=('legacy',path_index,legacy_epoch,values[2])
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
        frame_keys[id(values)]=identity
        versions.add(values[1])
        groups[2000 if values[30]&32768 else values[30]&8191].append(values)
        check=0
        for value in extra[:15]:check^=value
        if extra[0]==0x4144 and extra[1]==values[2] and extra[15]==check:details[identity]=extra
        check=0
        for value in batch[:15]:check^=value
        if batch[0]==0x4153 and batch[1]==1 and batch[2]==values[2] and batch[15]==check:submissions[identity]=batch
    if process.wait()!=0:raise RuntimeError('ffmpeg failed')

def stats(values):
    ordered=sorted(values)
    result={name:round(ordered[min(len(ordered)-1,math.ceil(q*len(ordered))-1)],3)
            for name,q in [('p50',.5),('p95',.95),('p99',.99),('max',1)]}
    result['mean']=round(sum(values)/len(values),3)
    return result
if versions.intersection((3,4,5,6)):
    if len(versions)!=1:raise RuntimeError('Cannot mix phase/counter protocol versions')
    names[3]='ground_items';names[4]='frame_setup_terrain_submit';names[7]='structures_props';names[15]='tree_selection'
# Distinguish actual camera/terrain rebuilds from cached world frames, using
# measured work rather than assuming injected stick input always moves a player.
def rebuilt(row):return (row[23]&255)!=0 if row[1]>=5 else row[6]!=0
for source,cached,changed in ((0,4,5),(6,8,9),(16,20,21)):
    groups[cached]=[r for r in groups.get(source,[]) if not rebuilt(r)]
    groups[changed]=[r for r in groups.get(source,[]) if rebuilt(r)]
if versions in ({5},{6}):
    world=[r for r in groups.get(0,[]) if r[1]>=5]
    groups[1000]=[r for r in world if not r[23]&4096]
    groups[1001]=[r for r in world if r[23]&4096 and not r[23]&8192]
    groups[1002]=[r for r in world if r[23]&8192]
provenance=json.loads(Path(args.metadata).read_text()) if args.metadata else {}
report={'units':'milliseconds; '+('console timebase' if args.platform=='hardware' else 'guest clock' if args.platform=='dolphin' else 'platform unspecified')+'; CPU submission and blocking waits, not GPU execution duration',
        'platform':args.platform,'storage':args.storage,'video_mode':args.video_mode,'provenance':provenance,
        'capture_files':paths,'protocol_versions':sorted(versions),'sessions':sorted(sessions),
        'repeated_video_samples':repeated,'missing_sequence_samples':missing,'invalid_v6_samples':invalid_v6,
        'excluded_transition_samples':0,'legacy_identity_limitations':bool(versions-{6}),
        'landscape_loading_frames':len(groups.get(2000,[])),'scenarios':{}}
report['memory_snapshots']=[{'session':key[0],'sequence':key[1],**memory_report([v for part in range(8) for v in parts[part]])} for key,parts in memory_parts.items() if len(parts)==8]
report['incomplete_memory_snapshots']=sum(len(parts)!=8 for parts in memory_parts.values())
report['configurations']=[{'profiling_mode':key[0],'warm_terrain_prefetch':key[1],'full_resolution_aa':key[2],'frames':count} for key,count in configurations.items()]
report['terrain_queued_sections']=stats(queued) if queued else None
report['hardware_qualification_missing']=[name for name in ('commit','sdk','engine','artifact_sha256','fixture_sha256','debug_settings') if name not in provenance]
for scene,rows in groups.items():
    if not rows:continue
    timings={}
    for i,name in enumerate(names):
        values=[intervals[frame_keys[id(r)]] if i==12 and r[1]==6 else r[4+i]/(100 if r[1]==1 else 10) for r in rows]
        result=stats(values)
        clipped=[r[4+i]==65535 and (r[3]!=0 if r[1]==1 else bool(r[3]&(1<<i))) for r in rows]
        result['censored_samples']=sum(clipped)
        # If clipping affected a percentile, its reported value is a lower bound.
        for key,q in [('p50',.5),('p95',.95),('p99',.99),('max',1)]:
            selected=sorted(zip(values,clipped))[min(len(rows)-1,math.ceil(q*len(rows))-1)]
            result[key+'_is_lower_bound']=selected[1]
        timings[name]=result
    report['scenarios'][{0:'world',1:'backpack',2:'indoor',3:'other',4:'world_cached_terrain',5:'world_rebuilt_terrain',6:'world_direct',7:'backpack_direct',8:'world_direct_cached_terrain',9:'world_direct_rebuilt_terrain',10:'map',11:'dialogue',12:'item_actions',13:'interaction_list',14:'dialogue_long',16:'world_baseline',17:'backpack_baseline',20:'world_baseline_cached_terrain',21:'world_baseline_rebuilt_terrain',26:'map_baseline',32:'world_legacy_font',33:'backpack_legacy_font',42:'map_legacy_font',1000:'stationary',1001:'walking',1002:'running'}.get(scene,('runtime_'+str(scene//64)+'_scene_'+str(scene%64) if scene>=64 else str(scene)))]={
        'frames':len(rows),'timings':timings,
        'missing_terrain_frames':sum(bool(r[30]&16384) for r in rows),
        'terrain_repair_frames':sum(bool(r[30]&8192) for r in rows),
        'triangles':stats([r[20]+(r[21]<<16) for r in rows]),
        'terrain_triangles':stats([r[22] for r in rows]),
        **({'terrain_patch_builds':stats([r[23]&255 for r in rows]),'shadow_deferred_frames':sum(bool(r[23]&2048) for r in rows),'animation_update_calls':stats([(r[23]>>8)&7 for r in rows]),'ground_ring_enabled':stats([int(bool(r[23]&16384)) for r in rows]),'ground_ring_uploads':stats([int(bool(r[23]&32768)) for r in rows])} if versions in ({5},{6}) else {'rejected_terrain_quads':stats([r[23] for r in rows])}),
        'texture_cache_kib':stats([r[25] for r in rows]),
        'culled_objects':stats([r[24] for r in rows]),
        'draw_submissions':stats([r[28] for r in rows]),
        **({'pass_draw_submissions':{name:stats([submissions[frame_keys[id(r)]][i] for r in rows if frame_keys[id(r)] in submissions]) for i,name in enumerate(('terrain_or_interior','trees','buildings','fences','rain','ui_before_profiler','ui_final_flush'),3)}} if all(frame_keys[id(r)] in submissions for r in rows) else {}),
        'texture_bindings':stats([r[29] for r in rows]),
        **({'drawing_api_calls':stats([r[26] for r in rows]),'material_changes':stats([r[27] for r in rows])} if versions in ({4},{5},{6}) else
           {'texture_cache_hits_max':max(r[26] for r in rows),'texture_cache_misses_max':max(r[27] for r in rows)})}
if details:
    rows=list(details.values())
    report['preparation_details']={'frames':len(rows),
        **{name:stats([r[i]/10 for r in rows]) for i,name in enumerate(('selection_ms','generation_ms','publication_ms','material_animation_ms','native_masks_ms'),2)},
        **{name:stats([r[i] for r in rows]) for i,name in {7:'managed_allocations',10:'mesh_creates',11:'mesh_updates',12:'mesh_releases',13:'staging_growths',14:'recycled_sections'}.items()},
        'gc_collections':sum(r[8]>>12 for r in rows),'free_block_searches':sum(r[8]&4095 for r in rows),
        'texture_operations':sum(r[9]&255 for r in rows),'shadow_page_updates':sum(r[9]>>8 for r in rows)}
if not report['scenarios']:raise RuntimeError('No valid AnimalTown telemetry found')
print(json.dumps(report,indent=2))

if '--check-streaming' in sys.argv:
    if versions not in ({5},{6}):raise RuntimeError('Streaming checks require telemetry v5/v6')
    if report['landscape_loading_frames']:raise RuntimeError('Landscape loading interrupted travel')
    world=report['scenarios'].get('world')
    if not world or world['frames']<100:raise RuntimeError('Not enough world frames for streaming checks')
    if world['missing_terrain_frames']:raise RuntimeError('Visible terrain was missing during travel')

if '--check-movement' in sys.argv:
    if versions not in ({5},{6}):raise RuntimeError('Movement checks require telemetry v5/v6')
    stationary=report['scenarios'].get('stationary')
    if stationary and stationary['terrain_patch_builds']['p50']!=0:raise RuntimeError('Terrain cache keeps rebuilding while stationary')
    for scenario in ('walking','running'):
        result=report['scenarios'].get(scenario)
        if result is None or result['frames']<30:raise RuntimeError('Not enough actual '+scenario+' frames')
        if result['timings']['frame_interval']['p95']>50:raise RuntimeError(scenario+' p95 exceeds 50 ms')
