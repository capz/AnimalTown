"""Texture lifecycle checks plus exact static-view reuse and sustained travel.
Run DIRECTORY --capture CAPTURE.avi [MORE.avi ...], after texture_streaming.py.
"""
from pathlib import Path
import contextlib,io,json,sys
skip_walk='--skip-walk' in sys.argv;travel_only='--travel-only' in sys.argv
for flag in ('--skip-walk','--travel-only'):
    if flag in sys.argv:sys.argv.remove(flag)
script=Path(__file__).with_name('analyze_asset_residency.py')
source=script.read_text().replace("first=rgb('asset-interior-first')", "first=rgb('asset-interior-sharp')")
source=source[:source.rindex('if result['scenario'].get('explicit_save') and 'card_unchanged' in result['scenario']:
    result['scenario']['card_unchanged_before_explicit_save']=result['scenario'].pop('card_unchanged')
print(json.dumps(result,indent=2))')]
namespace={'__name__':'__main__','__file__':str(script)}
if travel_only:
    exec(compile(source[:source.index("logo=marker")],str(script),'exec'),namespace)
    import runpy,math
    args=namespace['args'];sys.argv=[str(Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py'),*[str(p) for p in args.capture]]
    with contextlib.redirect_stdout(io.StringIO()):namespace['decoded']=runpy.run_path(sys.argv[0])
    def stats(values):
        values=sorted(values);return {'median':values[(len(values)-1)//2],'p95':values[math.ceil(len(values)*.95)-1],'max':values[-1]}
    namespace['stats']=stats;namespace['room']=lambda data:data
    namespace['result']={'scenario':json.loads((args.directory/'texture-travel-scenario.json').read_text()),'method':'Separate unobscured outdoor capture; checksummed guest telemetry, 0.1 ms timing resolution. Not physical DVD performance.'}
else:exec(compile(source,str(script),'exec'),namespace)
result=namespace['result'];rgb=namespace['rgb'];room=namespace['room'];marker=namespace['marker'];decoded=namespace['decoded'];stats=namespace['stats']
directory=namespace['args'].directory
if (directory/'texture-reuse-on.png').exists():
    # Ignore HUD/telemetry. Logical clocks, materials, world poses and camera
    # remain independent of skin/list/placement storage choices.
    normal=room(rgb('asset-interior-second-reset'))
    comparisons={}
    for stem in ['texture-lists-off','texture-placement-off','texture-poses-off','texture-poses-on','texture-placement-on','texture-reuse-on']:
        other=room(rgb(stem));different=sum(a!=b for a,b in zip(normal,other));comparisons[stem]=different
        if different:raise AssertionError(f'Reuse switch changed the fixed room image: {stem}: {different} channels')
    result['reuse_fixed_view_differing_channels']=comparisons
if not skip_walk and (directory/'texture-walk-3.png').exists():
    low,high=marker('texture-walk-start')[2],marker('texture-walk-3')[2]
    rows=sorted((r for scene in (0,2,2000) for r in decoded['groups'].get(scene,[]) if low<r[2]<=high),key=lambda r:r[2])
    if not rows or len(rows)!=high-low:raise AssertionError('Walking interval is incomplete')
    forbidden=sum(bool(r[30]&(8192|16384|32768)) for r in rows)
    moving=sum(bool(r[23]&4096) for r in rows)
    if forbidden or moving<30:raise AssertionError('Walking did not retain a drawable scene or enough actual movement')
    result['sustained_walk']={'frames':len(rows),'moving_frames':moving,'loading_missing_recovery_frames':forbidden,'frame_interval_ms':stats([r[16]/10 for r in rows]),'preparation_ms':stats([r[6]/10 for r in rows])}
if result['scenario'].get('explicit_save') and 'card_unchanged' in result['scenario']:
    result['scenario']['card_unchanged_before_explicit_save']=result['scenario'].pop('card_unchanged')
print(json.dumps(result,indent=2))
