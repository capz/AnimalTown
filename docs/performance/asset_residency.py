"""Native logo/cold-villager/interior lifecycle on a copied residency fixture.

Run IMAGE OUTPUT --stream-travel --season spring --dsp-hle. Prepare the copied
card with world-debug-fixture/Fixture.csproj -- OUTPUT --residency first.
"""
from pathlib import Path
import sys
script=Path(__file__).resolve().parents[2]/'tools/iso-smoke.py'
source=script.read_text().replace('else:time.sleep(45)','else:time.sleep(60)');sys.path.insert(0,str(script.parent))
a=source.index("        elif '--stream-travel' in sys.argv:");b=source.index("        elif '--movement-profile' in sys.argv:",a)
block=r'''        elif '--stream-travel' in sys.argv:
            import hashlib
            card=root/'MemoryCardA.USA.raw';before=hashlib.sha256(card.read_bytes()).hexdigest()
            def chord(button):
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press(button);os.write(controller,b'RELEASE L\nRELEASE R\n')
            chord('Z');time.sleep(1);screenshot('asset-title-logo.png')
            press('A');time.sleep(5);building_view('asset-outside-cold.png',False)
            screenshot('asset-restore-start.png');chord('A');time.sleep(4);screenshot('asset-restore-end.png');building_view('asset-prefetched-porch.png',False)
            press('A');time.sleep(5);building_view('asset-interior-first.png',True)
            os.write(controller,b'SET C .9 .5\n');time.sleep(2);os.write(controller,b'SET C .5 .5\n');time.sleep(1);building_view('asset-interior-rotated.png',True)
            chord('A');press('A');time.sleep(5);building_view('asset-exit-cold.png',False)
            # A real approach prefetches occupants and walls, without another
            # developer prewarm. Start one metre outside the exit socket.
            os.write(controller,b'SET MAIN .5 .8\n');time.sleep(.2);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(4);screenshot('asset-approach-second.png')
            press('A');time.sleep(5);building_view('asset-interior-second.png',True)
            # Keep this camera fixed for the repeated view / texture check.
            press('L');time.sleep(1);screenshot('asset-interior-second-reset.png')
            # SDK main/ARAM totals, after encoded profiling markers are captured.
            press('START');press('R')
            for _ in range(3):press('D_UP')
            press('A');press('B');time.sleep(2);screenshot('asset-sdk-interior-warm.png')
            chord('A');press('A');time.sleep(5);building_view('asset-sdk-outside-cold.png',False)
            if p.poll() is not None:raise AssertionError('Emulator stopped during model residency')
            unchanged=before==hashlib.sha256(card.read_bytes()).hexdigest()
            if not unchanged:raise AssertionError('Asset residency wrote to the memory card')
            (root/'asset-residency-scenario.json').write_text(json.dumps({'source_card_sha256':before,'card_unchanged':unchanged,'two_house_entry_exit_cycles':True,'six_villagers_in_one_room_fixture':True},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
