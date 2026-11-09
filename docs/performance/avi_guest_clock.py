"""Incremental clock for the NTSC Dolphin AVI travel capture.

Count 00dc/00db chunks, including zero-size repeated VI frames. Packet decoders
omit those empty chunks; nb_read_packets is therefore not a guest-time clock.
The reader follows only complete chunks and retains its offset while recording.
"""
from pathlib import Path
import struct
class AviGuestClock:
    def __init__(self,root,track_presentations=False):
        self.root=Path(root);self.state={};self.track_presentations=track_presentations;self.presentations=[]
    def frames(self):
        total=0
        for path in sorted(self.root.glob('*.avi')):
            offset,count=self.state.get(path,(None,0));size=path.stat().st_size
            with path.open('rb') as f:
                if offset is None:
                    offset=12
                    while offset+12<=size:
                        f.seek(offset);tag,length,kind=struct.unpack('<4sI4s',f.read(12))
                        if tag==b'LIST' and kind==b'movi':offset+=12;break
                        offset+=8+length+(length&1)
                    else:continue
                while offset+8<=size:
                    f.seek(offset);tag,length=struct.unpack('<4sI',f.read(8));end=offset+8+length+(length&1)
                    if end>size:break
                    if tag in (b'00dc',b'00db'):
                        count+=1
                        if length and self.track_presentations:self.presentations.append(total+count)
                    if tag==b'LIST':
                        kind=f.read(4)
                        if kind==b'rec ':offset+=12;continue
                    offset=end
                self.state[path]=(offset,count);total+=count
        return total
if __name__=='__main__':
 import sys
 print(AviGuestClock(sys.argv[1]).frames())
