"""Direct ISO boot: no separately selected DOL. Run inside animaltown-dolphin."""
import os
from pathlib import Path
import subprocess
import sys
import time
from datetime import datetime, timezone

disc=Path(sys.argv[1]).resolve()
root=Path(sys.argv[2]).resolve();root.mkdir(parents=True,exist_ok=True)
profile_seconds=int(sys.argv[sys.argv.index('--profile-seconds')+1]) if '--profile-seconds' in sys.argv else 25
if not 5<=profile_seconds<=300:raise ValueError('Profile duration must be 5..300 seconds per stationary scenario')
user=root/'user';(user/'Config').mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(Path('/work/vendor/sdk/scripts')))
from dolphin_controller_test import pad_config
(user/'Config'/'GCPadNew.ini').write_text(pad_config())
(user/'Pipes').mkdir(exist_ok=True)
for i in range(4):
    pipe=user/'Pipes'/f'dnd{i}'
    if pipe.exists():pipe.unlink()
    os.mkfifo(pipe)
controller=os.open(user/'Pipes'/'dnd0',os.O_RDWR|os.O_NONBLOCK)
def press(button):
    hold=2 if '--slow-input' in sys.argv or '--profile' in sys.argv else 1
    os.write(controller,f'PRESS {button}\n'.encode());time.sleep(hold)
    os.write(controller,f'RELEASE {button}\n'.encode());time.sleep(hold)
def screenshot(name):
    subprocess.run(['ffmpeg','-v','error','-f','x11grab','-video_size','800x600','-i',':93','-frames:v','1','-y',str(root/name)],env=env,check=True)

def building_view(name, indoors):
    screenshot(name)
    pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    target=(32,40,39) if indoors else (149,198,186)
    matches=sum(all(abs(pixels[(y*800+x)*3+c]-value)<=3 for c,value in enumerate(target))
                for y in range(80,300,4) for x in range(40,600,4))
    if p.poll() is not None or matches<500:
        raise AssertionError(f'Building scenario did not reach the expected {"interior" if indoors else "town"} view: {name}; matching background samples={matches}')

def building_save():
    press('START');screenshot('building-saving.png')
    # First-use card allocation and transactional readback take longer than a
    # frame. Do not send exit input until the settings panel is actually drawn.
    for sample in range(45):
        screenshot('building-save-indoor.png')
        pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/'building-save-indoor.png'),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
        paper=sum(all(abs(pixels[(y*800+x)*3+c]-value)<=3 for c,value in enumerate((255,245,221)))
                  for y in range(200,240,2) for x in range(530,550,2))
        if paper>150:
            print(f'Indoor save returned to settings after sample {sample}',flush=True);return
        if p.poll() is not None:raise RuntimeError('Game exited while saving indoors')
        time.sleep(2)
    raise AssertionError('Indoor save did not return to settings within the bounded wait')

def dialogue_cycles():
    import json
    def pixels(name):
        screenshot(name+'.png')
        return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    def body(frame):return b''.join(frame[(row*800+78)*3:(row*800+540)*3] for row in range(344,414))
    def returned(frame):
        return sum(all(abs(frame[(row*800+col)*3+c]-target)<=3 for c,target in enumerate((221,231,188))) for row in range(160,181) for col in range(100,540,4))>100
    results=[]
    press('A');press('X');press('L');press('D_DOWN')
    press('A');press('A');reference=body(pixels('dialogue-skipped-reference'));time.sleep(3)
    press('B')
    if not returned(pixels('dialogue-skipped-return')):raise AssertionError('Dialogue could not return after skip-to-end')
    for cycle in range(3):
        press('A');complete=False
        for sample in range(40):
            time.sleep(1)
            if p.poll() is not None:raise RuntimeError('Game exited during dialogue')
            if body(pixels(f'dialogue-natural-{cycle}'))==reference:complete=True;break
        results.append({'cycle':cycle,'completed':complete})
        (root/'dialogue-cycles.json').write_text(json.dumps(results,indent=2)+'\n')
        print(f'Dialogue {cycle} naturally completed: {complete}',flush=True)
        if not complete:raise AssertionError('Dialogue stopped before matching the complete text')
        time.sleep(2);press('B');responsive=returned(pixels(f'dialogue-return-{cycle}'))
        results[-1]['returnedToSettings']=responsive
        (root/'dialogue-cycles.json').write_text(json.dumps(results,indent=2)+'\n')
        if not responsive:raise AssertionError('Game stopped responding after dialogue completion')
    (root/'dialogue-cycles.json').write_text(json.dumps(results,indent=2)+'\n')
    press('START');time.sleep(12);screenshot('dialogue-cycles-save.png')

