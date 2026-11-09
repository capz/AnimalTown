"""Summarize exactly the marked route from guest_travel.py.

Usage: summarize_travel.py OUTPUT_DIRECTORY [--capture CAPTURE.avi|CAPTURE.mkv]
JSON goes to stdout. Profiler-off runs use the preserved AVI presentation index;
instrumented runs decode the checksummed bars, excluding warmup and save work.
"""
import argparse
import contextlib
import io
import json
import math
from pathlib import Path
import runpy
import sys

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory',type=Path)
parser.add_argument('--capture',type=Path)
args=parser.parse_args()
scenario=json.loads((args.directory/'travel-scenario.json').read_text())

def stats(values):
    values=sorted(values)
    if not values:raise ValueError('No marked route samples')
    return {'median':round(values[(len(values)-1)//2],4),
            'p95':round(values[math.ceil(len(values)*.95)-1],4),
            'max':round(values[-1],4),'mean':round(sum(values)/len(values),4)}

result={'disc':scenario['disc'],'actual_guest_seconds':scenario['actual_guest_seconds'],
        'overlay_enabled':scenario.get('overlay_enabled',True)}
if not result['overlay_enabled']:
    low,high=scenario['vi_markers'][0],scenario['vi_markers'][-1]
    presented=[v for v in scenario['presented_vi_indices'] if low<v<=high]
    gaps=[b-a for a,b in zip(presented,presented[1:])]
    if len(gaps)<100 or any(g<=0 for g in gaps):raise ValueError('Incomplete or unordered AVI presentation index')
    result.update({'units':'guest milliseconds; one NTSC VI resolution; profiler off; capture overhead remains',
                   'guest_seconds':(high-low)*1001/60000,'presentations':len(presented),
                   'frame_interval_ms':stats([g*1001/60 for g in gaps]),
                   'multi_vi_intervals':sum(g>1 for g in gaps),
                   'multi_vi_percent':sum(g>1 for g in gaps)*100/len(gaps)})
else:
    marks=scenario['frame_counters']
    if any(b<=a for a,b in zip(marks,marks[1:])):raise ValueError('Wrapped/non-progressing native frame markers are unsupported')
    captures=[args.capture] if args.capture else sorted((args.directory/'user/Dump/Frames').glob('*.avi'))
    if not captures:raise ValueError('Pass --capture when the AVI has been losslessly archived')
    decoder=Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py'
    sys.argv=[str(decoder),*[str(p) for p in captures],'--check-streaming','--check-movement']
    with contextlib.redirect_stdout(io.StringIO()):decoded=runpy.run_path(str(decoder))
    rows=[r for r in decoded['groups'][0] if marks[0]<r[2]<=marks[-1]]
    if len(rows)<100 or len({r[2] for r in rows})!=len(rows):raise ValueError('Incomplete or wrapped native route')
    details=[decoded['details'][r[2]] for r in rows if r[2] in decoded['details']]
    if len(details)!=len(rows):raise ValueError('Native detail bar missing from marked frames')
    result.update({'units':'guest milliseconds; internal counters have 0.1 ms resolution; profiling overhead remains',
                   'profiled_frames':len(rows),'frame_interval_ms':stats([r[16]/10 for r in rows]),
                   'preparation_ms':stats([r[6]/10 for r in rows]),'ui_ms':stats([r[12]/10 for r in rows]),
                   'animation_ms':stats([r[5]/10 for r in rows]),'actor_submission_ms':stats([r[9]/10 for r in rows]),
                   'explicit_pose_updates':sum(r[23]>>8&7 for r in rows),
                   'recovery_frames':sum(bool(r[30]&8192) for r in rows),
                   'missing_terrain_frames':sum(bool(r[30]&16384) for r in rows),
                   'loading_frames':sum(bool(r[30]&32768) for r in rows),
                   'terrain_build_stages':sum(r[23]&255 for r in rows),
                   'details':{name:stats([d[i]/10 for d in details]) for i,name in enumerate(
                       ['selection_ms','generation_ms','publication_ms','uv_material_ms','native_masks_ms'],2)}})
    result['details'].update({'allocations':sum(d[7] for d in details),
                              'gc_collections':sum(d[8]>>12 for d in details),
                              'texture_operations':sum(d[9]&255 for d in details),
                              'shadow_page_updates':sum(d[9]>>8 for d in details),
                              'mesh_creates':sum(d[10] for d in details),
                              'mesh_updates':sum(d[11] for d in details),
                              'mesh_releases':sum(d[12] for d in details)})
print(json.dumps(result,indent=2))
