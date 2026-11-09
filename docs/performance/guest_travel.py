"""Run the existing no-teleport route for equal NTSC guest-time segments.

Use the same arguments as tools/iso-smoke.py, including --stream-travel.
--profile-seconds now specifies guest seconds per direction (8 in the report).
--save-after-travel also verifies an explicit complete commit and menu recovery.
--no-overlay leaves the in-game profiler off and records AVI presentation timing.
No repository file is rewritten. Run in the existing Dolphin container.
"""
from pathlib import Path
import sys
from avi_guest_clock import AviGuestClock

if '--no-overlay' in sys.argv and any(flag in sys.argv for flag in ('--freeze-textures','--recreate-terrain-meshes')):
    raise ValueError('Run UV/mesh comparison controls with the diagnostic overlay enabled')

repository=Path(__file__).resolve().parents[2]
script=repository/'tools/iso-smoke.py'
sys.path.insert(0,str(script.parent))
source=script.read_text()
a=source.index("        elif '--stream-travel' in sys.argv:")
b=source.index("        elif '--movement-profile' in sys.argv:",a)
block=source[a:b]
if '--no-overlay' in sys.argv:
    start=block.index("            os.write(controller,b'PRESS L\\nPRESS R\\n')")
    end=block.index('            time.sleep(profile_seconds)',start)
    block=block[:start]+block[end:]
    start=block.index('            def marker(name):')
    end=block.index("            previous=marker('travel-start.png')",start)
    block=block[:start]+"            def marker(name):return guest_clock.frames()\n"+block[end:]
insert="""            import math
            guest_clock=AviGuestClock(root/'user/Dump/Frames',track_presentations=True)
            def guest_wait(seconds):
                start=guest_clock.frames();target=start+math.ceil(seconds*60000/1001)
                deadline=time.monotonic()+seconds*12+30
                while guest_clock.frames()<target:
                    if p.poll() is not None:raise AssertionError('Emulator stopped during guest-time route')
                    if time.monotonic()>deadline:raise AssertionError('Guest AVI clock stopped advancing')
                    time.sleep(.1)
                return (guest_clock.frames()-start)*1001/60000
"""
changes={
    "            previous=marker('travel-start.png')":insert+"            previous=marker('travel-start.png')",
    'time.sleep(profile_seconds*2)':'actual_guest_seconds=guest_wait(profile_seconds)',
    "time.sleep(2);screenshot(f'travel-{n}.png')":"guest_wait(1);screenshot(f'travel-{n}.png')",
    "'seconds_per_direction':profile_seconds*2":"'guest_seconds_per_direction':profile_seconds,'actual_guest_seconds':[]",
    "travel_report['frame_counters'].append(current)":"travel_report['frame_counters'].append(current);travel_report['actual_guest_seconds'].append(actual_guest_seconds)"}
for old,new in changes.items():
    if block.count(old)!=1:raise RuntimeError('Travel scenario changed: '+old)
    block=block.replace(old,new)
block=block.replace("'frame_counters':[previous]","'frame_counters':[previous],'overlay_enabled':'--no-overlay' not in sys.argv,'vi_markers':[guest_clock.frames()]")
block=block.replace("travel_report['frame_counters'].append(current)","travel_report['vi_markers'].append(guest_clock.frames());travel_report['presented_vi_indices']=guest_clock.presentations;travel_report['frame_counters'].append(current)")
if '--save-after-travel' in sys.argv:
    block+='''            before_save=committed_card_sequence(root/'MemoryCardA.USA.raw')
            building_save()
            for sample in range(30):
                after_save=committed_card_sequence(root/'MemoryCardA.USA.raw')
                if after_save>before_save:break
                if p.poll() is not None:raise AssertionError('Emulator stopped before the saved card flushed')
                time.sleep(1)
            else:raise AssertionError('Post-travel save did not flush a complete checksummed generation')
            screenshot('travel-save-returned.png')
            (root/'travel-save.json').write_text(json.dumps({'before':before_save,'after':after_save,'returned_to_settings':True},indent=2))
'''
exec(compile(source[:a]+block+source[b:],str(script),'exec'),{'__name__':'__main__','__file__':str(script),'AviGuestClock':AviGuestClock})
