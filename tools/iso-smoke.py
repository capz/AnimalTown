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
                    for y in range(131+i*29,155+i*29) for x in range(100,540,4)) for i in range(7)]
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
    press('D_UP');focus('settings-wrap',6)
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
x=subprocess.Popen(['Xvfb',':93','-screen','0','800x600x24'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
time.sleep(1)
with (root/'emulator.log').open('w') as log:
    p=subprocess.Popen(['/usr/games/dolphin-emu-nogui','-u',str(user),'-e',str(disc),'-v','OGL'],env=env,stdout=log,stderr=subprocess.STDOUT)
    try:
        time.sleep(45)
        if p.poll() is not None:
            raise RuntimeError(f'Dolphin exited during boot ({p.returncode}); see {root / "emulator.log"}')
        screenshot('screen.png')
        if '--interaction-profile' in sys.argv:
            press('A');time.sleep(2)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(1);press('Z')
            os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('interaction-world.png')
            press('A');screenshot('interaction-typing.png');time.sleep(profile_seconds);screenshot('interaction-dialogue.png')
            press('B');press('X');time.sleep(profile_seconds);screenshot('interaction-backpack.png')
            press('A');time.sleep(profile_seconds);screenshot('interaction-actions.png')
            press('B');press('L');press('D_DOWN');press('A');screenshot('interaction-long-typing.png')
            time.sleep(profile_seconds);screenshot('interaction-long-dialogue.png')
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
x.terminate();x.wait()
os.close(controller)
print(root/'screen.png')