def item_navigation():
    results=[]
    def read(name):
        screenshot(name+'.png')
        pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
        scores=[sum(all(abs(pixels[(y*800+x)*3+c]-target)<=3 for c,target in enumerate((221,231,188))) for y in range(187+i*18,204+i*18) for x in range(200,330,2)) for i in range(10)]
        return scores.index(max(scores)) if max(scores)>30 else -1
    def focus(name,expected):
        actual=read(name);results.append((name,actual==expected));print(f'{name}: expected {expected}, actual {actual}',flush=True)
    press('A');focus('actions-initial',0)
    press('D_DOWN');focus('actions-dpad-down',1)
    press('D_UP');focus('actions-dpad-up',0)
    # Dolphin allows one physical key to drive both mappings. Send both in the
    # same update, then release both, and require a visible navigation response.
    os.write(controller,b'PRESS D_DOWN\nSET MAIN 0.5 0.1\n');time.sleep(.5)
    os.write(controller,b'RELEASE D_DOWN\nSET MAIN 0.5 0.5\n');time.sleep(2)
    paired=read('actions-paired-input');results.append(('paired-input',paired in range(1,9)));print(f'paired input row: {paired}',flush=True)
    press('B');press('A');focus('actions-reopened',0)
    os.write(controller,b'SET MAIN 0.5 0.1\n');samples=[]
    try:
        for n in range(6):time.sleep(.3);samples.append(read(f'actions-stick-{n}'))
    finally:os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2)
    results.append(('stick-progress',all(row>=0 for row in samples) and len(set(samples))>1));print(f'action stick rows: {samples}',flush=True)
    press('B');press('A');focus('actions-reset',0)
    press('D_UP');focus('actions-wrap-up',9)
    press('D_DOWN');focus('actions-wrap-down',0)
    press('B');screenshot('actions-back.png')
    (root/'item-navigation.txt').write_text('\n'.join(f'{name}: {passed}' for name,passed in results)+f'\nPaired row: {paired}\nStick rows: {samples}\n')
    if not all(passed for _,passed in results):raise AssertionError('Item action navigation failed; see item-navigation.txt')

