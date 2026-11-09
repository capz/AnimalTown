"""Opt-in weather regression on a copied saved town; uses only debug-menu toggles.

Run in the Dolphin image with --stream-travel --season spring --dsp-hle.
The existing inspector cameras show the actual coast, mouth and river.
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
            def options(row,name,toggle=False):
                press('START');press('R')
                for _ in range(18-row):press('D_UP')
                screenshot(name+'-before.png')
                if toggle:press('A');screenshot(name+'-after.png')
                press('B')
            options(16,'weather-defaults')
            if '--defaults-only' not in sys.argv:
                chord('Z')
                # Arm rough mode while dry: the ocean must remain calm.
                options(17,'weather-rough-armed',True)
                for _ in range(4):chord('D_LEFT')
                time.sleep(3);screenshot('weather-dry-start.png');time.sleep(5);screenshot('weather-dry-end.png')
                options(16,'weather-rain-enabled',True)
                time.sleep(3);screenshot('weather-storm-start.png');time.sleep(5);screenshot('weather-storm-end.png')
                options(17,'weather-rough-disabled',True)
                time.sleep(3);screenshot('weather-rain-calm.png')
                options(17,'weather-rough-enabled',True)
                # Coast mode four -> mouth five: inspect the shared plane.
                chord('D_LEFT');time.sleep(3);screenshot('weather-mouth-level.png')
                # Mouth five -> normal camera zero -> inland river one.
                chord('D_LEFT');chord('D_LEFT');time.sleep(3);screenshot('weather-inland-rain.png')
                options(16,'weather-rain-disabled',True)
                for _ in range(3):chord('D_LEFT')
                time.sleep(3);screenshot('weather-calmed-after-rain.png')
                options(16,'weather-rain-restored',True)
                press('B');chord('A');time.sleep(3);screenshot('weather-house-exterior.png')
                press('A');time.sleep(4);building_view('weather-interior.png',True)
                time.sleep(3);building_view('weather-interior-later.png',True)
                press('L');press('A');time.sleep(5);screenshot('weather-house-return.png')
            if p.poll() is not None:raise AssertionError('Emulator stopped during weather preview')
            unchanged=hashlib.sha256(card.read_bytes()).hexdigest()==original
            if not unchanged:raise AssertionError('Debug weather modified the saved card')
            (root/'weather-scenario.json').write_text(json.dumps({'source_card_sha256':original,'card_unchanged':unchanged,'defaults_only':'--defaults-only' in sys.argv,'rain_toggles_via_debug_menu_only':True},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
