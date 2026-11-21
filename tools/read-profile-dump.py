"""Read an explicitly exported counter-only capture from Dolphin/USB Gecko logs.

The last 256 frames have exact interval samples. The all-frame log2 histogram
retains hitch counts after ring overwrite, with explicit quantization bounds.
"""
import argparse
import json
import math
from pathlib import Path
import re
from profile_wire import crc, memory_report

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('log')
parser.add_argument('--platform',choices=('dolphin','hardware','unknown'),default='unknown')
parser.add_argument('--storage',default='unspecified')
parser.add_argument('--video-mode',default='unspecified')
parser.add_argument('--metadata',help='JSON provenance including artifact/fixture hashes and debug configuration')
args=parser.parse_args()
sessions={};invalid=0
for line in Path(args.log).read_text(errors='replace').splitlines():
    match=re.search(r'ANIMALTOWN PROFILE (BEGIN|FRAME|MEMORY|HISTOGRAM|PEAKS|EVENTS|DROPPED_EVENTS|END) (.*)',line)
    if not match:continue
    kind,body=match.groups();fields=body.split()
    try:
        if kind=='BEGIN':
            schema=int(fields[0]);session=int(fields[1],16);total=int(fields[2]);overwritten=int(fields[3])
            if schema!=1:raise ValueError('Unknown schema')
            sessions[session]={'session':session,'total_frames':total,'overwritten_frames':overwritten,'frames':[],'complete':False}
            continue
        session=int(fields[0],16)
        if session not in sessions:raise ValueError('Missing session header')
        current=sessions[session]
        if kind=='END':current['complete']=True;continue
        if kind=='DROPPED_EVENTS':current['dropped_events']=int(fields[1]);continue
        words=[int(value,16) for value in fields[1:-1]]
        halves=[half for word in words for half in (word&65535,word>>16)]
        if crc(halves)!=int(fields[-1],16):raise ValueError('CRC mismatch')
        if kind=='FRAME':
            if len(words)!=16:raise ValueError('Bad frame length')
            current['frames'].append(words)
        elif kind=='MEMORY':
            if len(words)!=32:raise ValueError('Bad memory length')
            current['memory']=memory_report(words)
        elif kind=='HISTOGRAM':
            if len(words)!=32:raise ValueError('Bad histogram length')
            current['all_frames_interval_histogram']=[{'minimum_us':0 if i==0 else 1<<i,'maximum_us':(1<<(i+1))-1,'frames':value} for i,value in enumerate(words)]
        elif kind=='PEAKS':
            if len(words)!=16:raise ValueError('Bad phase peak length')
            current['all_frames_phase_peak_ms']=[value/10 for value in words]
        elif kind=='EVENTS':
            if len(words)%4 or len(words)>256:raise ValueError('Bad event length')
            current['checkpoints']=[dict(zip(('sequence','stage','clock_low32_us','mem1_used_bytes'),words[i:i+4])) for i in range(0,len(words),4)]
    except (ValueError,IndexError):invalid+=1

def stats(values):
    values=sorted(values)
    return {name:values[min(len(values)-1,math.ceil(q*len(values))-1)] for name,q in (('p50',.5),('p95',.95),('p99',.99),('max',1))}

for current in sessions.values():
    frames=current.pop('frames');current['retained_frames']=len(frames)
    current['missing_retained_frames']=max(0,min(256,current['total_frames'])-len(frames))
    if frames:
        current['first_sequence']=frames[0][0];current['last_sequence']=frames[-1][0]
        current['retained_frame_interval_ms']=stats([frame[1]/1000 for frame in frames])
        current['retained_missing_terrain_frames']=sum(bool(frame[13]>>16&16384) for frame in frames)
        current['retained_loading_frames']=sum(bool(frame[13]>>16&32768) for frame in frames)
if not sessions:raise RuntimeError('No profiling exports found')
print(json.dumps({'schema':1,'platform':args.platform,'storage':args.storage,'video_mode':args.video_mode,
                  'provenance':json.loads(Path(args.metadata).read_text()) if args.metadata else {},
                  'invalid_records':invalid,'sessions':list(sessions.values()),
                  'units':'milliseconds; CPU timebase and blocking waits, not GPU execution duration',
                  'histogram_quantization':'log2 microseconds; exact percentile samples cover retained ring only'},indent=2))
