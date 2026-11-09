"""Run the marked travel route, then inspect shadows on generated terrain.

Accepts guest_travel.py arguments (including --no-overlay / --save-after-travel).
Use --rtc-year 2028 with --season winter when the saved town is from 2027.
The additional inspector teleport and camera movement occur AFTER the route
markers, so they cannot affect the ordinary-travel performance measurements.
Uses the saved/generated town, never the developer gallery.
"""
from pathlib import Path
import sys

path=Path(__file__).with_name('guest_travel.py')
source=path.read_text()
needle="exec(compile(source[:a]+block+source[b:],str(script),'exec'),"
if source.count(needle)!=1:raise RuntimeError('Guest travel wrapper changed')
extra='''
if '--rtc-year' in sys.argv:
    year=int(sys.argv[sys.argv.index('--rtc-year')+1])
    if not 2000<=year<=2099:raise ValueError('RTC year must be 2000..2099')
    source=source.replace('datetime(2027,month,15,12',f'datetime({year},month,15,12')
block+='''+repr('''            def terrain_inspector():
                os.write(controller,b'PRESS L\\nPRESS R\\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\\nRELEASE R\\n');guest_wait(2)
            def terrain_walk(x,y,seconds,name):
                os.write(controller,f'SET MAIN {x} {y}\\n'.encode());guest_wait(seconds);os.write(controller,b'SET MAIN .5 .5\\n');guest_wait(1);screenshot(name)
            terrain_inspector();screenshot('projection-ramp-base.png')
            terrain_walk(0,.5,1.8,'projection-lower-corner.png')
            terrain_walk(.5,1,1.5,'projection-lower-approach.png')
            terrain_inspector()
            terrain_walk(.5,1,1.7,'projection-ramp-top.png')
            terrain_walk(1,.5,2.1,'projection-upper-corner.png')
            terrain_walk(.5,0,1.5,'projection-upper-approach.png')
''')+'''
'''
source=source.replace(needle,extra+needle)
exec(compile(source,str(path),'exec'),{'__name__':'__main__','__file__':str(path)})
