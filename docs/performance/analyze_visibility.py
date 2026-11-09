"""Verify the marked hidden-actor interval from visibility_culling.py.

Usage: analyze_visibility.py OUTPUT_DIRECTORY --capture CAPTURE.mkv
Screenshots identify the interval; checksummed native counters establish work.
"""
import argparse
import contextlib
import io
import json
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
    if len(pixels)!=800*600*3:raise ValueError('Expected an 800x600 Dolphin screenshot')
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

low=marker('hidden-coast-start.png');high=marker('hidden-coast-end.png')
if high<=low:raise ValueError('Hidden interval did not advance')
decoder=Path(__file__).resolve().parents[2]/'tools/read-frame-profile.py'
sys.argv=[str(decoder),str(args.capture)]
with contextlib.redirect_stdout(io.StringIO()):decoded=runpy.run_path(str(decoder))
rows=[r for r in decoded['groups'][0] if low<=r[2]<=high]
if len(rows)!=high-low+1:raise ValueError('Incomplete hidden interval')
updates=sum(r[23]>>8&7 for r in rows)
result={'from_frame':low,'to_frame':high,'frames':len(rows),
        'native_pose_updates':updates,'animation_phase_max_ms':max(r[5] for r in rows)/10,
        'units':'Guest 0.1 ms counters; explicit Update calls only. Source guard also covers Play/Stop/speed.'}
if updates:raise AssertionError('Hidden actors evaluated native poses')
print(json.dumps(result,indent=2))
