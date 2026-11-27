"""Verify an incrementally written AVI, OpenDML continuation and empty repeats."""
from pathlib import Path
import struct
from tempfile import TemporaryDirectory
from avi_guest_clock import AviGuestClock

def chunk(tag,data):return tag+struct.pack('<I',len(data))+data+(b'\0' if len(data)&1 else b'')
def movie(frames):return chunk(b'LIST',b'movi'+frames)
def riff(kind,data):return chunk(b'RIFF',kind+data)
with TemporaryDirectory() as directory:
    root=Path(directory);path=root/'capture_0.avi';clock=AviGuestClock(root,track_presentations=True)
    first=riff(b'AVI ',movie(chunk(b'00dc',b'frame1')+chunk(b'00dc',b'')))
    path.write_bytes(first);assert clock.frames()==2 and clock.frames()==2 and clock.presentations==[1]
    # A recording muxer has not finalized its RIFF/LIST sizes yet. Available
    # frame chunks remain countable; a partial chunk must never advance time.
    continuation=b'RIFF'+struct.pack('<I',0xffffffff)+b'AVIX'+b'LIST'+struct.pack('<I',0xffffffff)+b'movi'
    third=chunk(b'00db',b'frame3')
    with path.open('ab') as f:f.write(continuation+third[:10])
    assert clock.frames()==2 and clock.presentations==[1]
    with path.open('ab') as f:f.write(third[10:]+chunk(b'00dc',b''))
    assert clock.frames()==4 and clock.frames()==4 and clock.presentations==[1,3]
    path.write_bytes(first+riff(b'AVIX',movie(third+chunk(b'00dc',b''))))
    assert clock.frames()==4
    second=root/'capture_1.avi';second.write_bytes(b'RIFF')
    assert clock.frames()==4
    second.write_bytes(riff(b'AVI ',movie(chunk(b'00dc',b'frame5'))))
    assert clock.frames()==5 and clock.presentations==[1,3,5]
    fresh=AviGuestClock(root,track_presentations=True);assert fresh.frames()==5 and fresh.presentations==[1,3,5]

with TemporaryDirectory() as directory:
    root=Path(directory);path=root/'capture_0.avi'
    clock=AviGuestClock(root,track_presentations=True)
    first=riff(b'AVI ',movie(chunk(b'00dc',b'frame1')+chunk(b'00dc',b'')))
    path.write_bytes(first)
    assert clock.frames()==2 and clock.presentations==[1]
    # avi_write_idx1 reserves a zero size, then writes entries before seeking
    # back to publish the size. Its entries begin with 00dc too. Reading this
    # valid in-flight prefix must neither count fake frames nor lose the cursor.
    entries=struct.pack('<4sIII',b'00dc',0x10,4,6)*2
    with path.open('ab') as f:f.write(b'idx1'+struct.pack('<I',0)+entries)
    assert clock.frames()==2 and clock.presentations==[1]
    assert clock.state[path][0]==len(first)
    with path.open('r+b') as f:
        f.seek(len(first)+4);f.write(struct.pack('<I',len(entries)))
        f.seek(0,2)
        f.write(b'RIFF'+struct.pack('<I',0xffffffff)+b'AVIX'+
                b'LIST'+struct.pack('<I',0xffffffff)+b'movi'+
                chunk(b'00dc',b'frame3')+chunk(b'00dc',b''))
    assert clock.frames()==4 and clock.presentations==[1,3]
    fresh=AviGuestClock(root,track_presentations=True)
    assert fresh.frames()==4 and fresh.presentations==[1,3]

print('Guest clock: partial writes, unfinished index publication, AVI+AVIX, empty repeats, multiple files and presentation indices passed')
