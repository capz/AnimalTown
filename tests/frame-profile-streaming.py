"""Exercise checksummed telemetry and streaming gates without an emulator."""
import ast
from contextlib import redirect_stdout
from io import StringIO
import json
from pathlib import Path
import runpy
import sys
from unittest.mock import patch

ast.parse(Path('tools/iso-smoke.py').read_text())

class Capture:
    def __init__(self, flags, detail=False):
        self.flags = flags
        self.detail = detail
        self.frame = 0
        self.stdout = self

    def read(self, size):
        assert size == 640 * 480 * 3
        if self.frame == 120:
            return b''
        self.frame += 1
        words = [0] * 32
        words[0:3] = [0x4154, 5, self.frame]
        words[16] = 333
        words[30] = self.flags.get(self.frame, 0)
        for value in words[:31]:
            words[31] ^= value
        frame = bytearray(size)
        for bit in range(512):
            if words[bit // 16] & (1 << (bit % 16)):
                at = ((450 + bit // 64 * 4) * 640 + bit % 64 * 10 + 5) * 3
                frame[at:at + 3] = b'\xff\xff\xff'
        if self.detail:
            words=[0x4144,self.frame,1,2,3,4,5,17,4098,768,2,3,1,4,1,0]
            for value in words[:15]:words[15]^=value
            for bit in range(256):
                if words[bit//16] & (1 << (bit%16)):
                    at=((434+bit//64*4)*640+bit%64*10+5)*3
                    frame[at:at+3]=b'\xff\xff\xff'
        return bytes(frame)

    def wait(self):
        return 0

def decode(flags, failure=None, detail=False):
    output = StringIO()
    caught = None
    with patch.object(sys, 'argv', ['tools/read-frame-profile.py', 'capture.avi', '--check-streaming']), \
            patch('subprocess.Popen', return_value=Capture(flags, detail)), redirect_stdout(output):
        try:
            runpy.run_path('tools/read-frame-profile.py', run_name='__main__')
        except RuntimeError as error:
            caught = str(error)
    assert (caught is None) if failure is None else (caught is not None and failure in caught), caught
    return json.loads(output.getvalue())

normal = decode({60: 8192, 61: 8192, 62: 8192})
assert normal['landscape_loading_frames'] == 0
assert normal['scenarios']['world']['terrain_repair_frames'] == 3
assert normal['scenarios']['world']['missing_terrain_frames'] == 0
loading = decode({60: 32768}, 'Landscape loading interrupted travel')
assert loading['landscape_loading_frames'] == 1
missing = decode({60: 16384}, 'Visible terrain was missing during travel')
assert missing['scenarios']['world']['missing_terrain_frames'] == 1
extra=decode({},detail=True)['preparation_details']
assert extra['frames']==120 and extra['managed_allocations']['p50']==17
assert extra['gc_collections']==120 and extra['free_block_searches']==240
assert extra['texture_operations']==0 and extra['shadow_page_updates']==360
assert extra['mesh_updates']['p50']==3 and extra['generation_ms']['p50']==.2
print('Frame telemetry: normal travel, bounded repairs, loading interruption and missing-terrain rejection passed')
