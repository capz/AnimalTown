"""Native visibility/backface check using the saved town and existing inspectors.

Run with --stream-travel to enable lossless telemetry recording. This focused
scenario replaces that route with visible/hidden/reappearing actor views, an
actual diagonal cliff, a cottage and all four interior camera directions.
"""
from pathlib import Path
import sys

script=Path(__file__).resolve().parents[2]/'tools/iso-smoke.py'
source=script.read_text();sys.path.insert(0,str(script.parent))
a=source.index("        elif '--stream-travel' in sys.argv:");b=source.index("        elif '--movement-profile' in sys.argv:",a)
block='''        elif '--stream-travel' in sys.argv:
            press('A');time.sleep(5)
            def chord(button):
                os.write(controller,b'PRESS L\\nPRESS R\\n');time.sleep(.2);press(button);os.write(controller,b'RELEASE L\\nRELEASE R\\n')
            chord('Z')
            os.write(controller,b'SET MAIN 1 .5\\n');time.sleep(2);screenshot('visible-moving.png')
            chord('D_LEFT');os.write(controller,b'SET MAIN .5 .5\\n')
            for mode in range(1,4):chord('D_LEFT')
            time.sleep(5);screenshot('hidden-coast-start.png');time.sleep(5);screenshot('hidden-coast-end.png')
            press('B');time.sleep(2);screenshot('visible-returned.png')
            chord('START');time.sleep(3);screenshot('backface-cliff-base.png')
            os.write(controller,b'SET MAIN .5 1\\n');time.sleep(2);os.write(controller,b'SET MAIN .5 .5\\n');time.sleep(2);screenshot('backface-cliff-top.png')
            chord('A');time.sleep(3);screenshot('backface-house.png')
            press('A');time.sleep(4);building_view('backface-room-0.png',True)
            for view in range(1,5):
                os.write(controller,b'SET C .9 .5\\n');time.sleep(2);os.write(controller,b'SET C .5 .5\\n');time.sleep(1);screenshot(f'backface-room-{view}.png')
            press('L');press('A');time.sleep(5);building_view('backface-house-return.png',False)
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script)})
