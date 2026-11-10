"""Native texture streaming and model/interior lifecycle on a copied residency fixture.

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
            # A floor-only image can pass the generic room check. Verify the
            # stable north wall too, before any camera turn or debugging menu.
            pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/'asset-interior-first.png'),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
            wall=sum(pixels[(y*800+x)*3]>100 and pixels[(y*800+x)*3+1]>90 for y in range(65,120) for x in range(220,280))
            if wall<1500:raise AssertionError('Interior north wall was not submitted')
            # Preserve the immediate soft view, then compare steady sharp views.
            time.sleep(10);building_view('asset-interior-sharp.png',True)

            os.write(controller,b'SET C .9 .5\n');time.sleep(2);os.write(controller,b'SET C .5 .5\n');time.sleep(1);building_view('asset-interior-rotated.png',True)
            chord('A');press('A');time.sleep(5);building_view('asset-exit-cold.png',False)
            # A real approach prefetches occupants and walls, without another
            # developer prewarm. Start one metre outside the exit socket.
            os.write(controller,b'SET MAIN .5 .8\n');time.sleep(.2);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(4);screenshot('asset-approach-second.png')
            press('A');time.sleep(5);building_view('asset-interior-second.png',True)
            # Keep this camera fixed for the repeated view / texture check.
            press('L');time.sleep(1);screenshot('asset-interior-second-reset.png')
            if '--reuse-ab' in sys.argv:
                def toggle(row,name):
                    press('START');press('R')
                    for _ in range(31-row if row>15 else row):press('D_UP' if row>15 else 'D_DOWN')
                    press('A');press('B');time.sleep(3);building_view(name,True)
                toggle(29,'texture-lists-off.png')
                toggle(28,'texture-placement-off.png')
                toggle(30,'texture-poses-off.png')
                toggle(30,'texture-poses-on.png')
                toggle(28,'texture-placement-on.png')
                toggle(29,'texture-reuse-on.png')
            # SDK main/ARAM totals, after encoded profiling markers are captured.
            press('START');press('R')
            for _ in range(int(sys.argv[sys.argv.index('--testing-rows')+1]) - 21 if '--testing-rows' in sys.argv else 10):press('D_UP')
            press('A');press('B');time.sleep(2);screenshot('asset-sdk-interior-warm.png')
            chord('A');press('A');time.sleep(5);building_view('asset-sdk-outside-cold.png',False)
            if p.poll() is not None:raise AssertionError('Emulator stopped during model residency')
            unchanged=before==hashlib.sha256(card.read_bytes()).hexdigest()
            if not unchanged:raise AssertionError('Asset residency wrote to the memory card')
            if '--texture-walk' in sys.argv:
                # SDK totals obscure the lower encoded telemetry strip.
                press('START');press('R')
                for _ in range(10):press('D_UP')
                press('A');press('B');time.sleep(2)
                screenshot('texture-walk-start.png')
                for n,(x,y) in enumerate(((.9,.5),(.1,.5),(.5,.1),(.5,.9))):
                    os.write(controller,f'SET MAIN {x} {y}\nPRESS B\n'.encode());time.sleep(6)
                    os.write(controller,b'SET MAIN .5 .5\nRELEASE B\n');time.sleep(2);building_view(f'texture-walk-{n}.png',False)
                if before!=hashlib.sha256(card.read_bytes()).hexdigest():raise AssertionError('Walking or texture quality changes saved automatically')
            if '--texture-manual-save' in sys.argv:
                previous=committed_card_sequence(card);save_menu()
                for _ in range(180):
                    time.sleep(1)
                    if committed_card_sequence(card)>previous:break
                    if p.poll() is not None:raise AssertionError('Emulator stopped during explicit save')
                else:raise AssertionError('Explicit save did not commit after streaming')
                screenshot('texture-manual-save.png');saved=hashlib.sha256(card.read_bytes()).hexdigest();press('B');time.sleep(2)
                if saved!=hashlib.sha256(card.read_bytes()).hexdigest():raise AssertionError('Closing Settings wrote another generation')
            (root/'asset-residency-scenario.json').write_text(json.dumps({'source_card_sha256':before,'card_unchanged_before_explicit_save':unchanged,'two_house_entry_exit_cycles':True,'six_villagers_in_one_room_fixture':True,'reuse_ab':'--reuse-ab' in sys.argv,'sustained_walk':'--texture-walk' in sys.argv,'explicit_save':'--texture-manual-save' in sys.argv},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
