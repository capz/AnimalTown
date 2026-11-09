"""Focused world/season/default-cache check, or indoor rig/bubble/rim fixture.

Run with --stream-travel --season spring --dsp-hle. Use --indoor-fixture with
a disposable prepared card for stable six-species billboard/shader inspection.
"""
from pathlib import Path
import sys
script=Path(__file__).resolve().parents[2]/'tools/iso-smoke.py'
source=script.read_text();sys.path.insert(0,str(script.parent))
a=source.index("        elif '--stream-travel' in sys.argv:");b=source.index("        elif '--movement-profile' in sys.argv:",a)
block=r'''        elif '--stream-travel' in sys.argv:
            import hashlib
            card=root/'MemoryCardA.USA.raw';original=hashlib.sha256(card.read_bytes()).hexdigest()
            press('A');time.sleep(5)
            def chord(button):
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press(button);os.write(controller,b'RELEASE L\nRELEASE R\n')
            def focus(row):
                press('START');press('R')
                for _ in range(21-row):press('D_UP')
            def ready(row,name):
                y=131+(row%9)*24
                for _ in range(90):
                    screenshot(name)
                    data=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
                    hits=sum(all(abs(data[(py*800+px)*3+c]-v)<=3 for c,v in enumerate((221,231,188))) for py in range(y,y+21) for px in range(100,540,4))
                    if hits>100:return
                    if p.poll() is not None:raise AssertionError('Emulator stopped in debug controls')
                    time.sleep(1)
                raise AssertionError('Debug menu did not recover after season loading')
            chord('Z')
            if '--indoor-fixture' in sys.argv:
                building_view('attention-default-off.png',True)
                focus(19);ready(19,'attention-option-off.png');press('A');ready(19,'attention-option-on.png');press('B');time.sleep(2);screenshot('attention-on-0.png')
                for view in range(1,4):
                    os.write(controller,b'SET C .9 .5\n');time.sleep(2);os.write(controller,b'SET C .5 .5\n');time.sleep(1);screenshot(f'attention-on-{view}.png')
                press('L');time.sleep(2)
                focus(20);ready(20,'rim-default-100-menu.png')
                os.write(controller,b'SET MAIN 0 .5\n');time.sleep(8);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(1);ready(20,'rim-zero-menu.png');press('B');time.sleep(2);screenshot('rim-zero-world.png')
                focus(20);os.write(controller,b'SET MAIN 1 .5\n');time.sleep(16);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(1);ready(20,'rim-200-menu.png');press('B');time.sleep(2);screenshot('rim-200-world.png')
                focus(20);press('Z');ready(20,'rim-reset-100-menu.png');press('B');time.sleep(2);screenshot('rim-reset-100-world.png')
                focus(19);press('A');ready(19,'attention-option-restored.png');press('B');time.sleep(2);building_view('attention-restored-off.png',True)
            else:
                chord('START');time.sleep(3);screenshot('curve-default-ramp.png')
                for name in ('spring','summer','fall','winter','auto'):
                    focus(18);ready(18,'season-'+name+'-before.png');press('A');ready(18,'season-'+name+'-menu.png');press('B');time.sleep(2);screenshot('season-'+name+'.png')
                screenshot('ground-fast-start.png')
                os.write(controller,b'SET MAIN 1 .5\n');time.sleep(8);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(1);screenshot('ground-fast-end.png')
            if p.poll() is not None:raise AssertionError('Emulator stopped during rendering preview')
            unchanged=hashlib.sha256(card.read_bytes()).hexdigest()==original
            if not unchanged:raise AssertionError('Rendering previews wrote to the memory card')
            (root/'world-debug-scenario.json').write_text(json.dumps({'source_card_sha256':original,'card_unchanged':unchanged,'indoor_fixture':'--indoor-fixture' in sys.argv,'debug_menu_only':True},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
