"""Focused outdoor streaming capture with unobscured guest telemetry.
Run IMAGE OUTPUT --stream-travel --season spring --dsp-hle.
"""
from pathlib import Path
p=Path(__file__).with_name('texture_streaming.py');program=p.read_text()
a=program.index("block=r'''");b=program.index("'''\nexec(",a)
block=r'''        elif '--stream-travel' in sys.argv:
            import hashlib
            card=root/'MemoryCardA.USA.raw';before=hashlib.sha256(card.read_bytes()).hexdigest()
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\n')
            press('A');time.sleep(5);building_view('texture-walk-start.png',False)
            for n,(x,y) in enumerate(((.9,.5),(.1,.5),(.5,.1),(.5,.9))):
                os.write(controller,f'SET MAIN {x} {y}\nPRESS B\n'.encode());time.sleep(6)
                os.write(controller,b'SET MAIN .5 .5\nRELEASE B\n');time.sleep(2);building_view(f'texture-walk-{n}.png',False)
            if before!=hashlib.sha256(card.read_bytes()).hexdigest():raise AssertionError('Walking or texture quality changes saved automatically')
            (root/'texture-travel-scenario.json').write_text(json.dumps({'source_card_sha256':before,'card_unchanged':True,'four_running_directions':True,'sdk_overlay':False},indent=2))
'''
exec(compile(program[:a]+"block=r'''"+block+program[b:],str(p),'exec'),{'__name__':'__main__','__file__':str(p)})
