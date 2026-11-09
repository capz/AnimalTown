"""Native SDK panel check, using the actual Debug / testing menu.

Use --stream-travel to select this scenario. It intentionally skips movie dumping
and the game's encoded frame profiler, retaining focused screenshots instead.
"""
from pathlib import Path
import json
import subprocess
import sys

def analyze(root):
    """Check the opaque SDK panel in the fixed 800x600 smoke viewport."""
    def rgb(stem):
        return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/('sdk-overlay-'+stem+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    def region(pixels,box):
        left,top,right,bottom=box
        return b''.join(pixels[(y*800+left)*3:(y*800+right)*3] for y in range(top,bottom))
    frames={stem:rgb(stem) for stem in ('default-off','menu-off','menu-on','world-on-a','world-on-b','menu-restored','world-off','frame-capture-off')}
    counts={}
    for stem,pixels in frames.items():
        panel=region(pixels,(35,398,238,472))
        count=sum(max(panel[i:i+3])<20 for i in range(0,len(panel),3));counts[stem]=count
        expected=stem in ('menu-on','world-on-a','world-on-b')
        if (count>10000)!=expected:raise AssertionError('Unexpected SDK panel visibility: '+stem)
    label=(90,202,350,224)
    if region(frames['menu-off'],label)!=region(frames['menu-restored'],label):raise AssertionError('Off label was not restored')
    if region(frames['menu-off'],label)==region(frames['menu-on'],label):raise AssertionError('On label did not change')
    result=json.loads((root/'sdk-overlay-scenario.json').read_text())
    result.update(panel_black_pixels=counts,menu_label_off_on_off_verified=True)
    (root/'sdk-overlay-analysis.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))

if len(sys.argv)>1 and sys.argv[1]=='--analyze-only':
    analyze(Path(sys.argv[2]));sys.exit(0)

script=Path(__file__).resolve().parents[2]/'tools/iso-smoke.py'
source=script.read_text();sys.path.insert(0,str(script.parent))
source=source.replace(" or '--stream-travel' in sys.argv",'')
a=source.index("        elif '--stream-travel' in sys.argv:");b=source.index("        elif '--movement-profile' in sys.argv:",a)
block=r'''        elif '--stream-travel' in sys.argv:
            import hashlib
            card=root/'MemoryCardA.USA.raw';before=hashlib.sha256(card.read_bytes()).hexdigest()
            press('A');time.sleep(5);screenshot('sdk-overlay-default-off.png')
            def option():
                press('START');press('R');press('D_UP')
            option();screenshot('sdk-overlay-menu-off.png');press('A');time.sleep(2);screenshot('sdk-overlay-menu-on.png');press('B');time.sleep(4);screenshot('sdk-overlay-world-on-a.png')
            os.write(controller,b'SET MAIN 1 .5\n');time.sleep(2);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(2);screenshot('sdk-overlay-world-on-b.png')
            option();press('A');screenshot('sdk-overlay-menu-restored.png');press('B');time.sleep(3);screenshot('sdk-overlay-world-off.png')
            # Row zero remains a separate, unchanged frame-capture control.
            press('START');press('R');screenshot('sdk-overlay-frame-capture-off.png');press('B')
            if p.poll() is not None:raise AssertionError('Emulator stopped during SDK overlay check')
            unchanged=before==hashlib.sha256(card.read_bytes()).hexdigest()
            if not unchanged:raise AssertionError('SDK performance toggle wrote to the card')
            (root/'sdk-overlay-scenario.json').write_text(json.dumps({'source_card_sha256':before,'card_unchanged':unchanged,'sdk_toggle_via_debug_menu':True},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
analyze(Path(sys.argv[2]))
