"""Decode the dry/rough-rain intervals from debug_weather.py's native capture.

Usage: analyze_debug_weather.py OUTPUT_DIRECTORY --capture CAPTURE.mkv
"""
import argparse
import contextlib
import io
import json
import math
from pathlib import Path
import runpy
import subprocess
import sys

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory',type=Path)
parser.add_argument('--capture',type=Path,required=True)
args=parser.parse_args()

def marker(name):
    pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(args.directory/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    if len(pixels)!=800*600*3:raise ValueError('Expected an 800x600 screenshot')
    for origin in range(20,36):
        for width in range(570,601):
            def word(index):
                value=0
                for bit in range(16):
                    n=index*16+bit;x=origin+int((n%64+.5)*width/64);y=450+n//64*4
                    if pixels[(y*800+x)*3]>128:value|=1<<bit
                return value
            if word(0)!=0x4154:continue
            values=[word(n) for n in range(32)];checksum=0
            for value in values[:31]:checksum^=value
            if values[1]==5 and checksum==values[31]:return values[2]
    raise ValueError('No valid telemetry in '+name)

def stats(values):
    values=sorted(values)
    return {'median':values[(len(values)-1)//2],'p95':values[math.ceil(len(values)*.95)-1],'max':values[-1]}

decoder=Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py'
sys.argv=[str(decoder),str(args.capture)]
with contextlib.redirect_stdout(io.StringIO()):decoded=runpy.run_path(str(decoder))
report={'units':'Guest 0.1 ms counters; stationary coast, single instrumented captures; physical GameCube untested','intervals':{}}
for name in ('dry','storm'):
    low=marker('weather-'+name+'-start.png');high=marker('weather-'+name+'-end.png')
    rows=[r for r in decoded['groups'][0] if low<=r[2]<=high]
    if high<=low or len(rows)!=high-low+1:raise ValueError('Incomplete weather interval')
    details=[decoded['details'][r[2]] for r in rows]
    result={'from_frame':low,'to_frame':high,'frames':len(rows),
        'frame_interval_ms':stats([r[16]/10 for r in rows]),'terrain_prepare_ms':stats([r[6]/10 for r in rows]),
        'terrain_build_stages':sum(r[23]&255 for r in rows),
        'loading_missing_or_recovery_frames':sum(bool(r[30]&(8192|16384|32768)) for r in rows),
        'managed_allocations':sum(d[7] for d in details),'gc_collections':sum(d[8]>>12 for d in details),
        'texture_operations':sum(d[9]&255 for d in details),
        'mesh_creates':sum(d[10] for d in details),'mesh_updates':sum(d[11] for d in details),'mesh_releases':sum(d[12] for d in details)}
    for key in ('terrain_build_stages','loading_missing_or_recovery_frames','gc_collections','texture_operations','mesh_creates','mesh_updates','mesh_releases'):
        if result[key]:raise AssertionError(name+' steady interval unexpectedly did '+key)
    report['intervals'][name]=result
def pixels(name):
    return subprocess.check_output(['ffmpeg','-v','error','-i',str(args.directory/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
for name,row in (('rough-armed',17),('rain-enabled',16),('rough-disabled',17),('rough-enabled',17),('rain-disabled',16),('rain-restored',16)):
    before=pixels('weather-'+name+'-before.png');after=pixels('weather-'+name+'-after.png')
    y=131+(row%9)*24
    def label(data):return b''.join(data[(py*800+100)*3:(py*800+390)*3] for py in range(y,y+21))
    if label(before)==label(after):raise AssertionError('Native debug label did not toggle: '+name)
def bright_water(name):
    data=pixels(name)
    return sum(data[(y*800+x)*3]>150 and data[(y*800+x)*3+1]>200 and data[(y*800+x)*3+2]>210 for y in range(180,350) for x in range(40,600))
calm=bright_water('weather-rain-calm.png');storm=bright_water('weather-storm-start.png')
if storm<=calm*2+30:raise AssertionError('Ocean whitecaps did not appear independently of rain')
report['native_debug_labels_changed']=True
report['bright_water_pixels']={'rain_calm':calm,'rain_rough':storm}
report['scenario']=json.loads((args.directory/'weather-scenario.json').read_text())
print(json.dumps(report,indent=2))