def settings_navigation():
    results=[]
    held_samples=[]
    def read_focus(name):
        screenshot(name+'.png')
        pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
        scores=[sum(all(abs(pixels[(y*800+x)*3+c]-target)<=3 for c,target in enumerate((221,231,188)))
                    for y in range(131+i*24,151+i*24) for x in range(100,540,4)) for i in range(9)]
        return scores.index(max(scores)) if max(scores)>100 else -1
    def focus(name,expected):
        actual=read_focus(name)
        results.append((name,expected,actual));print(f'{name}: expected row {expected}, actual {actual}',flush=True)
    def stick(y,duration=.18):
        os.write(controller,f'SET MAIN 0.5 {y}\n'.encode());time.sleep(duration)
        os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(1)
    focus('settings-initial',0)
    stick(.1);focus('settings-stick-down',1)
    press('D_DOWN');focus('settings-dpad-down',2)
    press('A');screenshot('settings-about.png');press('B');press('B');focus('settings-about-return',2)
    press('D_UP');focus('settings-dpad-up',1)
    press('A');screenshot('settings-controls.png');press('B');press('B');focus('settings-controls-return',1)
    press('D_DOWN');press('D_DOWN');press('A');focus('settings-performance-on',3)
    press('A');focus('settings-performance-off',3)
    for _ in range(3):press('D_UP')
    press('D_UP');focus('settings-wrap',8)
    press('D_UP');press('D_UP');focus('settings-model-row',6)
    press('A');focus('settings-model-direct',6)
    press('A');focus('settings-model-indexed',6)
    press('R');screenshot('settings-next-tab.png');press('L');focus('settings-tab-return',0)
    press('B');screenshot('settings-close.png')
    press('X');press('L');focus('settings-backpack-entry',0)
    # Guest time need not advance at wall-clock speed in Dolphin. Observe
    # progress under a continuously held stick instead of assuming how many
    # 120 ms guest-clock repeats fit into a short wall-clock pulse.
    for name,y in [('down',.1),('up',.9)]:
        os.write(controller,f'SET MAIN 0.5 {y}\n'.encode())
        samples=[]
        try:
            for n in range(8):
                time.sleep(.3);samples.append(read_focus(f'settings-held-{name}-{n}'))
        finally:
            os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2)
        passed=all(row>=0 for row in samples) and len(set(samples))>1
        results.append((f'settings-held-{name}-progress',1,int(passed)))
        held_samples.append(f'Held {name} rows: {samples}')
        print(held_samples[-1],flush=True)
    (root/'settings-navigation.txt').write_text('\n'.join(f'{name}: expected={expected}, actual={actual}' for name,expected,actual in results)+'\n'+'\n'.join(held_samples)+'\n')
    if any(expected!=actual for _,expected,actual in results):raise AssertionError('Settings navigation failed; see settings-navigation.txt and screenshots')
(user/'Config'/'Dolphin.ini').write_text('''[Core]
CPUCore = 1
CPUThread = False
DSPHLE = False
SkipIPL = True
SlotA = 1
SlotB = 255
MemcardAPath = '''+str(root/'MemoryCardA.USA.raw')+'''
[Display]
RenderToMain = False
RenderWindowWidth = 640
RenderWindowHeight = 480
[DSP]
EnableJIT = True
[Interface]
ConfirmStop = False
UsePanicHandlers = False
''')
if '--season' in sys.argv:
    season=sys.argv[sys.argv.index('--season')+1]
    month={'spring':4,'summer':7,'fall':10,'winter':1}[season]
    rtc=int(datetime(2027,month,15,12,tzinfo=timezone.utc).timestamp())
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text().replace('SkipIPL = True\n',f'SkipIPL = True\nEnableCustomRTC = True\nCustomRTCValue = {rtc}\n'))
if '--dsp-hle' in sys.argv:
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text().replace('DSPHLE = False','DSPHLE = True'))
(user/'Config'/'GFX.ini').write_text('''[Settings]
InternalResolution = 1
MSAA = 1
EFBScale = 1
DumpFrames = True
DumpFormat = avi
DumpCodec = ffv1
[Hacks]
EFBToTextureEnable = False
''')
if '--profile' in sys.argv or '--interaction-profile' in sys.argv:
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text()+'\n[Movie]\nDumpFrames = True\nDumpFramesSilent = True\n')
    config=user/'Config'/'GFX.ini'
    config.write_text(config.read_text().replace('DumpCodec = ffv1','DumpCodec = ffv1\nDumpEncoder = ffv1\nDumpPixelFormat = bgr0'))
