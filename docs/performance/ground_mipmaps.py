"""Focused native ground mip/menu/shadow check on a copied emulator card.

Run IMAGE OUTPUT --stream-travel --season spring --dsp-hle in the Dolphin image.
The final marked travel interval retains the existing encoded capture bars.
"""
from pathlib import Path
import sys
script=Path(__file__).resolve().parents[2]/'tools/iso-smoke.py'
source=script.read_text();sys.path.insert(0,str(script.parent))
a=source.index("        elif '--stream-travel' in sys.argv:");b=source.index("        elif '--movement-profile' in sys.argv:",a)
block=r'''        elif '--stream-travel' in sys.argv:
            import hashlib
            card=root/'MemoryCardA.USA.raw';before=hashlib.sha256(card.read_bytes()).hexdigest()
            press('A');time.sleep(5)
            def chord(button):
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press(button);os.write(controller,b'RELEASE L\nRELEASE R\n')
            def option(row):
                press('START');press('R')
                for _ in range(24-row):press('D_UP')
            chord('D_RIGHT');chord('START');time.sleep(4);screenshot('mip-auto-world.png')
            option(22);screenshot('mip-default-on-menu.png');press('A');screenshot('mip-off-menu.png');press('B');time.sleep(2);screenshot('mip-off-world.png')
            option(22);press('A');press('D_DOWN');screenshot('mip-default-auto-menu.png');press('A');screenshot('mip-one-menu.png');press('B');time.sleep(2);screenshot('mip-one-world.png')
            option(23);os.write(controller,b'SET MAIN 1 .5\n');time.sleep(5);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(1);screenshot('mip-ten-menu.png');press('B');time.sleep(2);screenshot('mip-ten-world.png')
            option(23);press('Z');screenshot('mip-reset-auto-menu.png');press('B');time.sleep(2);screenshot('mip-reset-auto-world.png')
            # L/R remain tab controls, separate from the numeric D-pad/stick.
            option(23);press('L');screenshot('mip-shoulder-settings.png');press('B')
            chord('Z');time.sleep(1);screenshot('mip-travel-start.png')
            os.write(controller,b'SET MAIN 1 .5\n');time.sleep(8);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(1);screenshot('mip-travel-end.png')
            if p.poll() is not None:raise AssertionError('Emulator stopped during mip controls')
            unchanged=before==hashlib.sha256(card.read_bytes()).hexdigest()
            if not unchanged:raise AssertionError('Ground mip controls wrote to the memory card')
            (root/'ground-mip-scenario.json').write_text(json.dumps({'source_card_sha256':before,'card_unchanged':unchanged,'debug_menu_only':True},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