(user/'Config'/'Logger.ini').write_text('[Options]\nWriteToFile = True\nVerbosity = 4\n[Logs]\nBOOT = True\nOSREPORT = True\nDVDINTERFACE = True\n')
env=dict(os.environ,DISPLAY=':93',QT_QPA_PLATFORM='xcb')
xvfb=subprocess.Popen(['Xvfb',':93','-screen','0','800x600x24'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
time.sleep(1)
with (root/'emulator.log').open('w') as log:
    p=subprocess.Popen(['/usr/games/dolphin-emu-nogui','-u',str(user),'-e',str(disc),'-v','OGL'],env=env,stdout=log,stderr=subprocess.STDOUT)
    try:
        time.sleep(45)
        if p.poll() is not None:
            raise RuntimeError(f'Dolphin exited during boot ({p.returncode}); see {root / "emulator.log"}')
        screenshot('screen.png')
        if '--building-reload' in sys.argv:
            press('A');time.sleep(5);building_view('building-reloaded-interior.png',True)
        elif '--buildings' in sys.argv:
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('B');os.write(controller,b'RELEASE L\nRELEASE R\n')
            time.sleep(3);building_view('building-exterior.png',False)
            time.sleep(10);screenshot('building-npc-journey.png')
            os.write(controller,b'PRESS A\n');time.sleep(.5);screenshot('building-door-opening.png');os.write(controller,b'RELEASE A\n')
            time.sleep(4);building_view('building-interior-0.png',True)
            for view in range(1,5):
                os.write(controller,b'SET C 0.9 0.5\n');time.sleep(2);os.write(controller,b'SET C 0.5 0.5\n');time.sleep(1);screenshot(f'building-interior-{view}.png')
            press('L');time.sleep(1)
            building_save();press('B')
            press('A');time.sleep(4);building_view('building-exited.png',False)
        elif '--cliff-kit' in sys.argv:
            press('A')
            if '--new-town' in sys.argv:press('D_UP');press('A');press('A');time.sleep(2);press('B');press('B')
            time.sleep(3);screenshot('cliff-normal.png')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_UP');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(3);screenshot('cliff-kit-eight.png')
            os.write(controller,b'SET MAIN 0.9 0.5\n');time.sleep(1);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('cliff-kit-uv-shift.png')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_UP');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(2);screenshot('cliff-kit-off.png')
            if '--new-town' in sys.argv:
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(3);screenshot('cliff-raised-walls.png')
        elif '--uv-debug' in sys.argv:
            press('A');time.sleep(3);screenshot('uv-normal.png')
            def setting(up,name):
                press('X');press('L')
                for _ in range(up):press('D_UP')
                screenshot(name+'-setting.png');press('A');time.sleep(2);screenshot(name+'-enabled.png');press('B');time.sleep(3);screenshot(name+'.png')
            setting(2,'uv-colors')
            setting(1,'uv-combined')
            setting(2,'uv-lines')
            setting(1,'uv-restored')
            press('START');time.sleep(12);screenshot('uv-save.png')
        elif '--dialogue-cycles' in sys.argv:
            dialogue_cycles()
        elif '--movement-directions' in sys.argv:
            press('A');time.sleep(2);screenshot('movement-start.png')
            os.write(controller,b'SET MAIN 0.5 0.0\nPRESS B\n');time.sleep(3)
            os.write(controller,b'SET MAIN 0.5 0.5\nRELEASE B\n');time.sleep(1)
            axes=[(.9,.1),(.1,.1),(.1,.9),(.9,.9)]
            pads=[('D_RIGHT','D_DOWN'),('D_LEFT','D_DOWN'),('D_LEFT','D_UP'),('D_RIGHT','D_UP')]
            for mode in ('stick','dpad'):
                for n in range(4):
                    if mode=='stick':os.write(controller,f'SET MAIN {axes[n][0]} {axes[n][1]}\n'.encode())
                    else:os.write(controller,(''.join(f'PRESS {button}\n' for button in pads[n])).encode())
                    if n%2:os.write(controller,b'PRESS B\n')
                    time.sleep(2)
                    os.write(controller,b'SET MAIN 0.5 0.5\nRELEASE B\n')
                    if mode=='dpad':os.write(controller,(''.join(f'RELEASE {button}\n' for button in pads[n])).encode())
                    time.sleep(.5);screenshot(f'movement-{mode}-{n}.png')
            press('START');time.sleep(12);screenshot('movement-save.png')
        elif '--cloud-shadows' in sys.argv:
            press('A');press('D_UP');press('A');press('A');time.sleep(2);press('B');press('B')
            time.sleep(2);screenshot('cloud-partial-a.png');time.sleep(20);screenshot('cloud-partial-b.png')
            def cloud_cover():
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_RIGHT');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(2)
            cloud_cover();screenshot('cloud-clear.png')
            cloud_cover();screenshot('cloud-overcast.png')
            cloud_cover();screenshot('cloud-restored.png')
            press('X');screenshot('cloud-menu.png');press('B')
            press('A');time.sleep(2);screenshot('cloud-indoor-a.png');time.sleep(8);screenshot('cloud-indoor-b.png')
            press('A');time.sleep(2);screenshot('cloud-exterior-return.png')
        elif '--river-coast' in sys.argv:
            press('A');press('D_UP');press('A');press('A');time.sleep(2);press('B');press('B')
            for scene in ['river', 'river-corner', 'bridge', 'coast-beach', 'estuary', 'restored']:
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_LEFT');os.write(controller,b'RELEASE L\nRELEASE R\n')
                time.sleep(3);screenshot(scene+'.png')
                if scene=='river':
                    time.sleep(8);screenshot('river-animated.png')
                    def rgb(name):return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
                    first=rgb('river.png');second=rgb('river-animated.png');wet=changed=0
                    for at in range(0,len(first),3):
                        r,g,b=first[at:at+3]
                        if r<120 and g>100 and b>g+4:
                            wet+=1;changed+=first[at:at+3]!=second[at:at+3]
                    (root/'river-animation.txt').write_text(f'Water pixels: {wet}\nAnimated water pixels: {changed}\n')
                    if wet<1000 or changed<50:raise AssertionError('Expected visible animated lowered river')
            press('X');screenshot('river-coast-menu.png');press('B')
        elif '--water' in sys.argv:
            # Fresh card: create a player, then use the clear starter reserve
            # to reach the western ocean boundary in any generated town.
            press('A');press('D_UP');press('A');press('A');time.sleep(2);press('B');press('B')
            os.write(controller,b'SET MAIN 0.0 0.5\nPRESS B\n');time.sleep(14)
            os.write(controller,b'SET MAIN 0.5 0.5\nRELEASE B\n');time.sleep(2)
            def water_frame(name):
                screenshot(name+'.png')
                return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
            first=water_frame('water-a');time.sleep(8);second=water_frame('water-b')
            wet=changed=0
            for y in range(120,430):
                for x in range(20,300):
                    at=(y*800+x)*3;r,g,b=first[at:at+3]
                    if r<120 and g>100 and b>g+4:
                        wet+=1
                        if first[at:at+3]!=second[at:at+3]:changed+=1
            (root/'water-check.txt').write_text(f'Water pixels: {wet}\nAnimated water pixels: {changed}\n')
            if wet<1000 or changed<50:raise AssertionError('Expected visible animated ocean; inspect water-a/b.png and water-check.txt')
            # Exercise camera reprojection, then a plain 2D material immediately
            # after dual-texture TEV (state must not leak into menus).
            os.write(controller,b'SET MAIN 0.5 0.1\n');time.sleep(2)
            os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('water-camera.png')
            press('X');screenshot('water-menu.png');press('B');screenshot('water-return.png')
        elif '--terrain-edges' in sys.argv:
            press('A');time.sleep(2);screenshot('terrain-edge-start.png')
            directions=[(.5,0,3),(0,.5,2),(1,.5,4),(.5,0,2),(0,.5,2),(.5,1,2),(1,0,2),(0,1,2)]
            for n,(axis_x,axis_y,duration) in enumerate(directions):
                os.write(controller,f'SET MAIN {axis_x} {axis_y}\nPRESS B\n'.encode());time.sleep(duration)
                os.write(controller,b'SET MAIN 0.5 0.5\nRELEASE B\n');time.sleep(.5);screenshot(f'terrain-edge-{n}.png')
            press('START');time.sleep(12);screenshot('terrain-edge-save.png')
        elif '--interaction-profile' in sys.argv:
            press('A');time.sleep(2)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(1);press('Z')
            os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('interaction-world.png')
            press('A');screenshot('interaction-typing.png');time.sleep(profile_seconds);screenshot('interaction-dialogue.png')
            press('B');press('X');time.sleep(profile_seconds);screenshot('interaction-backpack.png')
            press('A');time.sleep(profile_seconds);screenshot('interaction-actions.png')
            press('B');press('L');press('D_DOWN');press('A');screenshot('interaction-long-typing.png')
            time.sleep(profile_seconds);screenshot('interaction-long-dialogue.png')
        elif '--terrain-life' in sys.argv:
            press('A');press('D_UP');press('A');press('A');time.sleep(2);press('B');press('B')
            screenshot('life-world-before.png');time.sleep(20);screenshot('life-world-after.png')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(2)
            screenshot('terrain-ramp-low.png')
            os.write(controller,b'SET MAIN 0.5 0.9\n');time.sleep(1.7);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('terrain-ramp-high.png')
            press('START');time.sleep(12);screenshot('terrain-high-save.png');press('B')
            os.write(controller,b'SET MAIN 0.5 0.1\n');time.sleep(1.7);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('terrain-ramp-return.png')
            press('Y');time.sleep(2);screenshot('life-map.png');press('B');press('START');time.sleep(12);screenshot('terrain-life-save.png')
        elif '--play' in sys.argv:
            press('A');press('D_UP');press('A');press('A');time.sleep(2);screenshot('bubble.png');press('B');press('B')
            time.sleep(2);screenshot('world.png')
            press('Y');time.sleep(1);screenshot('map.png');press('Y')
            press('X');time.sleep(1);screenshot('pockets.png');press('B')
            press('START');time.sleep(12);screenshot('save.png')
        elif '--resume' in sys.argv:
            press('A');time.sleep(2);screenshot('resume.png')
            press('X');time.sleep(1);screenshot('resume-backpack.png')
            if '--town-map' in sys.argv:
                press('B');press('Y');time.sleep(2);screenshot('town-map.png')
                press('B');press('X')
            if '--cache-transitions' in sys.argv:
                for n in range(3):
                    press('B');time.sleep(1);screenshot(f'cache-world-{n}.png')
                    press('Y');time.sleep(1);screenshot(f'cache-map-{n}.png')
                    press('B');press('X');time.sleep(1);screenshot(f'cache-backpack-{n}.png')
            if '--backpack-layout' in sys.argv:
                press('A');screenshot('backpack-actions.png')
                press('B');screenshot('backpack-return.png')
                press('D_LEFT');screenshot('backpack-last-slot.png')
                for _ in range(4):press('D_LEFT')
                screenshot('backpack-lower-left.png')
                press('D_UP')
                for _ in range(2):press('D_RIGHT')
                screenshot('backpack-selected-item.png')
                press('A');screenshot('backpack-selected-actions.png')
                for _ in range(9):press('D_DOWN')
                press('A');screenshot('backpack-quit.png')
            if '--item-navigation' in sys.argv:item_navigation()
            press('START');time.sleep(12);screenshot('resume-save.png')
            if '--split-save' in sys.argv:
                press('B');time.sleep(5);press('START');time.sleep(12);screenshot('split-save-second.png')
            if '--settings-navigation' in sys.argv:settings_navigation()
        if '--profile' in sys.argv:
            press('B')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('Z')
            os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-world.png')
            if '--compare-batches' in sys.argv:
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('X')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-world-baseline.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('X')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-world-batched.png')
            if '--compare-renderers' in sys.argv:
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('Y')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-world-direct.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('Y')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(3);screenshot('profile-world-indexed.png')
            os.write(controller,b'SET MAIN 0.8 0.5\n');time.sleep(12)
            os.write(controller,b'SET MAIN 0.2 0.5\n');time.sleep(12)
            os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(3)
            press('X');time.sleep(profile_seconds);screenshot('profile-backpack.png')
            if '--compare-batches' in sys.argv:
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('X')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-backpack-baseline.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('X')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-backpack-batched.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('D_DOWN')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-backpack-legacy-font.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('D_DOWN')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('profile-backpack-font-return.png')
                press('B');press('Y');time.sleep(max(10,profile_seconds));screenshot('profile-map-batched.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('X')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(max(10,profile_seconds));screenshot('profile-map-baseline.png')
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(2);press('X')
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(max(10,profile_seconds));screenshot('profile-map-return.png')
    finally:
        p.terminate()
        try:p.wait(timeout=5)
        except subprocess.TimeoutExpired:p.kill();p.wait()
xvfb.terminate();xvfb.wait()
os.close(controller)
print(root/'screen.png')
