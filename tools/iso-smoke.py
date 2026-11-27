"""Direct ISO boot: no separately selected DOL. Run inside animaltown-dolphin.

--title-logo captures the title, picker and naming header, then discards back
to the title. It also checks fitted normal/debug panels and expanded status
feedback. Use a disposable card with no committed town; no world is loaded.
The title-logo-report.json check covers committed town state, not card formatting
or independent console/dev configuration writes.

--terrain-materials creates a disposable town and checks actual GX mip-band
sampling plus animated/frozen water captures. Its debug changes are session-only.

--daylight-location uses an empty card to verify the first-town shortcut,
local lighting, explicit saves and the shared location after a title reload.

--player-chat uses a disposable --chat-fixture card to exercise the controller
keyboard, normal player speech, live debug gating and one resident attention cue.

--aram-residency verifies automatic indoor terrain/floor reclamation, real
model retire/restore cycles, rapid doorway reversal and the default after reboot.

--online-review captures the actual half-world closure panel using a disposable
online-review fixture, then cancels before any account/network operation.

--online-play uses a private disposable credential fixture, the emulated
broadband adapter and a real public HTTPS/WSS backend. Run this only in CI.

--warm-terrain remains accepted for older capture commands. Terrain prefetch
is automatic; this flag no longer changes developer settings or opens a menu.
"""
import os
import atexit
import json
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
from dolphin_pad import pad_config
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
    subprocess.run(['ffmpeg','-v','error','-f','x11grab','-draw_mouse','0','-video_size','800x600','-i',env['DISPLAY'],'-frames:v','1','-y',str(root/name)],env=env,check=True)

def start_display():
    # Let Xvfb select a free display instead of reusing image-baked :93 locks.
    # -displayfd reports its number only once the server accepts clients.
    import select
    read_fd,write_fd=os.pipe();server=None
    try:
        with (root/'xvfb.log').open('w') as log:
            server=subprocess.Popen(['Xvfb','-displayfd',str(write_fd),'-noreset','-nocursor','-screen','0','800x600x24'],pass_fds=(write_fd,),stdout=log,stderr=subprocess.STDOUT)
        os.close(write_fd);write_fd=-1
        if not select.select([read_fd],[],[],30)[0]:raise RuntimeError('Xvfb did not report a ready display; inspect xvfb.log')
        # Xvfb writes the number and its newline separately. Do not close the
        # pipe after a short first read: its newline write would receive EPIPE.
        ready=b''
        while b'\n' not in ready:
            if not select.select([read_fd],[],[],30)[0]:raise RuntimeError('Xvfb readiness line did not finish')
            part=os.read(read_fd,32)
            if not part:raise RuntimeError('Xvfb closed its readiness pipe')
            ready+=part
            if len(ready)>32:raise RuntimeError('Invalid Xvfb readiness line')
        number=ready.decode().strip()
        if not number.isdigit() or server.poll() is not None:raise RuntimeError('Xvfb failed to initialize; inspect xvfb.log')
        environment=dict(os.environ,DISPLAY=':'+number,QT_QPA_PLATFORM='xcb')
        # llvmpipe otherwise creates a worker per visible host CPU. Multiple
        # disposable controller runs can exhaust the container's thread limit.
        environment.setdefault('LP_NUM_THREADS','2')
        return server,environment
    except BaseException:
        if server is not None:
            server.terminate()
            try:server.wait(timeout=5)
            except subprocess.TimeoutExpired:server.kill();server.wait()
        raise
    finally:
        os.close(read_fd)
        if write_fd>=0:os.close(write_fd)


def rgb(name):
    return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])

def fill_matches(pixel,color,tolerance=5):
    # Default UI grain multiplies fill RGB by an I8 sample of 245..255;
    # alpha/text stay unchanged. Compare hue along that brightness range,
    # allowing a small margin for GX integer rounding and filtered samples.
    peak=max(color)
    if peak==0:return max(pixel)<=tolerance
    scale=max(pixel)/peak
    return .85<=scale<=1.02 and all(abs(pixel[c]-value*scale)<=tolerance
        for c,value in enumerate(color))

def color_count(pixels,color,tolerance=5):
    return sum(fill_matches(pixels[i:i+3],color,tolerance)
               for i in range(0,len(pixels),3))

def region_color_count(pixels,color,left,top,right,bottom,tolerance=5):
    return sum(fill_matches(pixels[(y*800+x)*3:(y*800+x)*3+3],color,tolerance)
               for y in range(top,bottom) for x in range(left,right))

def runtime_log_text():
    files=sorted((user/'Logs').glob('*.log'))
    if files:return '\n'.join(path.read_text(errors='replace') for path in files)
    path=root/'emulator.log'
    return path.read_text(errors='replace') if path.exists() else ''

def aram_residency_errors(text):
    import re
    session=text.rfind('ANIMALTOWN GAME START');errors=[];counts={}
    for event in re.finditer(r'ANIMALTOWN MODEL RESIDENCY (RETRY|ADMISSION) (\d+): ([^\r\n]+)',text[max(0,session):]):
        species=int(event[2]);counts[species]=counts.get(species,0)+1
        errors.append({'operation':event[1],'species':species,'message':event[3]})
    return {'session_start':session,'errors':errors,'repeated_species':sorted(species for species,count in counts.items() if count>1)}

def check_runtime_failure():
    # Session errors must be detected before native shutdown can hide the
    # managed exception behind an unchanged loading frame.
    failures=[line for line in runtime_log_text().splitlines()
              if ('ANIMALTOWN SESSION FAILED' in line or 'ANIMALTOWN ASSET LOAD FAILED' in line
                  or 'ANIMALTOWN BUILDING ASSET ERROR' in line or 'ANIMALTOWN MANAGED OOM' in line
                  or 'MANAGED MAIN: FAILED' in line)]
    if failures:
        (root/'runtime-failure.txt').write_text('\n'.join(failures)+'\n')
        context=[line for line in runtime_log_text().splitlines() if '[OSREPORT]' in line][-40:]
        print('Runtime context before failure:\n'+'\n'.join(context),flush=True)
        raise AssertionError('Game runtime failure: '+failures[0])
    if '--aram-residency' in sys.argv:
        evidence=aram_residency_errors(runtime_log_text())
        if evidence['errors']:(root/'runtime-residency-errors.json').write_text(json.dumps(evidence,indent=2)+'\n')
        if evidence['repeated_species']:
            cause=next(error for error in evidence['errors'] if error['species'] in evidence['repeated_species'])
            raise AssertionError('Repeated model residency failures for species '+str(evidence['repeated_species'])+
                                 ': '+cause['message'])

def press_presented(button,clock):
    # Card I/O and software rendering can outlast a wall-timed button pulse.
    # Hold each edge across actual guest presentations before sending the next.
    def frames(count):
        clock.frames();target=len(clock.presentations)+count;deadline=time.monotonic()+90
        while True:
            clock.frames()
            if len(clock.presentations)>=target:return
            check_runtime_failure()
            if p.poll() is not None:raise AssertionError('Emulator stopped during title checkpoint input')
            if time.monotonic()>deadline:raise AssertionError('Guest did not present during title checkpoint input')
            time.sleep(.05)
    os.write(controller,b'RELEASE A\nRELEASE B\nRELEASE START\nRELEASE D_UP\nRELEASE D_DOWN\n')
    frames(8);os.write(controller,f'PRESS {button}\n'.encode());frames(12)
    os.write(controller,f'RELEASE {button}\n'.encode());frames(12)

def card_digest():
    import hashlib
    card=root/'MemoryCardA.USA.raw'
    return hashlib.sha256(card.read_bytes()).hexdigest() if card.exists() else None

def onboarding_signature(pixels,stage):
    # Draw2D's 640x480 canvas is letterboxed to Dolphin's 584px viewport.
    # These are broad interior fill samples, so neither text rasterization nor
    # the animated backdrop affects the stage checks.
    cream=color_count(pixels,(255,245,221))
    if stage=='name':
        return (cream>70000 and region_color_count(pixels,(245,226,186),65,279,590,400)>23000
                and region_color_count(pixels,(53,105,84),350,143,550,168)<1000)
    if stage=='look':
        return cream>70000 and region_color_count(pixels,(53,105,84),350,143,550,168)>3000
    if stage=='review':
        return cream>70000 and region_color_count(pixels,(53,105,84),130,353,380,397)>7000
    if stage=='exit':
        return (cream>70000 and region_color_count(pixels,(53,105,84),150,300,300,328)>2500
                and region_color_count(pixels,(245,226,186),340,300,490,328)>2500)
    if stage=='home' or stage=='world':
        if cream>=70000 or color_count(pixels,(149,198,186))>=150000:return False
        # Autumn/winter views may contain no green grass or trees. Require an
        # actual rendered viewport, then distinguish the placement header
        # from gameplay instead of depending on the seasonal ground palette.
        rendered=sum(max(pixels[(y*800+x)*3:(y*800+x)*3+3])>20
                     for y in range(0,480,2) for x in range(28,612,2))
        header=region_color_count(pixels,(255,245,221),160,25,480,67)
        # The shop's full-width interaction cue shares the top of the screen
        # with the narrower plot-preview heading. Its left edge distinguishes
        # a saved indoor game from an unfinished home-placement preview.
        shop_hint=region_color_count(pixels,(255,245,221),50,35,125,62)>1000
        if stage=='home':return rendered>60000 and header>5000 and not shop_hint
        # Gameplay has no bottom legend, and running hides the floating clock.
        return rendered>60000 and (header<5000 or shop_hint)
    raise ValueError('Unknown onboarding stage: '+stage)

def wait_onboarding(stage,name=None):
    name=name or 'onboarding-'+stage+'.png'
    deadline=time.monotonic()+(360 if stage in ('home','world') else 180)
    loading=False
    while time.monotonic()<deadline:
        check_runtime_failure()
        if p.poll() is not None:raise AssertionError('Emulator stopped before onboarding '+stage)
        screenshot(name);pixels=rgb(name)
        if color_count(pixels,(149,198,186))>150000 and color_count(pixels,(255,245,221))<100:loading=True
        if onboarding_signature(pixels,stage):return loading
        time.sleep(1)
    context=[line for line in runtime_log_text().splitlines() if 'ANIMALTOWN' in line or 'TOWN LOAD ERROR' in line][-30:]
    print('Runtime context before onboarding timeout:\n'+'\n'.join(context),flush=True)
    raise AssertionError('Onboarding did not reach '+stage+'; inspect '+name)

def wait_saved_after(before,card=None,timeout_seconds=240):
    deadline=time.monotonic()+timeout_seconds
    sequence=before['sequence'] if before else 0
    while time.monotonic()<deadline:
        check_runtime_failure()
        after=committed_card_sequence(card or root/'MemoryCardA.USA.raw',details=True)
        if after and after['sequence']>sequence:return after
        if p.poll() is not None:raise AssertionError('Emulator stopped during explicit onboarding save')
        time.sleep(1)
    screenshot('onboarding-save-failure.png')
    raise AssertionError('Save & begin did not publish a complete checksummed generation')

def stop_dolphin(process,timeout_seconds=5):
    """Observe shutdown; a forced kill cannot establish a flushed raw card."""
    forced=False
    if process.poll() is None:
        process.terminate()
        try:process.wait(timeout=timeout_seconds)
        except subprocess.TimeoutExpired:
            forced=True;process.kill();process.wait()
    code=process.poll()
    return {'returncode':code,'forced_kill':forced,'clean_exit':code==0 and not forced}

def run_jobs(context):
    from jobs_smoke import run,finish
    # Dolphin periodically overwrites raw cards in place. Its normal shutdown
    # joins the card writer and completes the final flush before returning 0.
    # A controller exception still propagates after cleanup, without export.
    try:report=run(context)
    finally:shutdown=stop_dolphin(context['p'],timeout_seconds=30)
    finish(context,report,shutdown)

def finish_onboarding(prefix='new-villager',before=None):
    """Complete an already selected empty slot via the actual four stages."""
    before=before if before is not None else committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    wait_onboarding('name',prefix+'-name.png');press('A');press('START')
    wait_onboarding('look',prefix+'-look.png');press('A')
    loading=wait_onboarding('home',prefix+'-home.png');press('A')
    wait_onboarding('review',prefix+'-review.png');press('A')
    after=wait_saved_after(before);wait_onboarding('world',prefix+'-game.png')
    return after,loading

def testing_menu_options():
    import re
    source=(Path(__file__).resolve().parents[1]/'game/AnimalTown.GameCube/Program.cs').read_text()
    match=re.search(r'\btestingOptions\s*=\s*\{([^}]+)\}',source)
    if not match:raise AssertionError('Cannot locate debug-menu option contract')
    return [entry.strip() for entry in match[1].split(',') if entry.strip()]


def villager_visuals():
    """Real menu/model/animation swaps, with a disposable town and saved records."""
    import re
    sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(user/'Dump/Frames',track_presentations=True)
    # Options saves and model paging can outlast a wall-timed pulse. Wait for
    # actual guest presentations on both sides of each menu input edge.
    def tap(button):press_presented(button,clock)
    start_game(prefix='visuals-start');set_debug_visibility(True)
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    report={'scenario':'villager-visuals','passed':False,'swaps':[],'physical_hardware':False}
    def marker(pattern,after=0,retry=None):
        deadline=time.monotonic()+120
        retry_at=time.monotonic()+3
        while time.monotonic()<deadline:
            if p.poll() is not None:raise AssertionError('Game exited during visual swapping')
            check_runtime_failure()
            match=re.search(pattern,runtime_log_text()[after:])
            if match:return match
            # Essential admission may first ask the paging owner to reclaim
            # headroom. Repeat Apply as the menu instructs; busy loads ignore A.
            if retry and time.monotonic()>=retry_at:
                retry();retry_at=time.monotonic()+3
            time.sleep(.2)
        screenshot('visuals-timeout.png');raise AssertionError('Missing visual marker: '+pattern)
    def choose(actor,appearance):
        begin=len(runtime_log_text());tap('START');tap('R')
        row=testing_menu_row('VillagerVisualOption');count=testing_menu_count()
        for _ in range(min(row,count-row)):tap('D_DOWN' if row<=count//2 else 'D_UP')
        tap('A');marker(r'VILLAGER VISUAL MENU actor 0 ',begin)
        for _ in range(actor):tap('D_RIGHT')
        tap('D_DOWN')
        for _ in range(13):
            events=re.findall(r'VILLAGER VISUAL MENU actor (\d+) candidate (-?\d+)',runtime_log_text()[begin:])
            if events and tuple(map(int,events[-1]))==(actor,appearance):break
            tap('D_RIGHT')
        else:raise AssertionError('Cannot select unused appearance '+str(appearance))
        screenshot('visuals-selected-'+str(actor)+'-'+str(appearance)+'.png')
        tap('D_DOWN');apply_at=len(runtime_log_text());tap('A')
        marker(r'VILLAGER VISUAL APPLIED actor '+str(actor)+' appearance '+str(appearance)+r'\b',apply_at,retry=lambda:tap('A'))
        screenshot('visuals-applied-'+str(actor)+'-'+str(appearance)+'.png')
        tap('B');tap('B');time.sleep(2)
        screenshot('visuals-world-'+str(actor)+'-'+str(appearance)+'.png')
        report['swaps'].append({'actor':actor,'appearance':appearance})
    for appearance in range(6,12):
        choose(0,appearance)
        # Force both standing and locomotion on each supplied rig.
        # Walk back on alternate rigs so the fixture stays around its home
        # view instead of accumulating a town-wide residency/streaming tour.
        # Optional swaps may correctly refuse memory in a crowded scene.
        os.write(controller,b'SET MAIN '+(b'0.7' if appearance%2==0 else b'0.3')+b' 0.5\n');time.sleep(2)
        os.write(controller,b'PRESS B\n');time.sleep(2)
        os.write(controller,b'RELEASE B\n')
        os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(1)
    choose(1,6)
    after=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    if after!=before:raise AssertionError('Visual-only swaps changed committed town/player/job records')
    report.update(passed=True,committed_records_unchanged=True)
    (root/'villager-visuals-report.json').write_text(json.dumps(report,indent=2)+'\n')

def testing_menu_count():
    return len(testing_menu_options())

def testing_menu_row(option):
    """Resolve an option ID/name rather than assuming its current display row."""
    import re
    wanted=str(option);options=testing_menu_options()
    if wanted in options:return options.index(wanted)
    if wanted.isdecimal():
        source=(Path(__file__).resolve().parents[1]/'game/AnimalTown.GameCube/Program.cs').read_text()
        for row,name in enumerate(options):
            constant=re.search(r'\b'+re.escape(name)+r'\s*=\s*(\d+)\b',source)
            if constant and int(constant[1])==int(wanted):return row
    raise AssertionError('Debug option is not available: '+wanted)

def start_game(new_town=False,prefix='start',tap=None):
    """Navigate Title and the resident picker before sending gameplay input."""
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    tap=tap or press
    wait_title();tap('A');wait_profiles(prefix+'-picker.png');tap('A')
    active=before['active'] if before else 0
    if new_town or not before or not before['profiles'][active]['created']:
        return finish_onboarding(prefix,before)
    return before,wait_onboarding('world',prefix+'-game.png')

def player_chat():
    import re
    sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(user/'Dump/Frames',track_presentations=True)
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    if not before:raise AssertionError('Use a disposable --chat-fixture card')
    start_game(prefix='chat-start');set_debug_visibility(False)
    initial=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    report={'scenario':'player-chat','disc':disc.name,'passed':False,'screenshots':[],'events':[]}
    selected_key=0

    def frames(count):
        clock.frames();target=len(clock.presentations)+count;deadline=time.monotonic()+90
        while len(clock.presentations)<target:
            check_runtime_failure()
            if p.poll() is not None:raise AssertionError('Emulator stopped during player chat')
            if time.monotonic()>deadline:raise AssertionError('Chat stopped presenting frames')
            time.sleep(.03);clock.frames()

    def tap(button):
        os.write(controller,f'PRESS {button}\n'.encode());frames(3)
        os.write(controller,f'RELEASE {button}\n'.encode());frames(3)

    def events():return re.findall(r'ANIMALTOWN CHAT SPEECH actor (\d+) pages (\d+)',runtime_log_text())

    def capture(name):
        frames(3);screenshot(name);report['screenshots'].append(name)

    def wait_speech():
        # Presentations and simulation ticks need not run at the same rate.
        # Observe the actual IPL cue: only "Show all" extends into this tail;
        # a completed page says "Next" or "Close". Also require the panel so
        # leaving dialogue cannot silently satisfy the ready condition.
        deadline=time.monotonic()+90
        while time.monotonic()<deadline:
            frames(2);screenshot('chat-page-wait.png');pixels=rgb('chat-page-wait.png')
            if region_color_count(pixels,(255,245,221),80,340,540,425)<26000:raise AssertionError('Speech panel closed before its page was read')
            if region_color_count(pixels,(49,72,62),540,435,555,466)<2:return
        raise AssertionError('Speech did not finish typing')

    def open_chat():
        nonlocal selected_key
        count=runtime_log_text().count('ANIMALTOWN CHAT OPEN');tap('R');selected_key=0
        if runtime_log_text().count('ANIMALTOWN CHAT OPEN')<=count:raise AssertionError('R did not open chat')

    def type_text(text):
        nonlocal selected_key
        keys="qwertyuiopasdfghjkl'zxcvbnm,./0123456789 !?-:;()@+"
        for character in text:
            target=keys.index(character);row,col=divmod(selected_key,10);tr,tc=divmod(target,10)
            while row!=tr:tap('D_DOWN');row=(row+1)%5
            while col!=tc:tap('D_RIGHT');col=(col+1)%10
            tap('A');selected_key=target

    def send(actor):
        count=len(events());tap('START');frames(4)
        if len(events())!=count+1 or int(events()[-1][0])!=actor:raise AssertionError('Chat routed to the wrong speaker')
        pages=int(events()[-1][1]);wait_speech();return pages

    def close_speech(pages):
        for index in range(pages):
            if index:wait_speech()
            tap('A')

    try:
        open_chat();capture('chat-keyboard.png');type_text('hello neighbours!')
        pages=send(0);capture('chat-player-speech.png');close_speech(pages)
        open_chat();type_text('/attend rowan hello')
        pages=send(0);capture('chat-command-disabled.png');close_speech(pages)
        set_debug_visibility(True)
        open_chat();type_text('/attend nobody hello')
        count=len(events());tap('START')
        if len(events())!=count or 'ANIMALTOWN CHAT INVALID' not in runtime_log_text():raise AssertionError('Invalid command was spoken')
        capture('chat-invalid-command.png')
        for _ in range(len('/attend nobody hello')):tap('X')
        type_text('/attend rowan hello');pages=send(1);capture('chat-rowan-attention.png');close_speech(pages)
        # Changing the flag while typing cannot retain command rights.
        open_chat();type_text('/attend rowan hello');set_debug_visibility(False)
        pages=send(0);capture('chat-command-disabled-mid-entry.png');close_speech(pages)
        # Wide uppercase glyphs force pagination with the real IPL metrics;
        # 120 lowercase w glyphs can fit in the existing three-line panel.
        open_chat();tap('L');type_text('w'*120);capture('chat-long-draft.png');pages=send(0)
        if pages<2:raise AssertionError('Long speech did not paginate')
        capture('chat-long-message.png');close_speech(pages)
        tap('START')
        for _ in range(5):tap('D_DOWN')
        count=runtime_log_text().count('ANIMALTOWN CHAT OPEN');tap('A')
        if runtime_log_text().count('ANIMALTOWN CHAT OPEN')<=count:raise AssertionError('Settings did not open chat')
        capture('chat-from-settings.png');tap('B');capture('chat-cancel-to-settings.png')
        count=runtime_log_text().count('ANIMALTOWN CHAT OPEN');tap('A')
        if runtime_log_text().count('ANIMALTOWN CHAT OPEN')<=count:raise AssertionError('Cancel did not restore the selected Settings entry')
        tap('B');tap('B')
        if committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=initial:raise AssertionError('Chat altered committed town or player data')
        report['events']=events();report['town_unchanged']=True;report['passed']=True
    finally:
        (root/'chat-report.json').write_text(json.dumps(report,indent=2)+'\n')

def daylight_location():
    import re
    card=root/'MemoryCardA.USA.raw'
    if committed_card_sequence(card):raise AssertionError('Daylight location needs a fresh card')

    def panel(name,selected):
        deadline=time.monotonic()+60
        y=165+selected*35
        while time.monotonic()<deadline:
            check_runtime_failure()
            screenshot(name);pixels=rgb(name)
            if region_color_count(pixels,(221,231,188),130,y+3,510,y+24)>1500:return
            time.sleep(.5)
        raise AssertionError('Daylight location did not show the requested focus: '+name)

    def next_city():
        press('D_DOWN');press('D_DOWN');press('D_RIGHT')

    def open_settings():
        press('START');press('D_UP');press('A')

    wait_title();press('A');wait_profiles('daylight-picker.png');press('A')
    wait_onboarding('name');press('A');press('START')
    wait_onboarding('look');press('A');wait_onboarding('home');press('A')
    wait_onboarding('review','daylight-review-shortcut.png');original=card_digest()
    press('X');panel('daylight-classic.png',0);press('D_RIGHT')
    press('D_DOWN');press('D_DOWN');panel('daylight-first-town.png',2)
    press('A');wait_onboarding('review')
    if card_digest()!=original:raise AssertionError('Applying a daylight location wrote the card')
    press('A');first=wait_saved_after(None);wait_onboarding('world','daylight-town-dusk.png')
    if first['daylight_location']!=1:raise AssertionError('Save & begin did not retain Amsterdam')
    matches=re.findall(r'ANIMALTOWN DAYLIGHT minute (\d+) shadow (\d+) cloud (\d+) lamp (\d+)',runtime_log_text())
    if not matches or tuple(map(int,matches[-1][1:]))!=(0,0,255):
        raise AssertionError('Amsterdam night did not reach the native daylight gains')

    original=card_digest();open_settings();panel('daylight-in-settings.png',0)
    next_city();panel('daylight-london-preview.png',2);press('B')
    press('A');panel('daylight-after-cancel.png',0)
    next_city();press('A')
    if card_digest()!=original:raise AssertionError('Editing the location bypassed the normal save actions')
    press('START');press('D_DOWN');press('A');press('D_DOWN');press('A')
    after=wait_saved_after(first)
    if after['daylight_location']!=2:raise AssertionError('Save & Quit did not retain London')
    reloaded,_=start_game(prefix='daylight-reload')
    if reloaded!=after:raise AssertionError('Location reload changed the committed town')
    open_settings();press('D_DOWN');press('D_DOWN');panel('daylight-reloaded-city.png',2)
    (root/'daylight-location.json').write_text(json.dumps({'first_city':'Amsterdam','reloaded_city':'London',
        'preview_date':'2026-11-19','applied_without_writes':True,'native_shadows':0,'native_lamps':255},indent=2)+'\n')

def daylight_clock():
    """Check the real clock-to-native-lighting bridge and retain two outdoor frames."""
    import re
    requested=sys.argv[sys.argv.index('--daylight-clock')+1]
    start_game(prefix='daylight-start')
    def native_state():
        text=runtime_log_text();text=text[text.rfind('ANIMALTOWN GAME START'):]
        matches=re.findall(r'ANIMALTOWN DAYLIGHT minute (\d+) shadow (\d+) cloud (\d+) lamp (\d+) bytes (\d+)',text)
        if not matches:raise AssertionError('No native lighting state was published for the drawable scene')
        minute,shadow,cloud,lamp,storage=map(int,matches[-1])
        if shadow+lamp!=255:raise AssertionError('Sun shadows and lamps are not complementary')
        if not 0<storage<=512*1024:raise AssertionError('Shadow storage exceeded the existing budget')
        if requested=='12:00':
            if shadow!=255 or lamp!=0 or cloud<=0:raise AssertionError('Daytime clouds/shadows were not restored')
        elif requested=='20:00':
            if shadow!=0 or cloud!=0 or lamp!=255:raise AssertionError('Night must disable clouds/shadows and enable lamps')
        elif requested in ('06:30','18:00'):
            if not 0<shadow<255 or not 0<lamp<255 or cloud<=0:raise AssertionError('Dawn/dusk is missing its partial fade')
        else:raise ValueError('Use 06:30, 12:00, 18:00 or 20:00 for daylight captures')
        return {'minute':minute,'shadow':shadow,'cloud':cloud,'lamp':lamp,'shadow_storage_bytes':storage}
    first=native_state();screenshot('daylight-a.png');time.sleep(20);check_runtime_failure()
    second=native_state();screenshot('daylight-b.png')
    (root/'daylight-report.json').write_text(json.dumps({'requested_clock':requested,'first':first,'second':second,'passed':True},indent=2)+'\n')

def onboarding():
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    slot=int(sys.argv[sys.argv.index('--profile-slot')+1]) if '--profile-slot' in sys.argv else next((i for i in range(4) if not before or not before['profiles'][i]['created']),-1)
    if not 0<=slot<4:raise ValueError('Onboarding requires an unused profile slot 0..3')
    if before and before['profiles'][slot]['created']:raise AssertionError('Onboarding fixture slot is already occupied')
    original_digest=None
    def unchanged(label):
        if card_digest()!=original_digest or committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=before:
            raise AssertionError(label+' changed the card before Save & begin')
    def choose():
        wait_title();press('A');wait_profiles('onboarding-picker.png')
        for _ in range((slot-(before['active'] if before else 0))%4):press('D_RIGHT')
        press('A');wait_onboarding('name','onboarding-name-empty.png')
    choose()
    if committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=before:raise AssertionError('Entering Name saved a draft')
    # The first card mount may initialize a fresh emulator card. From Name
    # onward even uncommitted card bytes must remain unchanged until saving.
    original_digest=card_digest();press('START');wait_onboarding('name','onboarding-name-required.png');unchanged('Blank name validation')
    # Keep editing is the safe default; discarding is a separate explicit act.
    press('B');wait_onboarding('exit','onboarding-exit-default.png');press('A')
    wait_onboarding('name','onboarding-kept-editing.png');unchanged('Keep editing')
    press('A');screenshot('onboarding-name-entered.png');press('START')
    wait_onboarding('look','onboarding-look-fox.png')
    press('B');wait_onboarding('name','onboarding-back-to-name.png');press('START')
    wait_onboarding('look');press('D_RIGHT');screenshot('onboarding-look-bunny.png');press('A')
    loading=wait_onboarding('home','onboarding-home-first.png');unchanged('Home preview')
    # Discard after the real town renderer was constructed, exercising its
    # cleanup path as well as cancellation of the initial 2D identity screens.
    press('B');wait_onboarding('look','onboarding-back-to-look.png');press('B')
    wait_onboarding('name','onboarding-returned-name.png');press('B');wait_onboarding('exit')
    press('D_RIGHT');screenshot('onboarding-exit-discard.png');press('A')
    wait_title();screenshot('onboarding-discarded-to-title.png');unchanged('Discard after home preview')
    choose();press('A');press('START');wait_onboarding('look');press('D_RIGHT');press('A')
    loading=wait_onboarding('home','onboarding-home.png') or loading;press('A')
    wait_onboarding('review','onboarding-review.png');unchanged('Review')
    press('B');wait_onboarding('home','onboarding-review-back-home.png');press('A')
    wait_onboarding('review','onboarding-review-final.png');time.sleep(4);unchanged('Review idle')
    press('A');after=wait_saved_after(before);wait_onboarding('world','onboarding-gameplay.png')
    if after['active']!=slot or not after['profiles'][slot]['created'] or not after['plots'][slot]['placed']:
        raise AssertionError('Save & begin did not activate the completed resident and home')
    if after['profiles'][slot]['name']!='A' or after['profiles'][slot]['species']!=1:
        raise AssertionError('Name/look choices were not retained through back navigation and save')
    if before:
        for i in range(4):
            if i!=slot and (after['profiles'][i]!=before['profiles'][i] or after['plots'][i]!=before['plots'][i] or after['profile_payload_sha256'][i]!=before['profile_payload_sha256'][i]):
                raise AssertionError('Onboarding changed another resident or their private save data')
        if after['terrain_sha256']!=before['terrain_sha256']:raise AssertionError('Onboarding regenerated or changed the shared landscape')
    saved_digest=card_digest();time.sleep(8);screenshot('onboarding-gameplay-idle.png')
    if card_digest()!=saved_digest:raise AssertionError('Gameplay after Save & begin wrote another implicit save')
    # Reaching world also passes TownGame's hard cleanup invariant: no live
    # onboarding views, draft, picker portraits, or wordmark source bytes.
    report={'slot':slot,'before':before,'after':after,'default_keep_editing':True,'discard_to_title':True,
            'blank_name_rejected':True,'back_navigation':['look-name','home-look','review-home'],'review_did_not_save':True,
            'explicit_save_committed':True,'other_profiles_preserved':True,'loading_background_seen':loading,
            'gameplay_cleanup_invariant_passed':True,'gameplay_did_not_autosave':True}
    (root/'onboarding.json').write_text(json.dumps(report,indent=2))

def wait_title(modal=False):
    # Observe the actual menu, rather than sending input during either splash.
    deadline=time.monotonic()+180
    while time.monotonic()<deadline:
        check_runtime_failure()
        if p.poll() is not None:raise AssertionError('Emulator stopped before title')
        name='title-modal-wait.png' if modal else 'title-wait.png'
        screenshot(name);pixels=rgb(name)
        cream=color_count(pixels,(255,245,221));leaf=color_count(pixels,(53,105,84),tolerance=12)
        # The fitted title panel has substantially less exposed cream than the
        # full-size options/debug modal. Keep the existing modal gate intact.
        if modal:
            if cream>70000 and leaf<1000:return
        elif cream>50000 and leaf>6000 and color_count(pixels,(149,198,186))>15000:return
        time.sleep(2)
    raise AssertionError('Debug modal did not appear' if modal else 'Cream title/menu did not appear')

def title_frame_geometry(pixels,panel,menu,rows,step,status,wide=False):
    """Measure rendered panel, focused ribbon and glyph bounds, not just logs."""
    def sample(color,region=(0,0,800,600),tolerance=5):
        left,top,right,bottom=region
        return [(x,y) for y in range(max(0,top),min(600,bottom))
                for x in range(max(0,left),min(800,right))
                if fill_matches(pixels[(y*800+x)*3:(y*800+x)*3+3],color,tolerance)]
    def bounds(points,label):
        if not points:raise AssertionError('No rendered '+label+' pixels')
        return [min(x for x,y in points),min(y for x,y in points),
                max(x for x,y in points)+1,max(y for x,y in points)+1]
    def close(actual,expected,tolerance,label):
        if any(abs(a-b)>tolerance for a,b in zip(actual,expected)):
            raise AssertionError(label+' bounds '+str(actual)+' do not match '+str([round(v,2) for v in expected]))
    viewport=bounds(sample((149,198,186)),'mint backdrop')
    logical_width=480*(16/9 if wide else 4/3)
    sx=(viewport[2]-viewport[0])/logical_width;sy=(viewport[3]-viewport[1])/480
    margin=(logical_width-640)/2
    def project(rect):
        x,y,width,height=rect
        return [viewport[0]+(x+margin)*sx,viewport[1]+y*sy,
                viewport[0]+(x+margin+width)*sx,viewport[1]+(y+height)*sy]
    cream=sample((255,245,221));actual_panel=bounds(cream,'cream panel')
    # TitleArt adds a 9px side bulge; allow a few pixels for GX sampling and
    # the viewport's partly covered outermost pixel, not a different panel.
    expected_panel=project([panel[0]-9,panel[1],panel[2]+18,panel[3]])
    close(actual_panel,expected_panel,8,'Title panel')
    first_menu=project(menu)
    focus=bounds(sample((53,105,84),(viewport[0],int(first_menu[1])-3,viewport[2],viewport[3]),12),'focused menu')
    row=round(((focus[1]-viewport[1])/sy-menu[1])/step)
    if not 0<=row<rows:raise AssertionError('Focused ribbon is outside the title menu rows')
    expected_focus=project([menu[0],menu[1]+row*step,menu[2],menu[3]])
    close(focus,expected_focus,4,'Focused menu')
    # Exclude the rounded corners, where the panel behind the ribbon is also
    # cream, so the measured box contains only the selected label's ink.
    inset=max(2,int(22*sx))
    label=bounds(sample((255,245,221),(focus[0]+inset,focus[1]+1,focus[2]-inset,focus[3]-1)),'selected label')
    label_offset=[(label[i]+label[i+2]-focus[i]-focus[i+2])/2 for i in (0,1)]
    if any(abs(value)>4 for value in label_offset):
        raise AssertionError('Selected title label is not centered in its ribbon: '+str(label_offset))
    result={'viewport':viewport,'cream_pixels':len(cream),'panel_bounds':actual_panel,
            'expected_panel_bounds':[round(v,2) for v in expected_panel],
            'focused_row':row,'focus_bounds':focus,'label_bounds':label,'label_center_offset':label_offset}
    if status:
        footer=project([140,410,360,46]);zone=project([110,406,420,62])
        ink=sample((49,72,62),tuple(int(v) for v in zone));status_bounds=bounds(ink,'title status')
        if (status_bounds[0]<footer[0]-4 or status_bounds[1]<footer[1]-4
                or status_bounds[2]>footer[2]+4 or status_bounds[3]>footer[3]+4):
            raise AssertionError('Title status escapes its bounded footer: '+str(status_bounds))
        offset=[(status_bounds[i]+status_bounds[i+2]-footer[i]-footer[i+2])/2 for i in (0,1)]
        if any(abs(value)>4 for value in offset):
            raise AssertionError('Title status ink is not centered in its footer: '+str(offset))
        occupied=sorted({y for x,y in ink})
        bands=1+sum(b-a>3 for a,b in zip(occupied,occupied[1:]))
        result.update(status_bounds=status_bounds,status_center_offset=offset,status_line_bands=bands)
    return result

def title_logo():
    """Exercise full-colour branding and title reloads without creating a town."""
    import re
    card=root/'MemoryCardA.USA.raw'
    # An absent card returns 0, while an initialized empty card returns None.
    # Both represent the same uncommitted town state.
    before=committed_card_sequence(card,details=True) or None
    report={'scenario':'title-logo','disc':disc.name,'passed':False,
            'committed_town_before':before,'committed_town_after':before,
            'town_sequence_before':before['sequence'] if before else 0,
            'town_sequence_after':before['sequence'] if before else 0,
            'town_state_unchanged':True,'gameplay_started':False,
            'card_write_scope':'Committed town records only; initial card formatting and independent console/dev configuration writes are not asserted.',
            'screenshots':[],'warm_artwork':{},'title_logo_traces':[],'layouts':{}}

    def state():
        check_runtime_failure()
        text=runtime_log_text()
        failures=[line for line in text.splitlines()
                  if re.search(r'\bunhandled(?: managed)? exception\b|\bfatal error\b',line,re.IGNORECASE)]
        if failures:raise AssertionError('Branding runtime failure: '+failures[0])
        if p.poll() is not None:raise AssertionError('Emulator stopped during title-logo check')
        report['gameplay_started']='ANIMALTOWN GAME START' in text
        if report['gameplay_started']:raise AssertionError('Title-logo check unexpectedly constructed the town')
        after=committed_card_sequence(card,details=True) or None
        report['committed_town_after']=after
        report['town_sequence_after']=after['sequence'] if after else 0
        report['town_state_unchanged']=after==before
        if after!=before:raise AssertionError('Branding navigation/discard changed committed town state')
        return text

    def artwork(name,compact=False,capture=True):
        state()
        if capture:screenshot(name)
        pixels=rgb(name)
        # Keep the compact sample above the warm heading strip and away from
        # portraits/leaf flourishes. A missing or green-tinted logo cannot pass
        # using unrelated cream panels, menu fills or character artwork.
        bounds=(240,18,400,70) if compact else (100,20,540,255)
        left,top,right,bottom=bounds
        warm=0
        for y in range(top,bottom):
            for x in range(left,right):
                red,green,blue=pixels[(y*800+x)*3:(y*800+x)*3+3]
                warm+=red>=105 and red-green>=12 and green-blue>=10 and red-blue>=40
        minimum=120 if compact else 1200
        report['screenshots'].append(name)
        report['warm_artwork'][name]={'region':list(bounds),'pixels':warm,'minimum':minimum}
        if warm<minimum:raise AssertionError(name+' has no sufficiently visible full-colour acorn artwork')

    def setup_header(name):
        text=state()
        traces=re.findall(r'ANIMALTOWN SETUP HEADER (picker|[012]) logo bytes (\d+)',text)
        if not traces or int(traces[-1][1])!=0:raise AssertionError('Setup header retained title logo artwork')
        report['screenshots'].append(name)
        report.setdefault('setup_headers',{})[name]={'logo_bytes':int(traces[-1][1]),'stage':traces[-1][0]}

    def title(name,minimum_loads,rows=3,status=False):
        wait_title()
        expected={
            (3,False):([104,48,432,384],[140,73,360,180],[140,289,360,34]),
            (4,False):([104,28,432,424],[140,52,360,180],[140,268,360,34]),
            (3,True):([104,12,432,456],[140,49,360,180],[140,265,360,34]),
            (4,True):([104,12,432,456],[140,28,360,180],[140,244,360,34])
        }[(rows,status)]
        pattern=r'ANIMALTOWN TITLE LAYOUT rows (\d+) status (\d+) panel ([\d,]+) logo ([\d,]+) menu ([\d,]+) step (\d+)'
        deadline=time.monotonic()+20;failure='Missing matching title layout/load trace'
        while True:
            text=state()
            traces=[tuple(map(int,match)) for match in re.findall(r'ANIMALTOWN TITLE LOGO (\d+)x(\d+) bytes (\d+)',text)]
            report['title_logo_traces']=[{'width':w,'height':h,'bytes':size} for w,h,size in traces]
            if any(trace!=(512,256,262144) for trace in traces):
                raise AssertionError('Title loaded unexpected logo dimensions or texture bytes')
            layouts=re.findall(pattern,text)
            if len(traces)>=minimum_loads and layouts and tuple(map(int,layouts[-1][:2]))==(rows,int(status)):
                trace=layouts[-1];rects=tuple([int(value) for value in rect.split(',')] for rect in trace[2:5])
                if rects!=expected or int(trace[5])!=42:raise AssertionError('Title layout differs from the approved fitted geometry')
                screenshot(name)
                try:
                    options=options_card_files().get('options')
                    wide=bool(options and len(options)==16 and options[5])
                    metrics=title_frame_geometry(rgb(name),rects[0],rects[2],rows,42,status,wide)
                    report['layouts'][name]={'rows':rows,'status':bool(status),'panel':rects[0],
                                             'logo':rects[1],'menu':rects[2],'step':42,**metrics}
                    break
                except AssertionError as error:failure=str(error)
            if time.monotonic()>deadline:raise AssertionError(failure)
            time.sleep(.2)
        artwork(name,capture=False)
        return len(traces)

    try:
        if before:raise AssertionError('--title-logo requires a disposable card with no committed town')
        original_debug=debug_visibility();report['initial_debug_visibility']=original_debug
        if original_debug:
            # Reusing a disposable card may retain console preferences. Clear
            # only this toggle, then construct a fresh title without its toast.
            set_debug_visibility(False);wait_title()
            press('A');wait_profiles('title-logo-normalize-picker.png');press('B')
        initial_loads=title('title-logo.png',1)
        press('A');wait_profiles('title-logo-picker.png')
        setup_header('title-logo-picker.png')
        press('B');title('title-logo-after-picker.png',initial_loads+1)
        press('A');wait_profiles('title-logo-picker-reopened.png')
        setup_header('title-logo-picker-reopened.png')
        press('A');wait_onboarding('name','title-logo-name.png')
        setup_header('title-logo-name.png')
        press('B');wait_onboarding('exit','title-logo-discard.png')
        report['screenshots'].append('title-logo-discard.png');state()
        # D_RIGHT is the mapped pipe button, not the MAIN analog axis. Repeating
        # right only keeps Discard selected; A is a separate press/release edge.
        press('D_RIGHT');press('A')
        loads=title('title-logo-after-onboarding.png',initial_loads+2)
        report['discarded_new_villager']=True
        # Triple Z deliberately writes an independent console preference and
        # supplies real status text. It never commits a town or loads gameplay.
        set_debug_visibility(True)
        title('title-logo-status.png',loads,rows=4,status=True)
        press('A');wait_profiles('title-logo-debug-picker.png');press('B');wait_title()
        press('D_UP') # Select the long Debug label, including its descenders.
        loads=title('title-logo-debug.png',loads+1,rows=4)
        press('A');wait_title(modal=True);screenshot('title-logo-debug-modal.png')
        report['screenshots'].append('title-logo-debug-modal.png');state()
        press('B');wait_title();press('D_DOWN') # Wrap back to Start.
        set_debug_visibility(False)
        title('title-logo-status-normal.png',loads,status=True)
        press('A');wait_profiles('title-logo-normal-picker.png');press('B')
        final_loads=title('title-logo-return.png',loads+1)
        if original_debug:set_debug_visibility(True)
        state()
        report['title_reloads_observed']=final_loads-initial_loads
        report['debug_modal_opened']=True
        report['debug_visibility_restored']=debug_visibility()==original_debug
        if not report['debug_visibility_restored']:raise AssertionError('Title check did not restore the debug visibility preference')
        report['passed']=True
    except Exception as error:
        report['error']=str(error)
        raise
    finally:
        (root/'title-logo-runtime.log').write_text(runtime_log_text())
        (root/'title-logo-report.json').write_text(json.dumps(report,indent=2)+'\n')

def terrain_mip_pixels(pixels):
    """Fit adjacent mip colours through the actual daytime shadow shader."""
    # GroundMipPalette's CMPR endpoints use RGB565 bit replication in GX.
    words=(0x46AC,0xFF28)
    palette=tuple(((((v>>11)&31)<<3)|(((v>>11)&31)>>2),
                   (((v>>5)&63)<<2)|(((v>>5)&63)>>4),
                   ((v&31)<<3)|((v&31)>>2)) for v in words)
    # native-shadow-union-gx.inc blends toward S=(35,49,66), with at most
    # 66/255 shadow alpha. Scaling colours about RGB zero is not invariant:
    # a moving cloud changed the old Auto/reset mean despite identical bands.
    # At the scenario's fixed noon, environment tint is white and lamps are off.
    # Fit P-S = a*(C0-S) + b*(C1-C0), where 1-66/255 <= a <= 1,
    # 0 <= b <= a and b/a is the trilinear fraction between adjacent levels.
    shadow=(35,49,66);segments=[]
    for first,second in zip(palette,palette[1:]):
        a=tuple(c-s for c,s in zip(first,shadow));d=tuple(e-c for c,e in zip(first,second))
        aa=sum(c*c for c in a);dd=sum(c*c for c in d);ad=sum(c*e for c,e in zip(a,d))
        segments.append((a,d,aa,dd,ad,aa*dd-ad*ad))
    counts=[0]*len(palette);level_sum=error_sum=line_pixels=0;cache={}
    for y in range(44,430,2):
        for x in range(40,602,2):
            # Exclude the diagnostic legend and the ordinary floating clock.
            if (x>=350 and y<132) or (x<212 and y>=340):continue
            pixel=pixels[(y*800+x)*3:(y*800+x)*3+3]
            r,g,b=pixel
            if r>140 and b>125 and r-g>20 and b-g>8:
                line_pixels+=4
                continue
            if pixel not in cache:
                q=tuple(c-s for c,s in zip(pixel,shadow));best=None;error=9.0
                for level,(a,d,aa,dd,ad,det) in enumerate(segments):
                    qa=sum(c*e for c,e in zip(q,a));qd=sum(c*e for c,e in zip(q,d))
                    scale=(qa*dd-qd*ad)/det;blend=(qd*aa-qa*ad)/det
                    # Small coefficient margins and a 3-RGB vector residual
                    # accommodate GX's integer interpolation/rounding only.
                    if not 1-66/255-.015<=scale<=1.015:continue
                    fraction=blend/scale
                    if not -.015<=fraction<=1.015:continue
                    difference=sum((q[c]-scale*a[c]-blend*d[c])**2 for c in range(3))
                    if difference<error:best=level+min(1,max(0,fraction));error=difference
                cache[pixel]=(best,error)
            level,error=cache[pixel]
            if level is not None:
                counts[int(level+.5)]+=4;level_sum+=level*4;error_sum+=error*4
    total=sum(counts)
    # The histogram rounds to the nearest mip; the mean retains blends.
    return {'level_pixels':counts,'classified_pixels':total,'line_pixels':line_pixels,
            'mean_level':level_sum/max(1,total),'fit_rgb_rms':(error_sum/max(1,total))**.5}

def terrain_restoration_pixels(reference,current):
    """Compare fixed-noon ground art independent of moving shadow alpha."""
    shadow=(35,49,66);samples=matched=0;cache={}
    for y in range(140,338,2):
        for x in range(40,602,2):
            at=(y*800+x)*3;before=reference[at:at+3];after=current[at:at+3]
            r,g,b=before
            if g<=max(r+15,b+25,70):continue
            key=before+after
            if key not in cache:
                a=tuple(v-s for v,s in zip(before,shadow));b=tuple(v-s for v,s in zip(after,shadow))
                aa=sum(v*v for v in a);bb=sum(v*v for v in b);dot=sum(v*w for v,w in zip(a,b))
                # P-S=(1-alpha)*(C-S); its direction survives every union
                # shadow. GX rounding gets a small angular allowance. Stale
                # diagnostic green/yellow has a different direction.
                cache[key]=bool(aa and bb and dot>=0 and dot*dot>=.998*.998*aa*bb)
            samples+=1;matched+=cache[key]
    return {'ground_samples':samples,'matching_samples':matched,'matching_fraction':matched/max(1,samples),'minimum_cosine':.998}

def water_optics_pixels(first,second):
    """Measure interior water texture motion without assuming a ripple palette.

    Refraction introduces bed/stone colours outside the old two-colour affine
    ripple basis. Compare local RGB edge directions instead: uniform exposure
    and additive shading leave those directions unchanged, whereas translated
    ripples or refracted bed detail change the local pattern. The scenario also
    records an equally long frozen-motion control for changing cloud shadows.
    """
    def wet(data,x,y):
        r,g,b=data[(y*800+x)*3:(y*800+x)*3+3]
        return g>80 and b>80 and g>r+2 and b>r-8 and b>g-45
    samples=changed=textured=patterns=flips=0
    for y in range(60,308,2):
        for x in range(38,604,2):
            points=((x,y),(x-4,y),(x+4,y),(x,y-4),(x,y+4))
            if not all(wet(data,px,py) for data in (first,second) for px,py in points):continue
            patches=[[data[(py*800+px)*3:(py*800+px)*3+3] for px,py in points] for data in (first,second)]
            a,b=patches[0][0],patches[1][0];samples+=1
            if max(abs(a[c]-b[c]) for c in range(3))>=4:changed+=1
            edge0=[p[c]-a[c] for p in patches[0][1:] for c in range(3)]
            edge1=[p[c]-b[c] for p in patches[1][1:] for c in range(3)]
            aa=sum(v*v for v in edge0);bb=sum(v*v for v in edge1)
            if aa<144 or bb<144:continue
            textured+=1;dot=sum(v*w for v,w in zip(edge0,edge1))
            if dot<0 or dot*dot<.85*.85*aa*bb:patterns+=1
            for i in range(4):
                da=sum(edge0[i*3:i*3+3]);db=sum(edge1[i*3:i*3+3])
                if abs(da)>=18 and abs(db)>=18 and da*db<0:
                    flips+=1;break
    return {'interior_samples':samples,'rgb_changed_samples':changed,
            'textured_samples':textured,'local_pattern_changed_samples':patterns,
            'local_edge_sign_changes':flips,'region':[38,60,604,308],
            'edge_measure':'local RGB direction','minimum_edge_cosine':.85}

def water_material_pixels(first,second,shadowed=False,optics=False):
    """Measure moving crest colour/edges, with shoreline and HUD excluded."""
    if optics:return water_optics_pixels(first,second)
    def wet(data,x,y):
        r,g,b=data[(y*800+x)*3:(y*800+x)*3+3]
        return g>85 and b>85 and g>r+8 and b>r+8
    origin=(35,49,66) if shadowed else (0,0,0);cache={}
    def chromaticity(pixel):
        if pixel not in cache:
            total=max(1,sum(pixel)-sum(origin))
            cache[pixel]=tuple((pixel[c]-origin[c])/total for c in range(3))
        return cache[pixel]
    samples=chroma=edge_flips=0
    for y in range(60,308,2):
        for x in range(38,604,2):
            # Require a water-only neighbourhood in both frames. This avoids
            # moving actors, shore foam and the boundary with solid ground.
            neighbours=((x,y),(x-3,y),(x+3,y),(x,y-3),(x,y+3))
            if not all(wet(data,px,py) for data in (first,second) for px,py in neighbours):continue
            at=(y*800+x)*3;a=first[at:at+3];b=second[at:at+3]
            ca=chromaticity(a);cb=chromaticity(b);samples+=1
            # Default archived water has no cloud-shadow shader stages; the
            # native probe below checks that actual 4..6-stage/2-texgen path.
            # Legacy river water has the same affine shadow blend as ground:
            # normalizing RGB-S cancels that blend. Its spatial colour edges
            # also use this normalization so moving clouds cannot prove waves.
            if max(abs(ca[c]-cb[c]) for c in range(3))>.018:chroma+=1
            for dx,dy in ((3,0),(0,3)):
                offset=((y+dy)*800+x+dx)*3
                if shadowed:
                    na=chromaticity(first[offset:offset+3]);nb=chromaticity(second[offset:offset+3])
                    flip=any(abs(na[c]-ca[c])>=.018 and abs(nb[c]-cb[c])>=.018
                             and (na[c]-ca[c])*(nb[c]-cb[c])<0 for c in range(3))
                else:
                    da=sum(first[offset:offset+3])-sum(a);db=sum(second[offset:offset+3])-sum(b)
                    flip=abs(da)>=24 and abs(db)>=24 and da*db<0
                if flip:
                    edge_flips+=1;break
    return {'interior_samples':samples,'changed_chromaticity_samples':chroma,
            'crest_edge_sign_changes':edge_flips,'region':[38,60,604,308],
            'shadow_invariant':shadowed,'edge_measure':'chromaticity' if shadowed else 'intensity'}

def terrain_mip_applied(text,enabled,mips,distance):
    import re
    matches=re.findall(r'GROUND MIP BANDS enabled (\d) mips (\d) distance (\d+) size (\d+)x(\d+) levels (\d+)',text)
    if not matches:return None
    values=tuple(map(int,matches[-1]))
    # Both native preview and full source packages carry the same two-level
    # sampler contract. Optional full uploads yield while memory is pressured.
    if values[:3]!=(enabled,mips,distance) or values[3:5] not in ((64,64),(256,256)):return None
    if values[5]!=2:raise AssertionError('Unexpected diagnostic ground mip chain: '+str(values))
    return {'enabled':bool(enabled),'mipmaps':bool(mips),'distance':distance,
            'width':values[3],'height':values[4],'levels':values[5]}

def water_draw_state(log_text,scene,legacy=False,require_optics=False):
    """Validate one actual water draw and its same-frame optics evidence.

    Return None for an incomplete pair of console lines or an older renderer
    path during a mode switch. Malformed/current invalid evidence must fail.
    """
    import re
    water_pattern=(r'ANIMALTOWN WATER DRAW scene=(\d+) frame=(\d+) tev=(\d+) indirect=(\d+) strength=(\d+) '
                   r'tex0=(\d+)x(\d+) tex1=(\d+)x(\d+) uv0=([-\d,]+) uv1=([-\d,]+) '
                   r'layers=(\d+):(\d+),(\d+):(\d+) texgens=(\d+) matrices=(\d+),(\d+) '
                   r'k0=([0-9a-f]{8}) k1=([0-9a-f]{8}) blend=(\d+):(\d+):(\d+) '
                   r'depth=(\d+):(\d+):(\d+) layerstages=(\d+),(\d+)')
    matches=list(re.finditer(water_pattern,log_text))
    if not matches or int(matches[-1][1])!=scene:return None
    match=matches[-1];fields=match.groups()
    state={'scene':int(fields[0]),'frame':int(fields[1]),'tev_stages':int(fields[2]),
           'indirect_stages':int(fields[3]),'strength':int(fields[4]),
           'tex0':[int(fields[5]),int(fields[6])],'tex1':[int(fields[7]),int(fields[8])],
           'uv0':list(map(int,fields[9].split(','))),'uv1':list(map(int,fields[10].split(','))),
           'layers':[[int(fields[11]),int(fields[12])],[int(fields[13]),int(fields[14])]],
           'texgens':int(fields[15]),'matrices':[int(fields[16]),int(fields[17])],
           'foam_constants':[fields[18],fields[19]],
           'blend':list(map(int,fields[20:23])),'depth':list(map(int,fields[23:26])),
           'layer_stages':list(map(int,fields[26:28]))}
    shallow=scene in (1,2,3)
    prefix='ANIMALTOWN WATER OPTICS scene='+str(scene)+' frame='+str(state['frame'])+' '
    optics_lines=[line[line.index(prefix):] for line in log_text[match.end():].splitlines(keepends=True) if prefix in line]
    optics=None
    if optics_lines:
        # Console output can be read between writes. Wait for its newline so
        # an unfinished serial record cannot fail or establish acceptance.
        if not optics_lines[0].endswith(('\n','\r')):return None
        raw=dict(re.findall(r'(\w+)=([^\s]+)',optics_lines[0]))
        required={'scene','frame','ready','scope','part','captured','valid','size','source','bytes','copies',
                  'draws','mask','base','failed','sun','environment','textures','indirect','stages','generators','wave0','wave1','controls'}
        if set(raw)!=required:raise AssertionError('Incomplete water optics evidence: '+optics_lines[0])
        def integers(name,separator=','):return list(map(int,raw[name].split(separator)))
        try:
            optics={key:int(raw[key]) for key in ('ready','scope','part','captured','valid','bytes','copies','mask','base','failed','sun')}
            optics.update(environment=int(raw['environment'],16),size=integers('size','x'),source=integers('source','x'),draws=integers('draws'),
                          textures=[list(map(int,re.split('[x:]',value))) for value in raw['textures'].split(',')],
                          indirect=[list(map(int,value.split(':'))) for value in raw['indirect'].split(',')],
                          stages=[list(map(int,value.split(':'))) for value in raw['stages'].split(',')],
                          generators=[list(map(int,value.split(':'))) for value in raw['generators'].split(',')],
                          wave0=integers('wave0'),wave1=integers('wave1'),controls=integers('controls'))
        except ValueError as error:raise AssertionError('Malformed water optics evidence: '+optics_lines[0]) from error
        if (not shallow or optics['scope']!=2 or optics['ready'] not in (0,1) or optics['valid'] not in (0,1)
                or optics['mask'] not in (0,3,7) or not 0<=optics['sun']<=255 or not 0<=optics['environment']<=0xffffff
                or any(optics[key]<0 for key in ('part','captured','bytes','copies','failed'))):
            raise AssertionError('Unexpected water optics scope/capabilities: '+str(optics))
        if len(optics['draws'])!=3 or any(value<0 for value in optics['draws']) or optics['draws'][1]>optics['draws'][0]:
            raise AssertionError('Invalid water optics draw accounting: '+str(optics))
    elif shallow and (state['indirect_stages']!=1 or require_optics):
        if 'ANIMALTOWN WATER OPTICS unavailable;' in log_text:
            raise AssertionError('Water optics allocation failed on the supported smoke display; fallback is visible but feature acceptance is incomplete')
        return None

    bumped=bool(optics and optics['mask'])
    refracted=bool(bumped and optics['mask']&4)
    if bumped:
        shaded=optics['base']==8;archived=optics['base'] in (4,5)
        expected_stages=optics['base']+(8 if refracted else 3)
        if (not optics['ready'] or not (archived or shaded) or state['tev_stages']!=expected_stages
                or state['indirect_stages']!=3 or state['texgens']!=(7 if refracted else 6)
                or (archived and optics['base']!=(4 if optics['environment']==0xffffff else 5))):
            raise AssertionError('Actual GX water optics stage budget changed: '+str(state)+' '+str(optics))
        if optics['indirect']!=[[3,3],[4,3]] or optics['textures'][:2]!=[[64,64,3],[64,64,3]]:
            raise AssertionError('Water slopes/lighting lookup are not bound to the expected GX resources: '+str(optics))
        expected_orders=[[5,256],[5,4],[255,255]]+([[6,5],[6,6],[255,255],[6,5],[255,255]] if refracted else [[255,255]]*5)
        expected_generators=[[1,4,36],[1,4,39],[1,0,42]]+([[0,0,45]] if refracted else [])
        if (optics['stages']!=expected_orders or len(optics['generators'])!=4
                or optics['generators'][:len(expected_generators)]!=expected_generators):
            raise AssertionError('Actual GX water optics coordinate/stage bindings changed: '+str(optics))
        for key,scale in (('wave0',2200000),('wave1',3100000)):
            matrix=optics[key]
            if len(matrix)!=6 or any(abs(actual-expected)>3 for actual,expected in zip(matrix[:4],(scale,0,0,scale))):
                raise AssertionError('Actual GX water slope matrix has the wrong scale: '+str(optics))
            if any(not 0<=value<=1000000 for value in matrix[4:]):
                raise AssertionError('Water slope translation escaped its periodic range: '+str(optics))
        if len(optics['controls'])!=4 or any(abs(a-b)>1 for a,b in zip(optics['controls'],(140000,1500000,180000,100000))):
            raise AssertionError('Water optics controls do not match the reviewed material defaults: '+str(optics))
        if refracted:
            width,height=optics['size'] if len(optics['size'])==2 else (0,0)
            if (not optics['valid'] or optics['part']<=0 or optics['captured']!=optics['part'] or optics['copies']<=0
                    or optics['draws'][1]<=0 or width<=0 or height<=0 or width%4 or height%4
                    or width>320 or height>264 or optics['source']!=[2*width,2*height]
                    or optics['textures'][2:]!=[[width,height,4],[width,height,4]]
                    or optics['bytes']!=16384+width*height*2):
                raise AssertionError('Refraction did not use a fresh, correctly sized current-part capture: '+str(optics))
    else:
        archived=4<=state['tev_stages']<=6 and state['texgens']==2
        shaded=state['tev_stages']==8 and state['texgens']==3
        if not (archived or shaded) or state['indirect_stages']!=1:
            raise AssertionError('Fallback water lost its established GX program: '+str(state))
    if shaded!=legacy:return None
    if require_optics and shallow and not refracted:
        raise AssertionError('Water rendered its safe fallback; supported-display acceptance requires bump lighting and a current-part refraction capture: '+str(optics))
    if (state['tex0']!=[64,64] or state['tex1']!=[64,64]
            or state['layers']!=[[0,0],[1,0]] or state['matrices']!=[30,33]):
        raise AssertionError('Actual GX water draw lost its original ripple bindings: '+str(state))
    state['material_path']='legacy' if shaded else 'archived'
    expected_colors=['367f91ff','d3f2ecff'] if shallow else ['c4e4deff','e5f5edff']
    if bumped:
        # K0/K1 RGB still shade the original ripple layers. Their previously
        # unused alpha channels now hold independent specular/sheen gains.
        gains=[int(optics['controls'][2]*optics['sun']/1000000+.5),int(optics['controls'][3]*255/1000000+.5)]
        expected_colors=[color[:6]+format(gain,'02x') for color,gain in zip(expected_colors,gains)]
    if state['foam_constants']!=expected_colors:
        raise AssertionError('Actual GX water draw overwrote a ripple colour constant: '+str(state))
    if state['strength']!=(24 if shallow else 76) or state['layer_stages']!=([1,3] if shallow else [1,2]):
        raise AssertionError('Actual GX water draw selected the wrong surface program: '+str(state))
    expected_blend=[0,1,0] if refracted else [1,4,5] if shallow else [0,4,5]
    if state['blend']!=expected_blend or state['depth']!=([1,3,0] if shallow else [1,3,1]):
        raise AssertionError('Actual GX water blend/depth state is incorrect: '+str(state))
    state['surface']='shallow' if shallow else 'ocean'
    for key,scale in (('uv0',2400000 if shallow else 1350000),('uv1',2800000 if shallow else 1000000)):
        matrix=state[key]
        if len(matrix)!=6 or any(abs(actual-expected)>3 for actual,expected in zip(matrix[:4],(scale,0,0,scale))):
            raise AssertionError('Actual GX water UV matrix has the wrong scale: '+str(state))
    if optics:state['optics']=optics
    return state

def terrain_materials():
    """Observe real menu controls, native sampler output and water motion."""
    import re
    card=root/'MemoryCardA.USA.raw'
    report={'scenario':'terrain-materials','disc':disc.name,'passed':False,
            'screenshots':[],'mip_states':{},'water':{},'visual_failures':[],
            'physical_hardware':False}
    prefix='terrain-materials-'
    option_count=testing_menu_count()
    mipmaps_row,distance_row,bands_row=(testing_menu_row(option) for option in (22,23,34))
    def visual_assert(condition,message):
        # Independent image failures must not hide later restoration/water
        # evidence. Native state, menu navigation and runtime failures remain
        # immediate errors; no visual failure can produce a passing report.
        if not condition:report['visual_failures'].append(message)
    def alive():
        check_runtime_failure()
        if p.poll() is not None:raise AssertionError('Emulator stopped during terrain material capture')
        if re.search(r'\bunhandled(?: managed)? exception\b|\bfatal error\b',runtime_log_text(),re.IGNORECASE):
            raise AssertionError('Unhandled runtime failure during terrain material capture')
    def capture(name):
        alive();filename=prefix+name+'.png';screenshot(filename)
        if filename not in report['screenshots']:report['screenshots'].append(filename)
        return rgb(filename)
    def chord(button,with_z=False):
        buttons=['L','R']+(['Z'] if with_z else [])+[button]
        # One pipe update presents the complete chord to the guest. In
        # particular, Z must not first toggle the unrelated frame profiler.
        os.write(controller,(''.join('PRESS '+value+'\n' for value in buttons)).encode())
        time.sleep(1)
        os.write(controller,(''.join('RELEASE '+value+'\n' for value in reversed(buttons))).encode())
        time.sleep(1);alive()
    def menu_frame(name,row):
        deadline=time.monotonic()+90;y=131+(row%9)*24
        while time.monotonic()<deadline:
            pixels=capture(name)
            if region_color_count(pixels,(221,231,188),100,y,540,y+21)>5000:
                return b''.join(pixels[(py*800+85)*3:(py*800+575)*3] for py in range(y,y+21))
            time.sleep(.5)
        raise AssertionError('Debug menu did not focus row '+str(row))
    def menu(row,name):
        press('START');press('R')
        direction='D_DOWN' if row<=option_count//2 else 'D_UP'
        for _ in range(min(row,option_count-row)):press(direction)
        return menu_frame(name,row)
    def applied(enabled,mips,distance):
        return terrain_mip_applied(runtime_log_text(),enabled,mips,distance)
    def mip_frame(name,enabled,mips,distance,check=None):
        deadline=time.monotonic()+90;failure='No matching applied ground-mip state';last=None
        while time.monotonic()<deadline:
            alive();state=applied(enabled,mips,distance)
            if state:
                pixels=capture(name)
                if not onboarding_signature(pixels,'world'):
                    failure='World frame not visible after ground-mip change';time.sleep(.5);continue
                metrics=terrain_mip_pixels(pixels);last=metrics
                report['mip_states'][name]={**state,**metrics}
                try:
                    if check:check(metrics)
                    return metrics
                except AssertionError as error:failure=str(error)
            time.sleep(.5)
        if last is not None:
            visual_assert(False,name+': '+failure);return last
        raise AssertionError(failure)
    def visible_bands(metrics):
        if metrics['classified_pixels']<6000 or sum(count>250 for count in metrics['level_pixels'])<2:
            raise AssertionError('Actual terrain does not show both detail levels')
        if metrics['line_pixels']<32:raise AssertionError('No visible line at the mip switch')
    def base_only(metrics):
        if metrics['classified_pixels']<6000 or metrics['level_pixels'][0]<metrics['classified_pixels']*.88:
            raise AssertionError('Disabling mipmaps did not collapse diagnostic terrain to level zero')
    def water_probe(scene,legacy=False,require_optics=True):
        return water_draw_state(runtime_log_text(),scene,legacy,require_optics=require_optics and scene in (1,2,3))
    def water_wait(scene,predicate,legacy=False,require_optics=True):
        deadline=time.monotonic()+180
        while time.monotonic()<deadline:
            alive();state=water_probe(scene,legacy,require_optics)
            if state and predicate(state):return state
            time.sleep(.2)
        raise AssertionError('No matching actual GX water draw/progression for scene '+str(scene)
                             +' ('+('legacy' if legacy else 'archived')+')')
    def water_advance(scene):
        # Input is ignored while the new terrain view loads. A world-shaped
        # screenshot can still belong to the previous camera, so acknowledge
        # each shortcut with an actual draw before sending the next one.
        chord('D_LEFT')
        return water_wait(scene,lambda state:True,require_optics=False)
    def uv_progress(before,after):
        layers=[(before[key],after[key]) for key in ('uv0','uv1')]
        if 'optics' in before and before['optics']['mask']:
            layers.extend((before['optics'][key],after['optics'][key]) for key in ('wave0','wave1'))
        return [[min(abs(end[at]-start[at])%1000000,
                     1000000-abs(end[at]-start[at])%1000000)
                 for at in (4,5)] for start,end in layers]
    def water_pair(name,scene,moving=True,frames=60,legacy=False):
        previous=water_probe(scene,legacy);after=previous['frame'] if previous else -1
        start=water_wait(scene,lambda state:state['frame']>after,legacy)
        first=capture(name+'-a')
        def advanced(state):
            if state['frame']<start['frame']+frames:return False
            if not moving:return True
            return all(max(layer)>=40000 for layer in uv_progress(start,state))
        end=water_wait(scene,advanced,legacy);second=capture(name+'-b')
        if not moving and any(any(value for value in layer) for layer in uv_progress(start,end)):
            raise AssertionError('Freezing landscape motion did not stop actual ripple and slope UV matrices')
        bumped=bool(start.get('optics',{}).get('mask'))
        if bumped:
            if (end['optics']['copies']<=start['optics']['copies']
                    or end['optics']['part']<=start['optics']['part']
                    or end['optics']['draws'][1]<=start['optics']['draws'][1]):
                raise AssertionError('Water reused an old scene capture instead of capturing/drawing current frame parts')
        metrics=water_material_pixels(first,second,shadowed=legacy,optics=bumped)
        visual_assert(metrics['interior_samples']>=250,'No sufficient water interior in '+name)
        metrics.update(native_start=start,native_end=end,rendered_frames=end['frame']-start['frame'],
                       uv_translation_delta=uv_progress(start,end),motion_expected=moving)
        return metrics
    def moving_water(name,metrics,frozen):
        if 'local_pattern_changed_samples' in metrics:
            visual_assert(metrics['rgb_changed_samples']>=35,
                          name+' has no visible interior colour motion')
            visual_assert(metrics['local_pattern_changed_samples']>=max(25,frozen['local_pattern_changed_samples']*3)
                          and metrics['local_edge_sign_changes']>=max(10,frozen['local_edge_sign_changes']*2),
                          name+' does not move water/bed texture detail more strongly than its frozen control')
        else:
            visual_assert(metrics['changed_chromaticity_samples']>=max(35,frozen['changed_chromaticity_samples']*3)
                          and metrics['crest_edge_sign_changes']>=15,
                          name+' is not visibly stronger than the frozen water control')
    try:
        if committed_card_sequence(card):raise AssertionError('--terrain-materials requires a disposable card without a town')
        set_debug_visibility(True);start_game(True,prefix+'start');time.sleep(2)
        before=card_digest();town_before=committed_card_sequence(card,details=True)
        report['committed_town_before']=town_before
        chord('START');wait_onboarding('world',prefix+'ramp-ready.png')
        normal=mip_frame('normal',0,1,0);normal_art=rgb(prefix+'normal.png')
        off_label=menu(bands_row,'menu-off');press('A');on_label=menu_frame('menu-on',bands_row)
        if on_label==off_label:raise AssertionError('Ground mip bands menu label did not change')
        press('B');automatic=mip_frame('mips-auto',1,1,0,visible_bands)
        auto_label=menu(distance_row,'distance-auto');press('D_RIGHT');one_label=menu_frame('distance-one',distance_row)
        press('B');one=mip_frame('mips-one',1,1,1)
        menu(distance_row,'distance-one-return')
        for _ in range(9):press('D_RIGHT')
        ten_label=menu_frame('distance-ten',distance_row);press('B');ten=mip_frame('mips-ten',1,1,10)
        if len({auto_label,one_label,ten_label})!=3:raise AssertionError('Distance control did not display default, 1 and 10 distinctly')
        visual_assert(one['mean_level']>ten['mean_level']+.1,'Increasing mip distance did not retain a visibly finer sampled mip level')
        menu(distance_row,'distance-ten-return');press('X');reset_label=menu_frame('distance-reset',distance_row)
        if reset_label!=auto_label:raise AssertionError('X did not restore the default distance label')
        press('B');reset=mip_frame('mips-reset',1,1,0,visible_bands)
        visual_assert(abs(reset['mean_level']-automatic['mean_level'])<=.25,'Auto reset did not restore the diagnostic mip distribution')
        menu(mipmaps_row,'mipmaps-on');press('A');menu_frame('mipmaps-off',mipmaps_row);press('B')
        mip_frame('mips-disabled',1,0,0,base_only)
        menu(mipmaps_row,'mipmaps-off-return');press('A');press('D_DOWN');press('D_DOWN')
        menu_frame('menu-on-return',bands_row);press('A');restored_label=menu_frame('menu-restored',bands_row)
        if restored_label!=off_label:raise AssertionError('Ground mip bands did not return to Off')
        press('B')
        def restored_art(metrics):
            # Ordinary greens can fall inside the diagnostic palette fit as
            # clouds move. Verify the original per-pixel art instead of a
            # changing count of coincident colours; native Off is checked too.
            match=terrain_restoration_pixels(normal_art,rgb(prefix+'restored.png'));metrics['art_match']=match
            if match['ground_samples']<1000 or match['matching_fraction']<.95:
                raise AssertionError('Original ground art did not return after Ground mip bands was disabled')
        mip_frame('restored',0,1,0,restored_art)
        # WaterInspection is a read-only camera tour; it hides the player and
        # restores logical coordinates after every draw, keeping pairs fixed.
        water_advance(1);wait_onboarding('world',prefix+'river-ready.png');time.sleep(3)
        animated=water_pair('water-river',1)
        chord('D_DOWN',with_z=True);time.sleep(2)
        frozen=water_pair('water-river-frozen',1,moving=False,frames=animated['rendered_frames'])
        chord('D_DOWN',with_z=True);time.sleep(2);resumed=water_pair('water-river-resumed',1)
        report['water']['river']={'animated':animated,'frozen_control':frozen,'resumed':resumed}
        for name,metrics in (('river animation',animated),('resumed river animation',resumed)):
            moving_water(name,metrics,frozen)
        # The legacy river path adds the shadow/light footer that used to
        # overwrite WaterWarp's second foam colour. RecreateMeshes is a
        # session-only chord, not the unrelated in-game tree-noise menu row.
        chord('X',with_z=True);wait_onboarding('world',prefix+'legacy-river-ready.png')
        legacy_animated=water_pair('water-river-legacy',1,legacy=True)
        chord('D_DOWN',with_z=True);time.sleep(2)
        legacy_frozen=water_pair('water-river-legacy-frozen',1,moving=False,
                                 frames=legacy_animated['rendered_frames'],legacy=True)
        chord('D_DOWN',with_z=True);time.sleep(2)
        legacy_resumed=water_pair('water-river-legacy-resumed',1,legacy=True)
        report['water']['legacy_river']={'animated':legacy_animated,'frozen_control':legacy_frozen,
                                         'resumed':legacy_resumed,'switch_control':'L+R+Z+X'}
        for name,metrics in (('legacy river animation',legacy_animated),('resumed legacy river animation',legacy_resumed)):
            moving_water(name,metrics,legacy_frozen)
        last_legacy=legacy_resumed['native_end']['frame']
        chord('X',with_z=True);wait_onboarding('world',prefix+'archive-river-restored.png')
        report['water']['legacy_river']['archived_restored']=water_wait(1,lambda state:state['frame']>last_legacy)
        capture('water-river-archive-restored')
        for scene in (2,3,4):water_advance(scene)
        wait_onboarding('world',prefix+'coast-ready.png');time.sleep(3)
        coast=water_pair('water-coast',4);report['water']['coast']=coast
        visual_assert(coast['changed_chromaticity_samples']>=35 and coast['crest_edge_sign_changes']>=15,
                      'Coastal water has no visibly moving crest pattern')
        menu(bands_row,'water-menu');press('B');capture('water-menu-return');press('B')
        alive();report['committed_town_after']=committed_card_sequence(card,details=True)
        report['card_unchanged']=card_digest()==before
        report['town_unchanged']=report['committed_town_after']==town_before
        if not report['card_unchanged'] or not report['town_unchanged']:
            raise AssertionError('Session-only rendering controls changed saved card data')
        if report['visual_failures']:raise AssertionError('; '.join(report['visual_failures']))
        report['passed']=True
    except Exception as error:
        report['error']=str(error)
        # Retain the actual frame even if the first native probe is missing
        # or invalid, distinguishing absent water from failed animation.
        try:
            filename=prefix+'failure.png';screenshot(filename)
            if filename not in report['screenshots']:report['screenshots'].append(filename)
        except Exception:pass
        raise
    finally:
        (root/(prefix+'runtime.log')).write_text(runtime_log_text())
        (root/(prefix+'report.json')).write_text(json.dumps(report,indent=2)+'\n')

def title_menu():
    set_debug_visibility(True)
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    active=before['active'] if before else 0
    creating=not before or not before['profiles'][active]['created']
    screenshot('title-dots-a.png');time.sleep(3);screenshot('title-dots-b.png')
    a,b=rgb('title-dots-a.png'),rgb('title-dots-b.png')
    # Background-only pixels must change while the game remains deferred.
    mint=lambda pixels,i: all(abs(pixels[i+c]-value)<=5 for c,value in enumerate((149,198,186)))
    changed=sum(a[i:i+3]!=b[i:i+3] and (mint(a,i) or mint(b,i)) for i in range(0,len(a),3))
    if changed<100:raise AssertionError('Title background is not scrolling')
    press('D_UP');press('A');wait_title(modal=True);screenshot('title-debug-modal.png')
    press('D_RIGHT');screenshot('title-debug-edited.png')
    # Exercise paging, wraparound and slider rows, then return to row zero.
    press('D_UP');screenshot('title-debug-last-page.png');press('D_DOWN')
    press('B');wait_title();screenshot('title-dev-config-saved.png')
    press('A');wait_title(modal=True);screenshot('title-debug-reopened.png');press('B');wait_title()
    press('D_DOWN')
    # Start opens the shared-town resident picker before game asset loading.
    press('A');wait_profiles('title-villager-picker.png')
    if creating:
        press('A');wait_onboarding('name','title-onboarding-name.png');press('A');press('START')
        wait_onboarding('look','title-onboarding-look.png')
    # Capture during loading, not after a multi-second press/release helper.
    os.write(controller,b'PRESS A\n');time.sleep(.2)
    os.write(controller,b'RELEASE A\n')
    # Startup contains bounded synchronous card/asset reads. Observe distinct
    # sprout poses over time instead of assuming the first three captures span
    # three guest frames (software Dolphin can still be inside one read).
    dark=lambda pixels: frozenset(i for i in range(0,len(pixels),3) if all(abs(pixels[i+c]-value)<=5 for c,value in enumerate((49,72,62))))
    loading=[];poses=set();started=time.monotonic();deadline=started+90
    while len(loading)<3 and time.monotonic()<deadline:
        name='title-start-loading-'+str(len(loading))+'.png';screenshot(name);pixels=rgb(name)
        if color_count(pixels,(149,198,186))>150000 and color_count(pixels,(255,245,221))<100:
            pose=dark(pixels)
            if pose and pose not in poses:poses.add(pose);loading.append(pixels)
        if p.poll() is not None:raise AssertionError('Emulator stopped during loading animation')
        time.sleep(.35)
    if len(loading)<3:raise AssertionError('Start did not present three animated sprout poses over the plain dot background')
    print(f'Observed three distinct loading poses after {time.monotonic()-started:.1f}s',flush=True)
    deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        screenshot('title-start-game.png');pixels=rgb('title-start-game.png')
        if color_count(pixels,(149,198,186))<150000:break
        if p.poll() is not None:raise AssertionError('Emulator stopped during game loading')
        time.sleep(3)
    else:raise AssertionError('Game never left loading screen')
    if creating:
        press('A');wait_onboarding('review','title-onboarding-review.png');press('A')
        wait_saved_after(before);wait_onboarding('world','title-start-game.png')
    (root/'title-menu.txt').write_text(f'Diagonal dot pixels changed: {changed}\nOpened debug modal, edited and paged settings, saved/closed/reopened, then selected Start.\nThree loading frames contained no cream title/menu and an animated sprout; game then became visible.\n'+('Completed Name, Look, Home and explicit Save & begin.\n' if creating else ''))

def archive_records(payload,publication=False):
    """Read the SDK's verified DTR1 checkpoints from a consolidated card item."""
    import struct
    block=8192
    def hash_bytes(data):
        h=2166136261
        for value in data:h=((h^value)*16777619)&0xffffffff
        return h
    if len(payload)%block or not 6<=len(payload)//block<=2048:raise AssertionError('Invalid archive size')
    candidates=[]
    for bank in (0,1):
        root=payload[bank*block:(bank+1)*block]
        magic,version,sequence,blocks=struct.unpack_from('<4I',root)
        if magic!=0x31525444 or version!=1 or not 1<=sequence<=0x7fffffff or blocks!=len(payload)//block or hash_bytes(root[:-4])!=struct.unpack_from('<I',root,block-4)[0]:continue
        records={};ranges=[]
        try:
            for i in range(64):
                at=32+i*48
                if root[at]==0:continue
                raw=root[at:at+32];end=raw.find(b'\0')
                if not 1<=end<=31 or any(c<32 or c>126 for c in raw[:end]):raise ValueError('Invalid record name')
                name=raw[:end].decode('ascii')
                if name in records:raise ValueError('Duplicate record')
                page,offset,length,checksum=struct.unpack_from('<4I',root,at+32)
                if not 2<=page<blocks or offset>=block or length>(blocks-page)*block-offset:raise ValueError('Invalid record extent')
                start=page*block+offset;stop=start+max(1,length)
                if any(start<b and a<stop for a,b in ranges):raise ValueError('Overlapping records')
                value=payload[start:start+length]
                if hash_bytes(value)!=checksum:raise ValueError('Damaged record')
                ranges.append((start,stop));records[name]=value
            phase=records.get('AT-Archive',b'\0')[0];epoch=0
            if struct.unpack_from('<I',root,16)[0]==0x32435241:
                epoch,growth,phase=struct.unpack_from('<3I',root,20)
                if phase not in (1,2,3) or epoch>0x7fffffff or phase==3 and not 32<=growth<=2048:raise ValueError('Invalid archive publication')
            candidates.append((sequence,records,phase,epoch))
        except (ValueError,struct.error):continue
    if not candidates:raise AssertionError('No verified archive checkpoint')
    selected=max(candidates,key=lambda c:c[0])
    return selected[1:] if publication else selected[1]

def options_card_files(path=None):
    """Current committed directory/BAT only; freed sectors are not live files."""
    import struct
    if path is None:path=root/'MemoryCardA.USA.raw'
    if not path.exists():return {}
    card=path.read_bytes();sector=8192
    def word(at):return struct.unpack_from('>H',card,at)[0]
    def valid(block,start,end,checksum):
        values=struct.unpack('>'+str((end-start)//2)+'H',card[block*sector+start:block*sector+end])
        a=sum(values)&65535;b=sum(v^65535 for v in values)&65535
        return (0 if a==65535 else a,0 if b==65535 else b)==(word(block*sector+checksum),word(block*sector+checksum+2))
    def newest(blocks,offset):
        result=blocks[0]
        for block in blocks[1:]:
            if 0<((word(block*sector+offset)-word(result*sector+offset))&65535)<32768:result=block
        return result
    directories=[n for n in (1,2) if valid(n,0,0x1ffc,0x1ffc)]
    bats=[n for n in (3,4) if valid(n,4,sector,0)]
    if not directories or not bats:raise AssertionError('No valid card directory/BAT')
    directory=newest(directories,0x1ffa);bat=newest(bats,4);result={};published=None;dedicated={};villagers={}
    for entry in range(127):
        at=directory*sector+entry*64
        if card[at:at+6]!=b'AMTE01':continue
        name=card[at+8:at+40].split(b'\0',1)[0].decode('ascii')
        if name=='.__dnd_transaction':continue
        block,count=word(at+54),word(at+56);seen=set();data=bytearray()
        for _ in range(count):
            if block<5 or block>=len(card)//sector or block in seen:raise AssertionError('Invalid current card chain')
            seen.add(block);data.extend(card[block*sector:(block+1)*sector]);block=word(bat*sector+10+(block-5)*2)
        if block!=65535 or data[:8]!=b'DNDSAVE1':raise AssertionError('Invalid current card save container')
        length,offset=struct.unpack_from('>II',data,8)
        if offset<32 or offset+length>len(data):raise AssertionError('Truncated current card save')
        payload=bytes(data[offset:offset+length])
        if name in ('AnimalTown','AnimalTown2') and data[16:24]==b'DNDREC01':
            try:records,phase,epoch=archive_records(payload,True)
            except AssertionError:continue # Incomplete initial creation keeps the legacy source files authoritative.
            if phase in (1,2,3) and (published is None or (epoch,name=='AnimalTown2')>published[:2]):
                published=(epoch,name=='AnimalTown2',{key:value for key,value in records.items() if key!='AT-Archive'})
        elif name in ('AnimalTown-Video','AnimalTown-Debug'):
            dedicated['options' if name=='AnimalTown-Video' else 'dev_config']=payload
            if count!=1:raise AssertionError('Settings entry must occupy one block')
        elif name.startswith('AnimalTown-Villager'):
            try:villagers.update(villager_card_banks(name,payload))
            except ValueError:pass # A valid archived bank can still be authoritative.
        else:result[name]=payload
    if published is not None:
        # Use the same logical bank names as the game repository. Archived
        # copies stay authoritative until migration removes them.
        result={**villagers,**published[2]}
    return {**result,**dedicated}


def active_edge_smoothing():
    import re
    modes=re.findall(r'ANIMALTOWN AA ([01]) render',runtime_log_text())
    if not modes:raise AssertionError('No active edge-smoothing mode was reported')
    return modes[-1]=='1'

def wait_options_saved(resolution,wide,smoothing=None,after=None):
    import struct
    deadline=time.monotonic()+45
    while time.monotonic()<deadline:
        try:payload=options_card_files().get('options')
        except AssertionError:payload=None # First archive checkpoint may still be flushing.
        if (payload and len(payload)==16 and payload[:3]==b'ATO' and payload[3] in (1,2,3)
                and (resolution is None or payload[4]==resolution) and (wide is None or payload[5]==wide)
                and (smoothing is None or payload[3]==3 and payload[7]==smoothing)):
            checksum=2166136261
            for value in payload[:12]:checksum=((checksum^value)*16777619)&0xffffffff
            completed=(after is None or
                       f'ANIMALTOWN OPTIONS SAVED {payload[4]} {payload[5]} {payload[7]}' in runtime_log_text()[after:])
            if checksum==struct.unpack_from('<I',payload,12)[0] and completed:return payload
        check_runtime_failure();time.sleep(.5)
    raise AssertionError('A did not persist the activated video setting')

def activate_video_setting(resolution,wide,smoothing=None):
    # The record can already contain these bytes, or become readable before
    # final verification returns. Wait for this activation's completion marker
    # before sending navigation to the game, including idempotent reapplication.
    offset=len(runtime_log_text());press('A')
    return wait_options_saved(resolution,wide,smoothing,after=offset)

def video_options_footer_ink(pixels):
    # Common interior of the normal/widescreen footer, away from the moving
    # backdrop. Count antialiased text against the much brighter cream panel.
    return sum(max(pixels[(y*800+x)*3:(y*800+x)*3+3])<170
               for y in range(335,410) for x in range(150,500))

def debug_visibility(require_record=False):
    import struct
    try:payload=options_card_files().get('options')
    except AssertionError:return None if require_record else False
    if not payload or len(payload)!=16 or payload[:3]!=b'ATO' or payload[3] not in (1,2,3):return None if require_record else False
    checksum=2166136261
    for value in payload[:12]:checksum=((checksum^value)*16777619)&0xffffffff
    if checksum!=struct.unpack_from('<I',payload,12)[0]:return None if require_record else False
    return payload[3]>=2 and payload[6]==1

def set_debug_visibility(visible):
    if debug_visibility()==visible:return
    for _ in range(3):press('Z')
    deadline=time.monotonic()+45;pending=None
    while time.monotonic()<deadline:
        check_runtime_failure()
        try:
            saved=debug_visibility(True)
        except AssertionError as error:
            # The first save publishes the consolidated card file before its
            # first DTR1 checkpoint. Only this expected in-flight state is
            # retryable, and only after we requested a save. A later read must
            # still verify the checkpoint and options checksum/value in full.
            if str(error)!='No verified archive checkpoint':raise
            pending=error
        else:
            pending=None
            if saved==visible:return
        time.sleep(.5)
    raise AssertionError('Triple Z did not persist debug-menu visibility'
                         +('; archive checkpoint remained unverified' if pending else '')) from pending

def debug_visibility_scenario(reload=False):
    wait_title()
    if reload:
        if not debug_visibility():raise AssertionError('Reload did not retain enabled debug visibility')
        press('D_UP');press('A');wait_title(modal=True);screenshot('debug-restored-title-modal.png')
        (root/'debug-visibility-load.json').write_text(json.dumps({'visibility_restored':True}))
        return
    if options_card_files():raise AssertionError('Default-visibility scenario needs a fresh disposable card')
    screenshot('debug-default-title.png')
    # Hidden title navigation wraps from Start to Options, rather than Debug.
    press('D_UP');press('A');wait_title(modal=True);screenshot('debug-default-options.png');press('B');wait_title()
    press('Z');press('Z');press('B');press('Z')
    if debug_visibility():raise AssertionError('An interrupted Z sequence enabled debug access')
    press('B');set_debug_visibility(True)
    press('D_DOWN');press('A');wait_title(modal=True);screenshot('debug-unlocked-title-modal.png')
    # Ground mip distance remains a persisted title debug slider.
    for _ in range(11):press('D_DOWN')
    press('D_RIGHT');screenshot('debug-slider-before-hide.png')
    set_debug_visibility(False);wait_title();screenshot('debug-hidden-title-again.png')
    if card_dev_config(root/'MemoryCardA.USA.raw')['values'][23]!=1:raise AssertionError('Hiding debug menus reset the selected slider')
    press('D_DOWN');start_game(True,'debug-visibility')
    def focus(name,expected):
        screenshot(name+'.png');pixels=rgb(name+'.png')
        scores=[sum(fill_matches(pixels[(y*800+x)*3:(y*800+x)*3+3],(221,231,188))
                    for y in range(131+i*24,151+i*24) for x in range(100,540,4)) for i in range(9)]
        if max(scores)<100 or scores.index(max(scores))!=expected:raise AssertionError(name+' selected a hidden or unexpected menu row')
    press('START')
    for _ in range(7):press('D_DOWN')
    focus('debug-hidden-settings-wrap',0)
    press('R');screenshot('debug-hidden-tab-wrap-backpack.png');press('L');focus('debug-hidden-backpack-wrap-settings',0)
    set_debug_visibility(True);press('R');focus('debug-enabled-game-menu',0)
    set_debug_visibility(False);focus('debug-hiding-open-menu-returns-settings',0)
    # Leave the flag enabled for a second invocation with --debug-visibility-load.
    set_debug_visibility(True)
    if not debug_visibility():raise AssertionError('In-game visibility was not persisted for next startup')
    (root/'debug-visibility.json').write_text(json.dumps({'hidden_default':True,'interrupted_sequence_rejected':True,
        'title_toggled':True,'hidden_tabs_skipped':True,'game_toggled':True,'open_debug_closed_safely':True,'persisted_enabled':True}))

def title_options():
    before=options_card_files();town={name:data for name,data in before.items() if name.startswith('AT-') or name.startswith('AnimalTown')}
    if not town:raise AssertionError('Options deletion needs a disposable saved-town fixture')
    # Create the independent debug record through the unlocked title UI.
    set_debug_visibility(True)
    press('D_UP');press('A');wait_title(modal=True);press('D_RIGHT');press('B');wait_title()
    deadline=time.monotonic()+30;debug=None
    while time.monotonic()<deadline:
        debug=options_card_files().get('dev_config')
        if debug:break
        time.sleep(.5)
    if not debug:raise AssertionError('Could not prepare independent debug preferences')
    press('D_UP');press('A');wait_title(modal=True);screenshot('options-open.png')
    # The third Video row is distinct from Delete. Navigation alone never saves.
    original=options_card_files().get('options');press('D_DOWN');press('D_DOWN')
    if active_edge_smoothing():press('D_RIGHT')
    screenshot('options-smoothing-draft-off.png')
    if options_card_files().get('options')!=original:raise AssertionError('Smoothing navigation persisted an unapplied option')
    activate_video_setting(None,None,0);screenshot('options-smoothing-off.png')
    press('D_UP');press('D_UP')
    original=options_card_files().get('options');press('D_RIGHT');screenshot('options-draft-576p.png')
    if options_card_files().get('options')!=original:raise AssertionError('Navigation persisted an unapplied option')
    press('D_LEFT');activate_video_setting(1,0);screenshot('options-480p.png')
    press('D_RIGHT');activate_video_setting(2,0);screenshot('options-576p.png')
    press('D_DOWN');press('D_LEFT');saved=activate_video_setting(2,1);screenshot('options-widescreen.png')
    current=options_card_files()
    if any(current.get(name)!=data for name,data in town.items()):raise AssertionError('Video selection changed town data')
    press('D_DOWN');press('D_DOWN');press('A');screenshot('options-delete-default.png');press('A')
    if any(options_card_files().get(name)!=data for name,data in town.items()):raise AssertionError('Default Cancel deleted town data')
    press('A');press('D_RIGHT');screenshot('options-delete-focused.png');press('B')
    if any(options_card_files().get(name)!=data for name,data in town.items()):raise AssertionError('B cancellation deleted town data')
    press('A');press('D_RIGHT');delete_offset=len(runtime_log_text());press('A')
    deadline=time.monotonic()+90
    while time.monotonic()<deadline:
        current=options_card_files()
        if (not any(name.startswith('AT-') or name.startswith('AnimalTown') for name in current)
                and 'ANIMALTOWN TOWN DATA DELETED' in runtime_log_text()[delete_offset:]):break
        check_runtime_failure();time.sleep(1)
    else:raise AssertionError('Confirmed town deletion did not remove every record and its recovery marker')
    if current.get('options')!=saved or current.get('dev_config')!=debug:raise AssertionError('Deletion changed video/debug preferences')
    screenshot('options-deleted.png');press('B');wait_title();press('D_UP');press('D_UP');press('A');wait_profiles('options-empty-town.png')
    if 'ANIMALTOWN GAME START' in runtime_log_text():raise AssertionError('Title options loaded town rendering assets')
    (root/'title-options.json').write_text(json.dumps({'modes':[480,576],'wide_preserved_576_timing':'ANIMALTOWN VIDEO 2 1 output 640x576' in runtime_log_text(),
        'activation_persisted_immediately':True,'draft_did_not_save':True,'default_cancel_preserved_town':True,'back_cancel_preserved_town':True,
        'deleted_files':sorted(town),'remaining_files':sorted(current),'independent_preferences_preserved':True,'town_assets_deferred':True},indent=2))
    if 'ANIMALTOWN VIDEO 2 1 output 640x576' not in runtime_log_text():raise AssertionError('Widescreen change failed to preserve actual 576p output')

def title_options_load():
    expected=wait_options_saved(2,1)
    if 'ANIMALTOWN OPTIONS LOADED 2 1' not in runtime_log_text() or 'ANIMALTOWN VIDEO 2 1 output 640x576' not in runtime_log_text():
        raise AssertionError('Startup did not restore saved progressive/widescreen settings')
    press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True);screenshot('options-restored.png');press('B')
    if options_card_files().get('options')!=expected:raise AssertionError('Opening/closing Options rewrote preferences')
    press('D_UP');press('D_UP');press('A');wait_profiles('options-restored-empty-town.png')
    if any(name.startswith('AT-') or name.startswith('AnimalTown') for name in options_card_files()):raise AssertionError('Deleted villagers returned after restart')
    (root/'title-options-load.json').write_text(json.dumps({'persisted_resolution':576,'persisted_widescreen':True,'deleted_town_stayed_deleted':True}))

def title_options_unsupported():
    before=options_card_files();press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True)
    screenshot('options-unsupported-before.png');baseline=video_options_footer_ink(rgb('options-unsupported-before.png'))
    offset=len(runtime_log_text());press('A');deadline=time.monotonic()+30
    while time.monotonic()<deadline:
        screenshot('options-unsupported.png')
        if video_options_footer_ink(rgb('options-unsupported.png'))>baseline+100:break
        check_runtime_failure();time.sleep(.5)
    else:raise AssertionError('Unsupported progressive selection did not display its rejection feedback')
    if options_card_files()!=before:raise AssertionError('Unsupported progressive mode modified saved settings/town')
    if 'ANIMALTOWN OPTIONS SAVED' in runtime_log_text()[offset:]:raise AssertionError('Rejected mode was persisted')
    (root/'title-options-unsupported.json').write_text(json.dumps({'unsupported_mode_did_not_save':True,'rejection_feedback_visible':True}))

def wait_profiles(name='profile-picker.png'):
    maximum='--jobs' in sys.argv and sys.argv[sys.argv.index('--jobs')+1].startswith('maximum-')
    deadline=time.monotonic()+(600 if maximum else 180)
    while time.monotonic()<deadline:
        check_runtime_failure()
        # Normal scenarios accept the default if card selection needs a dialog.
        # Dedicated card scenarios drive and verify the chooser themselves.
        text=runtime_log_text()
        chooser=text.rfind('ANIMALTOWN SAVE CARD CHOOSER')
        chosen=max(text.rfind('ANIMALTOWN SAVE CARD SELECTED'),text.rfind('ANIMALTOWN SAVE CARD NONE'))
        if chooser>chosen:
            if '--save-cards' in sys.argv:raise AssertionError('Card scenario reached picker without confirming its card choice')
            screenshot('default-memory-card.png');press('A');continue
        screenshot(name);pixels=rgb(name)
        # Four green name pills identify the resident picker. Honey accents
        # are small and can vary with portrait/viewport rasterization.
        cards=color_count(pixels,(107,151,90))>3000
        if cards and color_count(pixels,(255,245,221))>70000:return
        if p.poll() is not None:raise AssertionError('Emulator stopped before villager picker')
        time.sleep(1)
    context=[line for line in runtime_log_text().splitlines() if 'ANIMALTOWN' in line][-30:]
    print('Runtime context before picker timeout:\n'+'\n'.join(context),flush=True)
    raise AssertionError('Villager picker did not appear')

def memory_cards():
    """Real slot selection, retained choice and optional saving on disposable cards."""
    import hashlib
    import re
    mode=sys.argv[sys.argv.index('--save-cards')+1]
    if mode not in ('slot-b','choose-b','without-saving','no-card'):raise ValueError('Unknown card scenario')
    paths=[root/'MemoryCardA.USA.raw',root/'MemoryCardB.USA.raw']
    def digests():return [hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None for path in paths]
    def chooser(expected,start=0,name='memory-card-choice.png'):
        deadline=time.monotonic()+90
        while time.monotonic()<deadline:
            check_runtime_failure();text=runtime_log_text()[start:]
            matches=re.findall(r'ANIMALTOWN SAVE CARD CHOOSER default (\d+)',text)
            if matches:
                if int(matches[-1])!=expected:raise AssertionError('Unexpected default memory card choice: '+matches[-1])
                screenshot(name);return
            if p.poll() is not None:raise AssertionError('Emulator stopped before card chooser')
            time.sleep(.5)
        raise AssertionError('Memory-card chooser did not appear')
    wait_title();before=digests();start=len(runtime_log_text());press('A')
    chosen=2 if mode in ('without-saving','no-card') else 1
    default=1 if mode=='slot-b' else 2 if mode=='no-card' else 0
    def enter_picker(start,name,remembered=False):
        if mode=='slot-b':
            wait_profiles(name)
            log=runtime_log_text()[start:]
            if 'ANIMALTOWN SAVE CARD AUTO 5' not in log or 'ANIMALTOWN SAVE CARD CHOOSER' in log:
                raise AssertionError('A sole Slot B card should open the picker directly')
            return
        chooser(chosen if remembered else default,start)
        if not remembered:
            press('X');screenshot('memory-card-rechecked.png')
            if digests()!=before:raise AssertionError('Opening/rechecking the card chooser changed a card')
            if mode=='choose-b':press('D_DOWN')
            if mode=='without-saving':press('D_UP')
        press('A');wait_profiles(name)
    enter_picker(start,'memory-card-picker.png')
    # Returning from the resident picker must preserve the confirmed destination.
    press('B');wait_title();start=len(runtime_log_text());press('A')
    enter_picker(start,'memory-card-picker-return.png',remembered=True)
    report={'scenario':mode,'default':default,'auto_selected':mode=='slot-b',
            'remembered_choice':chosen,'discovery_read_only':True,'passed':False}
    if mode in ('slot-b','without-saving'):
        press('A');wait_onboarding('name','memory-card-name.png');press('A');press('START')
        wait_onboarding('look','memory-card-look.png');press('A')
        wait_onboarding('home','memory-card-home.png');press('A')
        wait_onboarding('review','memory-card-review.png');press('A')
        if mode=='slot-b':
            after=wait_saved_after(None,paths[1]);report['saved_sequence']=after['sequence']
            if committed_card_sequence(paths[0]):raise AssertionError('Slot B town was saved to Slot A')
        wait_onboarding('world','memory-card-world.png')
        save_menu();screenshot('memory-card-save-menu.png')
        if mode=='without-saving':
            # This normally persists independent user options, even in menus.
            for _ in range(3):press('Z')
            if 'ANIMALTOWN DEBUG MENUS VISIBLE 1' not in runtime_log_text():raise AssertionError('Temporary-mode preference action did not run')
            if 'ANIMALTOWN ONBOARDING TEMPORARY' not in runtime_log_text():raise AssertionError('Temporary onboarding did not finish')
            if digests()!=before:raise AssertionError('Playing without saving modified a memory card')
            report['card_bytes_unchanged']=True
        elif digests()[0]!=before[0]:raise AssertionError('Slot B play modified Slot A')
    elif digests()!=before:raise AssertionError('Choosing a slot created a save before onboarding')
    report['passed']=True
    (root/'memory-card-report.json').write_text(json.dumps(report,indent=2)+'\n')

def profile_cards():
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    active=before['active'] if before else 0
    wait_title();press('A');wait_profiles('cards-enter.png')
    def focus_center(name):
        screenshot(name);pixels=rgb(name)
        focus=[x for y in range(275,400) for x in range(55,620)
               if all(abs(pixels[(y*800+x)*3+c]-v)<=5 for c,v in enumerate((255,216,136)))]
        if len(focus)<400:raise AssertionError('Graphical picker has no highlighted portrait card')
        return sum(focus)/len(focus)
    centers=[]
    for step in range(5):
        centers.append(focus_center(f'cards-{step}.png'))
        if step<4:press('D_RIGHT')
    ordered=sorted(range(4),key=lambda i:centers[i])
    if ([(active+i)%4 for i in ordered]!=[0,1,2,3] or abs(centers[0]-centers[4])>2 or
        any(centers[ordered[i+1]]-centers[ordered[i]]<60 for i in range(3))):
        raise AssertionError('Portrait-card right navigation/wrap does not follow the four profiles')
    press('D_LEFT')
    if abs(focus_center('cards-left-wrap.png')-centers[3])>2:raise AssertionError('Portrait-card left wrap selected the wrong profile')
    press('B');wait_title();screenshot('cards-back.png');press('A');wait_profiles('cards-return.png')
    if abs(focus_center('cards-return.png')-centers[0])>2:raise AssertionError('Cancelling changed the initial resident focus')
    press('B');wait_title()
    after=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    if after!=before:raise AssertionError('Browsing/cancelling the picker modified the saved town')
    (root/'profile-cards.json').write_text(json.dumps({'before':before,'after':after,'focus_centers':centers,'right_wrap':True,'back_to_title':True},indent=2))

def shared_profiles():
    slot=int(sys.argv[sys.argv.index('--profile-slot')+1]) if '--profile-slot' in sys.argv else 0
    if not 0<=slot<4:raise ValueError('Profile slot must be 0..3')
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    creating=not before or not before['profiles'][slot]['created']
    wait_title();press('A');wait_profiles()
    press('B');wait_title();screenshot('profile-cancel-to-title.png');press('A');wait_profiles()
    for _ in range((slot-(before['active'] if before else 0))%4):press('D_RIGHT')
    screenshot('profile-selected.png');press('A')
    if creating:
        after,loading=finish_onboarding('profile',before)
    else:
        loading=wait_onboarding('world','profile-game.png');save_menu();after=wait_saved_after(before)
    if after['active']!=slot:raise AssertionError('Wrong villager profile was saved')
    if before:
        for index in range(4):
            if index!=slot or not creating:
                if after['profiles'][index]!=before['profiles'][index]:raise AssertionError('Saving changed another profile unexpectedly')
        if not creating and (after['terrain_sha256']!=before['terrain_sha256'] or after['houses_sha256']!=before['houses_sha256']):raise AssertionError('Switching villagers changed the shared terrain/home plots')
    screenshot('profile-saved.png');(root/'profiles.json').write_text(json.dumps({'before':before,'after':after,'loading_background_seen':loading},indent=2))

def delete_profile():
    slot=int(sys.argv[sys.argv.index('--profile-slot')+1]) if '--profile-slot' in sys.argv else 0
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    if not before or not before['profiles'][slot]['created']:raise AssertionError('Deletion requires a disposable occupied slot')
    wait_title();press('A');wait_profiles('delete-picker.png')
    for _ in range((slot-before['active'])%4):press('D_RIGHT')
    press('Z');screenshot('delete-default-cancel.png');press('A');wait_profiles()
    if committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=before:raise AssertionError('Default Cancel changed the town')
    press('Z');press('D_RIGHT');screenshot('delete-focused.png');press('B');wait_profiles()
    if committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=before:raise AssertionError('B cancellation changed the town')
    press('Z');press('D_RIGHT');screenshot('delete-confirm.png');press('A')
    deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        after=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
        if after and after['sequence']>=before['sequence']+2 and not after['profiles'][slot]['created']:break
        if p.poll() is not None:raise AssertionError('Emulator stopped while deleting a resident')
        time.sleep(2)
    else:
        screenshot('delete-not-completed.png');raise AssertionError('Deletion did not publish two complete empty-profile generations')
    for i in range(4):
        if i!=slot and (before['profiles'][i]!=after['profiles'][i] or before['plots'][i]!=after['plots'][i]):
            raise AssertionError('Deletion changed another resident or their home')
    if after['terrain_sha256']!=before['terrain_sha256']:raise AssertionError('Deletion changed the shared landscape')
    wait_profiles('delete-complete.png');press('B');wait_title();press('A');wait_profiles('delete-reloaded.png')
    for _ in range((slot-after['active'])%4):press('D_RIGHT')
    press('Z');screenshot('delete-empty-z.png')
    if color_count(rgb('delete-empty-z.png'),(107,151,90))<3000:raise AssertionError('Z on an empty card opened a deletion modal')
    if committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=after:raise AssertionError('Reopening the picker changed the deletion')
    if '--profile-delete-start-new' in sys.argv:
        press('A');wait_onboarding('name','delete-new-creation.png')
        time.sleep(8);screenshot('delete-new-creation.png')
        if committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)!=after:raise AssertionError('Replacement creation autosaved')
    (root/'profile-deletion.json').write_text(json.dumps({'slot':slot,'before':before,'after':after,'default_cancel':True,'back_cancel':True,'reloaded':True},indent=2))

def submission_scenario():
    set_debug_visibility(True)
    sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(root/'user/Dump/Frames',track_presentations=True);segments=[]
    def segment(name,seconds):
        start=clock.frames();target=start+int(seconds*60000/1001+.999);deadline=time.monotonic()+seconds*12+30
        while clock.frames()<target:
            if p.poll() is not None:raise AssertionError('Emulator stopped during '+name)
            if time.monotonic()>deadline:raise AssertionError('Guest VI clock stopped during '+name)
            time.sleep(.1)
        end=clock.frames();present=[v for v in clock.presentations if start<v<=end]
        gaps=[(b-a)*1001/60 for a,b in zip(present,present[1:])]
        def percentile(q):return sorted(gaps)[min(len(gaps)-1,int((len(gaps)-1)*q))] if gaps else None
        segments.append({'name':name,'start_vi':start,'end_vi':end,'guest_seconds':(end-start)*1001/60000,'presentations':len(present),'fps':len(present)*60000/(1001*(end-start)),'frame_ms_p50':percentile(.5),'frame_ms_p95':percentile(.95)})
        (root/'submission-segments.json').write_text(json.dumps({'segments':segments,'clock':'NTSC VI chunks including repeated frames; software Dolphin, not hardware'},indent=2))
        screenshot('submissions-'+name+'.png')
        if name=='stationary':
            pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/('submissions-'+name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
            valid=False
            for origin in range(20,36):
                for width in range(570,601):
                    words=[0]*32
                    for bit in range(512):
                        x=origin+int((bit%64+.5)*width/64)
                        if pixels[((450+bit//64*4)*800+x)*3]>128:words[bit//16]|=1<<(bit%16)
                    checksum=0
                    for word in words[:31]:checksum^=word
                    if words[0]==0x4154 and words[1] in (5,6) and words[31]==checksum and words[30]==0:valid=True;break
                if valid:break
            if not valid:raise AssertionError('No valid gameplay telemetry after startup; inspect stationary screenshot')
    press('A');time.sleep(15)
    segment('stationary-profiler-off',6)
    os.write(controller,b'SET MAIN .9 .5\n');segment('walking-profiler-off',3)
    os.write(controller,b'SET MAIN .1 .5\n');segment('return-profiler-off',3)
    os.write(controller,b'SET MAIN .5 .5\n')
    os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\n')
    segment('stationary',profile_seconds)
    for n,(x,y,running) in enumerate(((.9,.5,False),(.1,.5,True))):
        os.write(controller,f'SET MAIN {x} {y}\n'.encode())
        if running:os.write(controller,b'PRESS B\n')
        segment('travel-'+str(n),profile_seconds);os.write(controller,b'SET MAIN .5 .5\nRELEASE B\n')
    press('X');segment('backpack',profile_seconds);press('B')
    press('Y');segment('map',5);press('B')
    # Session-only rain toggle (option 16, visible row 14 after shadow removal).
    press('START');press('R')
    for _ in range(7):press('D_DOWN')
    press('A');press('B');segment('rain',profile_seconds)
    press('START');press('R')
    for _ in range(7):press('D_DOWN')
    press('A');press('B')
    # Existing inspector selects the same NPC cottage in both builds.
    os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n')
    segment('cottage-closed',5)
    os.write(controller,b'PRESS A\n');time.sleep(.15);screenshot('submissions-cottage-opening.png');os.write(controller,b'RELEASE A\n')
    segment('interior',5)
    before_save=committed_card_sequence(root/'MemoryCardA.USA.raw')
    building_save()
    for sample in range(30):
        if committed_card_sequence(root/'MemoryCardA.USA.raw')>before_save:break
        time.sleep(1)
    else:raise AssertionError('Post-route save did not publish a complete generation')

def cliff_minimap():
    def frame(name):
        screenshot(name+'.png')
        return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    def paper(pixels,left,top,right,bottom):
        return sum(all(abs(pixels[(y*800+x)*3+c]-value)<=3 for c,value in enumerate((255,245,221)))
                   for y in range(top,bottom) for x in range(left,right))
    results=[]
    def map_state(name,expected):
        # Dolphin's 640x480 EFB appears in the 584-pixel-wide game viewport.
        # The running map fills its bulged silhouette; it has no paper frame.
        # Look for its flat terrain palette inside the viewport instead.
        pixels=frame(name)
        palette=((75,154,187),(167,119,73),(229,209,150),(130,127,92),(191,179,147),(119,168,88))
        count=sum(any(all(abs(pixels[(y*800+x)*3+c]-value)<=3 for c,value in enumerate(color)) for color in palette)
                  for y in range(310,435) for x in range(450,620))
        visible=count>400
        results.append(f'{name}: map terrain samples={count}, visible={visible}, expected={expected}')
        (root/'cliff-minimap.txt').write_text('\n'.join(results)+'\n')
        if visible!=expected:raise AssertionError('Unexpected minimap visibility: '+results[-1])
        return pixels
    # Normal onboarding; the starting area below z=42 is kept clear of trees.
    start_game(True,'cliff-minimap');time.sleep(12)
    pixels=map_state('hud-clean',False)
    if paper(pixels,60,22,620,55)>200:raise AssertionError('Build/currency HUD panel remains visible')
    os.write(controller,b'PRESS B\n')
    def circles(count):
        # Small south/east/north/west loops keep actual running in the clear
        # arrival area without hitting the house or relying on a long random route.
        for _ in range(count):
            for x,y in ((.5,.35),(.65,.5),(.5,.65),(.35,.5)):
                os.write(controller,f'SET MAIN {x} {y}\n'.encode());time.sleep(.35)
    circles(2);map_state('minimap-before-six',False)
    circles(6);map_state('minimap-running',True)
    os.write(controller,b'SET MAIN 0.5 0.5\nRELEASE B\n')
    time.sleep(1);map_state('minimap-stopping-grace',True)
    time.sleep(5);map_state('minimap-hidden',False)
    # Inspect the real generated terrace, with cloud motion disabled so the
    # shadow comparison is not a comparison of different weather frames.
    os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_RIGHT');press('START')
    os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15)
    on=frame('cliff-shadow-on')
    os.write(controller,b'PRESS L\nPRESS R\nPRESS Z\n');time.sleep(.2);press('Y')
    os.write(controller,b'RELEASE Z\nRELEASE L\nRELEASE R\n');time.sleep(2)
    off=frame('cliff-shadow-off')
    darkened=sum(sum(off[(y*800+x)*3:(y*800+x)*3+3])-sum(on[(y*800+x)*3:(y*800+x)*3+3])>45
                 for y in range(150,340) for x in range(430,650))
    results.append(f'Cliff shadow comparison: {darkened} darkened ground samples')
    (root/'cliff-minimap.txt').write_text('\n'.join(results)+'\n')
    if darkened<25:raise AssertionError('No visible ground shadow at the generated cliff')
    os.write(controller,b'PRESS L\nPRESS R\nPRESS Z\n');time.sleep(.2)
    for _ in range(3):press('Y')
    os.write(controller,b'RELEASE Z\nRELEASE L\nRELEASE R\n')
    for scene in range(1,5):
        os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_LEFT')
        os.write(controller,b'RELEASE L\nRELEASE R\n')
        # The next input must wait for the newly focused terrain to load.
        # Inputs during loading are intentionally ignored by the game.
        time.sleep(15);frame('water-tour-'+str(scene))
    frame('coastal-cliff-water')

def save_menu():
    press('START');press('A')

def save_and_quit():
    import re
    card=root/'MemoryCardA.USA.raw'
    before,_=start_game(prefix='quit-initial')
    title_marker='ANIMALTOWN TITLE READY'
    title_count=runtime_log_text().count(title_marker)
    press('D_DOWN')
    original=card.read_bytes()
    press('START');press('D_DOWN');press('A')
    screenshot('quit-confirm.png')
    press('A') # Keep playing is focused by default.
    if runtime_log_text().count(title_marker)!=title_count or card.read_bytes()!=original:
        raise AssertionError('Cancelling Save & Quit changed the card or left town')
    press('A');press('B') # Reopen confirmation, then back out.
    if card.read_bytes()!=original:raise AssertionError('Backing out of Save & Quit wrote to the card')
    press('A');press('D_DOWN');press('A')
    after=wait_saved_after(before)
    deadline=time.monotonic()+180
    while runtime_log_text().count(title_marker)<=title_count:
        check_runtime_failure()
        if time.monotonic()>=deadline:raise AssertionError('The saved session did not return to title')
        time.sleep(1)
    wait_title();screenshot('quit-title.png')
    reloaded,_=start_game(prefix='quit-reload')
    if reloaded!=after:raise AssertionError('Starting again did not preserve the committed save')
    quit_timing=re.search(r'ANIMALTOWN SAVE TOTAL: ([^\r\n]* / quit 1)',runtime_log_text())
    if not quit_timing:raise AssertionError('Missing verified Save & Quit timing')
    press('D_DOWN');save_menu()
    saved_again=wait_saved_after(reloaded)
    deadline=time.monotonic()+90
    while True:
        now_timing=re.search(r'ANIMALTOWN SAVE TOTAL: ([^\r\n]* / quit 0)',runtime_log_text())
        if now_timing:break
        check_runtime_failure()
        if time.monotonic()>=deadline:raise AssertionError('Missing verified Save Now timing')
        time.sleep(1)
    screenshot('save-now.png')
    (root/'save-and-quit.json').write_text(json.dumps({'before':before['sequence'],'saved':after['sequence'],
        'cancel_untouched':True,'returned_to_title':True,'restarted_game':True,
        'quit_timing':quit_timing[1],'save_now_sequence':saved_again['sequence'],'save_now_timing':now_timing[1]},indent=2)+'\n')

def villager_card_banks(name, payload):
    import struct
    bases = {'AnimalTown-Villager1': 'AT-Player',
             **{f'AnimalTown-Villager{slot}': f'AT-Villager{slot}-' for slot in (2, 3, 4)}}
    base = bases.get(name)
    if base is None or len(payload) < 12 or payload[:4] != b'ATV1':
        raise ValueError('Invalid villager card entry')
    first, second = struct.unpack_from('<II', payload, 4)
    if not first + second or max(first, second) > 8192 or len(payload) != 12 + first + second:
        raise ValueError('Invalid villager card banks')
    banks = (payload[12:12 + first], payload[12 + first:])
    return {base + str(bank): data for bank, data in enumerate(banks) if data}

def committed_card_sequence(path, expected_day=None, details=False):
    """Read a complete generation from either redundant directory/BAT pair."""
    import struct
    import zlib
    if not path.exists():return 0
    card=path.read_bytes();sector=8192
    town_versions=(4,6,7,8,9,10,11)
    job_extension_bytes=4*(40+1) # TownJobState words plus the encoded word count.
    if len(card)%sector:raise AssertionError('Truncated emulator memory card')
    def be16(at):return struct.unpack_from('>H',card,at)[0]
    def record(payload,kind):
        if len(payload)<32:raise ValueError('Short save record')
        magic,version,catalog,actual,sequence,seed,length=struct.unpack_from('<7I',payload)
        crc=struct.unpack_from('<I',payload,len(payload)-4)[0]
        if magic!=0x50535441 or version<1 or version>11 or (version>=6 and kind!=0) or actual!=kind or length!=len(payload) or zlib.crc32(payload[:-4])!=crc:
            raise ValueError('Invalid save record')
        return version,catalog,sequence,seed,crc
    def v11_daylight(town,at,ground_at,ground_count,placed_count,runtime):
        # Split v11 has thirteen building types and appends police custody
        # after the job state. Locate daylight from the bounded wire layout,
        # never by subtracting a fixed suffix from the end of the record.
        width,height=struct.unpack_from('<2I',town,28);cells=width*height;end=len(town)-4
        if (width,height)!=(128,120) or ground_count>512 or placed_count>256:
            raise ValueError('Invalid v11 town dimensions or item counts')
        def words(count):
            nonlocal at
            if count<0 or at<28 or count>(end-at)//4:raise ValueError('Truncated v11 town extension')
            result=struct.unpack_from('<'+str(count)+'i',town,at);at+=count*4
            return result
        active,*types=words(14)
        if not 0<=active<=3 or any(not 0<=kind<=255 for kind in types):raise ValueError('Invalid v11 building state')
        npc_runtime=words(42)
        if runtime is not None and (active or any(npc_runtime)):raise ValueError('Noncanonical v11 town projection')
        for size,paths in ((cells//4,True),(cells,False)):
            marker,length=words(2)
            if marker!=1 or length!=size or length>end-at:raise ValueError('Invalid v11 path or traffic extension')
            if paths and any(value&0xaa for value in town[at:at+length]):raise ValueError('Invalid v11 path style')
            at+=length
        snow_count=words(1)[0]
        if snow_count!=0 and not 4<=snow_count<=3+cells//2:raise ValueError('Invalid v11 snow checkpoint length')
        snow=words(snow_count)
        if snow:
            day,ticks,tokens=snow[:3]
            if day<0 or not 0<=ticks<30 or not 1<=tokens<=cells or snow_count!=3+(tokens+1)//2:
                raise ValueError('Invalid v11 snow checkpoint header')
            cell=0;has_wear=False
            for i in range(tokens):
                token=(snow[3+i//2]>>((i%2)*16))&65535
                if cell>=cells or token==0:raise ValueError('Invalid v11 snow checkpoint tile')
                if token&0x8000:
                    run=token&0x7fff
                    if run==0 or run>cells-cell:raise ValueError('Invalid v11 snow checkpoint run')
                    cell+=run
                else:
                    if token>>4>=600 or token&15==0:raise ValueError('Invalid v11 snow checkpoint dent')
                    cell+=1;has_wear=True
            if cell!=cells or not has_wear:raise ValueError('Incomplete v11 snow checkpoint')
        daylight=words(1)[0]
        if not 0<=daylight<=26:raise ValueError('Invalid v11 daylight location')
        jobs=words(41)
        if (runtime is None and jobs[0]!=40) or (runtime is not None and any(jobs)):
            raise ValueError('Invalid v11 job state projection')
        x,z=words(2)
        if (x,z)!=(-1,-1) and not (1<=x<=width-4 and 1<=z<=height-5):raise ValueError('Invalid v11 koban site')
        count=words(1)[0]
        if not 4<=count<=4+13*512:raise ValueError('Invalid v11 custody length')
        recovery=words(count);magic,version,sequence,receipts=recovery[:4]
        if magic!=0x3144464c or version!=1 or sequence<0 or not 0<=receipts<=512 or count!=4+13*receipts:
            raise ValueError('Invalid v11 custody header')
        indexes=set();ids=set()
        for n in range(receipts):
            index,recovery_id,source,item,x,z,kind,owner,day,rotation,room,wrapped,custody_owner=recovery[4+13*n:17+13*n]
            if not 0<=index<ground_count or index in indexes or not 1<=recovery_id<=sequence or recovery_id in ids or source not in (0,1,2) or item<=0 or not (0<=x<width and 0<=z<height and 0<=custody_owner<4) or wrapped not in (0,1):
                raise ValueError('Invalid v11 custody identity')
            if source==1:
                if kind!=-1 or not 0<=owner<=9 or not 0<=rotation<=3 or room not in (0,1,2,3,4,16,17,18,19,20,21,24,25,26,*range(32,44)) or wrapped:
                    raise ValueError('Invalid v11 recovered placement')
            elif not 0<=owner<4 or custody_owner!=owner or rotation or room or (source==0 and not 0<=kind<=3) or (source==2 and kind!=-1):
                raise ValueError('Invalid v11 recovered ground item')
            ground=struct.unpack_from('<6i',town,ground_at+index*24)
            if ground!=(item,x,z,custody_owner,day,4+8*wrapped):raise ValueError('Changed v11 custody item')
            indexes.add(index);ids.add(recovery_id)
        if at!=end:raise ValueError('Trailing v11 town data')
        return daylight
    best=0;best_day=None;best_details=None
    for directory in (1,2):
        for bat in (3,4):
            files={};villagers={};archive=None
            try:
                for i in range(127):
                    at=directory*sector+i*64
                    if card[at:at+6]!=b'AMTE01':continue
                    name=card[at+8:at+40].split(b'\0',1)[0].decode('ascii')
                    if not name.startswith(('AT-', 'AnimalTown-Villager')) and name not in ('AnimalTown','AnimalTown2'):continue
                    block,count=be16(at+54),be16(at+56);seen=set();data=bytearray()
                    for _ in range(count):
                        if block<5 or block>=len(card)//sector or block in seen:raise ValueError('Invalid card chain')
                        seen.add(block);data.extend(card[block*sector:(block+1)*sector])
                        block=be16(bat*sector+10+(block-5)*2)
                    if block!=65535 or data[:8]!=b'DNDSAVE1':raise ValueError('Invalid card container')
                    length,offset=struct.unpack_from('>II',data,8)
                    if offset<32 or offset+length>len(data):raise ValueError('Truncated card container')
                    payload=bytes(data[offset:offset+length])
                    if name in ('AnimalTown','AnimalTown2'):
                        if data[16:24]!=b'DNDREC01' or offset!=16384:continue
                        try:records,phase,epoch=archive_records(payload,True)
                        except AssertionError:continue # A cut during initial zero filling leaves the complete legacy town intact.
                        if phase in (1,2,3) and (archive is None or (epoch,name=='AnimalTown2')>archive[:2]):archive=(epoch,name=='AnimalTown2',records)
                    elif name.startswith('AnimalTown-Villager'):
                        try:villagers.update(villager_card_banks(name, payload))
                        except ValueError:pass # An archived bank can still be authoritative.
                    else:files[name]=payload
                if archive is not None:files={**villagers, **archive[2]}

                for bank in (0,1):
                    try:
                        names=[f'AT-{kind}{bank}' for kind in ('Town','Houses','Shops','Player')]
                        commit_name=f'AT-Commit{bank}'
                        if commit_name not in files:continue
                        commit=files[commit_name];identity=record(commit,4)
                        if identity[0]>=4:names.extend(f'AT-Villager{slot}-{bank}' for slot in (2,3,4))
                        runtime=None
                        if identity[0]==5:
                            if len(commit)!=192:continue
                            bases=['AT-Town','AT-Houses','AT-Shops','AT-Player','AT-Villager2-','AT-Villager3-','AT-Villager4-','AT-Residents']
                            names=[]
                            for i,base in enumerate(bases):
                                kind,copy,sequence,length,crc=struct.unpack_from('<5I',commit,28+i*20)
                                if kind!=(i if i<4 else i+1) or copy>1 or sequence>identity[2]:raise ValueError('Invalid save reference')
                                selected=None
                                for candidate in (copy,1-copy):
                                    name=base+str(candidate)
                                    try:
                                        part=files[name];pi=record(part,kind)
                                        if pi[0] not in (town_versions if i==0 else (4,)) or pi[1:4]!=(identity[1],sequence,identity[3]) or len(part)!=length or pi[4]!=crc:continue
                                        selected=name;break
                                    except (KeyError,ValueError,struct.error):continue
                                if selected is None:raise ValueError('Missing exact referenced revision')
                                if i==7:runtime=files[selected]
                                else:names.append(selected)
                            town_version=struct.unpack_from('<I',files[names[0]],4)[0]
                            expected_runtime=300+(job_extension_bytes if town_version>=9 else 0)
                            has_job_clock=town_version>=7 and len(runtime)==expected_runtime+12
                            if (len(runtime)!=expected_runtime and not has_job_clock) or (town_version>=9 and struct.unpack_from('<I',runtime,296)[0]!=40):
                                raise ValueError('Invalid resident runtime extension')
                            if has_job_clock:
                                marker,clock=struct.unpack_from('<Iq',runtime,expected_runtime-4)
                                if marker!=0x43424f4a or clock!=-(1<<63) and not -11676096000<=clock<13569465600:
                                    raise ValueError('Invalid offline job clock')
                        else:
                            if len(commit)!=32+8*len(names) or not all(name in files for name in names):continue
                            for kind,name in enumerate(names):
                                part=files[name];part_identity=record(part,kind if kind<4 else kind+1)
                                expected_length,expected_crc=struct.unpack_from('<II',commit,28+kind*8)
                                if part_identity[0] not in (town_versions if kind==0 else (identity[0],)) or part_identity[1:4]!=identity[1:4] or len(part)!=expected_length or part_identity[4]!=expected_crc:
                                    raise ValueError('Mixed save generation')
                        if identity[2]>best:
                            town=files[names[0]];town_day=struct.unpack_from('<i',town,40)[0];candidate_details=None
                            if identity[0]>=4:
                                at=48
                                for _ in range(2):at+=4+struct.unpack_from('<I',town,at)[0]*4
                                terrain_hash=__import__('hashlib').sha256(town[48:at]).hexdigest()
                                for _ in range(6):at+=8;at+=4+struct.unpack_from('<I',town,at)[0]*4;at+=8
                                ground_count=struct.unpack_from('<I',town,at)[0];ground_at=at+4;at=ground_at+ground_count*24
                                placed_count=struct.unpack_from('<I',town,at)[0];at+=4+placed_count*24
                                active=struct.unpack_from('<I',runtime,124)[0] if runtime is not None else struct.unpack_from('<I',town,at)[0];profiles=[]
                                town_version=struct.unpack_from('<I',town,4)[0]
                                if town_version>=11:
                                    if active>3:raise ValueError('Invalid v11 active resident')
                                    daylight=v11_daylight(town,at,ground_at,ground_count,placed_count,runtime)
                                else:
                                    daylight_offset=len(town)-8-(job_extension_bytes if town_version>=9 else 0)
                                    daylight=struct.unpack_from('<I',town,daylight_offset)[0] if town_version>=8 else 0
                                for name in names[3:]:
                                    part=files[name];length=struct.unpack_from('<I',part,160)[0]
                                    label=''.join(chr(struct.unpack_from('<I',part,164+i*4)[0]) for i in range(length))
                                    pocket_at=164+length*4
                                    pocket_at+=4+struct.unpack_from('<I',part,pocket_at)[0]*4
                                    profiles.append({'name':label,'created':struct.unpack_from('<I',part,28)[0],'wallet':struct.unpack_from('<I',part,40)[0],'species':struct.unpack_from('<I',part,44)[0],
                                                     'savings':struct.unpack_from('<q',part,132)[0],'pockets':list(struct.unpack_from('<30i',part,pocket_at))})
                                plots=[dict(zip(('placed','x','z'),struct.unpack_from('<3i',files[names[1]],28+i*12))) for i in range(4)]
                                profile_hashes=[__import__('hashlib').sha256(files[name][28:-4]).hexdigest() for name in names[3:]]
                                candidate_details={'sequence':identity[2],'dimensions':list(struct.unpack_from('<2I',town,28)),'active':active,'profiles':profiles,'profile_payload_sha256':profile_hashes,'plots':plots,'terrain_sha256':terrain_hash,'houses_sha256':__import__('hashlib').sha256(files[names[1]][28:-4]).hexdigest(),'daylight_location':daylight}
                                if runtime is not None:candidate_details['npc_spaces']=[struct.unpack_from('<i',runtime,128+i*28)[0] for i in range(6)]
                                shop=files[names[2]]
                                if len(shop)==708 and struct.unpack_from('<I',shop,84)[0]==154:
                                    candidate_details['shop']={'stock':list(struct.unpack_from('<12i',shop,36)), 'state':list(struct.unpack_from('<154i',shop,88))}
                            # Malformed metadata must not promote an otherwise
                            # checksummed newest generation over a valid fallback.
                            best=identity[2];best_day=town_day;best_details=candidate_details
                    except (ValueError,KeyError,UnicodeError,struct.error):continue
            except (ValueError,UnicodeError,struct.error):continue
    if expected_day is not None and best_day!=expected_day:
        raise AssertionError(f'Explicit save did not retain the new rollover day: expected {expected_day}, got {best_day}')
    return best_details if details else best

def testing_menu():
    set_debug_visibility(True)
    def label(name,row):
        y=131+(row%9)*24
        for sample in range(90):
            screenshot(name+'.png')
            pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),
                '-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
            focused=sum(all(abs(pixels[(py*800+px)*3+c]-value)<=3
                for c,value in enumerate((221,231,188)))
                for py in range(y,y+21) for px in range(100,540,4))
            if focused>100:
                return b''.join(pixels[(py*800+85)*3:(py*800+575)*3] for py in range(y,y+21))
            if p.poll() is not None:raise RuntimeError('Game exited during Testing menu checks')
            time.sleep(1)
        raise AssertionError(f'Testing row {row} did not become available after landscape loading')
    card=root/'MemoryCardA.USA.raw'
    if committed_card_sequence(card)!=0:raise AssertionError('Fresh-town test found a save before any explicit action')
    # Save & begin is the first explicit save. Later menu/gameplay actions
    # must preserve that completed generation until Save now is selected.
    start_game(True,'testing')
    if committed_card_sequence(card)==0:raise AssertionError('Save & begin did not save the completed villager')
    before=card_digest()
    for _ in range(3):press('START');time.sleep(1);press('B')
    if card_digest()!=before:raise AssertionError('Opening Settings wrote to the memory card')
    press('START');screenshot('testing-settings-no-save.png')
    # Enter through the visible Settings action, without a hidden shortcut.
    for _ in range(7):press('D_DOWN')
    label('testing-settings-debug-entry',7);press('A')
    manual_only='--manual-save-only' in sys.argv
    previous=0
    # Include session weather, the staged previews, SDK overlay, and diagnostics.
    season_row,time_row=(testing_menu_row(option) for option in ('SeasonPreviewOption','TimePreviewOption'))
    rows=[testing_menu_row(option) for option in (0,2,3,11,16,17,35,'SeasonPreviewOption','TimePreviewOption',21,36,37,38)]
    for row in ([0] if manual_only else rows):
        for _ in range(row-previous):press('D_DOWN')
        original=label(f'testing-row-{row}-before',row)
        if row in (season_row,time_row):
            press('D_LEFT' if row==season_row else 'D_RIGHT')
            pending=label(f'testing-row-{row}-pending',row)
            press('A');applied=label(f'testing-row-{row}-applied',row)
            if original==pending or pending==applied:
                raise AssertionError(f'Preview row {row} did not distinguish pending and active choices')
            press('D_RIGHT' if row==season_row else 'X')
            restoring=label(f'testing-row-{row}-restoring',row)
            press('A');restored=label(f'testing-row-{row}-restore',row)
            if restoring==restored or original!=restored:
                raise AssertionError(f'Preview row {row} did not restore its original choice')
            previous=row
            continue
        press('A');changed=label(f'testing-row-{row}-after',row)
        if original==changed:raise AssertionError(f'Testing row {row} did not change its displayed state')
        press('A');restored=label(f'testing-row-{row}-restore',row)
        if original!=restored:raise AssertionError(f'Testing row {row} did not restore its initial state')
        previous=row
    if card_digest()!=before:raise AssertionError('Debug actions wrote to the memory card')
    press('B')
    wait_seconds=int(sys.argv[sys.argv.index('--manual-save-wait')+1]) if '--manual-save-wait' in sys.argv else 0
    if not 0<=wait_seconds<=600:raise ValueError('Manual save wait must be 0..600 seconds')
    # Keep the real simulation running past the old five-minute trigger.
    # The optional RTC fixture also crosses the 06:00 daily rollover.
    for second in range(wait_seconds):
        time.sleep(1)
        if p.poll() is not None:raise RuntimeError('Game exited during manual-save idle check')
        if (second+1)%30==0:
            if card_digest()!=before:raise AssertionError('Timed play or day rollover wrote to the memory card')
            print(f'Manual-save idle check: {second+1}/{wait_seconds}s; card unchanged',flush=True)
    if card_digest()!=before:raise AssertionError('Gameplay wrote to the card without Save now')
    screenshot('testing-manual-save-idle.png')
    previous=committed_card_sequence(card)
    save_menu()
    for sample in range(180):
        time.sleep(1)
        if committed_card_sequence(card)>previous:break
        if p.poll() is not None:raise RuntimeError('Game exited during explicit save')
    else:
        screenshot('testing-save-failure.png')
        raise AssertionError('Explicit Save now did not commit a complete checksummed save generation')
    label('testing-save-returned-to-settings',0)
    screenshot('testing-explicit-save.png')
    if '--rtc-rollover' in sys.argv:
        expected_day=int((datetime(2027,month,15,6,tzinfo=timezone.utc).timestamp()-6*3600)//86400)
        committed_card_sequence(card,expected_day=expected_day)
    saved=card_digest();press('B');press('START');press('B');time.sleep(5)
    if card_digest()!=saved:raise AssertionError('Closing/reopening Settings wrote another save')
    (root/'testing-menu.txt').write_text('Completed onboarding with explicit Save & begin; Settings never saved automatically.\nVisible Settings entry opened Debug / testing.\n'+('Performance display toggled/restored.\n' if manual_only else 'Debug toggles and staged season/time previews were applied and restored.\n')+f'{wait_seconds}s idle play preserved the card bytes.\n'+'Only Save now committed the next generation of records and their matching checksummed commit.\nReopening Settings left the saved generation unchanged.\n')

def building_view(name, indoors):
    screenshot(name)
    pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    target=(32,40,39) if indoors else (149,198,186)
    matches=sum(all(abs(pixels[(y*800+x)*3+c]-value)<=3 for c,value in enumerate(target))
                for y in range(80 if indoors else 4,300,4) for x in range(40,600,4))
    if p.poll() is not None or matches<500:
        raise AssertionError(f'Building scenario did not reach the expected {"interior" if indoors else "town"} view: {name}; matching background samples={matches}')

def building_save():
    save_menu();screenshot('building-saving.png')
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

def town_shop():
    """Use --shop-fixture records on a disposable card; real UI and native meshes."""
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    if not before or 'shop' not in before:raise AssertionError('Shop scenario requires the disposable --shop-fixture card')
    displays=sum(item!=0 for item in before['shop']['stock'])
    sales=3 if displays==6 else 5 if displays==9 else 7
    start_game(False,'shop')
    def inspect():
        os.write(controller,b'SET C .5 0\nPRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\nSET C .5 .5\n');time.sleep(1)
        check_runtime_failure()
    def close_speech():press('B');press('B')
    inspect();screenshot('shop-exterior.png');press('A');time.sleep(8);check_runtime_failure();screenshot('shop-interior.png')
    inspect();inspect();press('A');screenshot('shop-finite-price.png');press('A');close_speech()
    press('A');screenshot('shop-sold-out.png');press('B')
    # Fixture inspection uses the same collision-clear approach sockets as NPCs.
    for _ in range(displays+sales-1):inspect()
    press('A');screenshot('shop-cabinet.png');press('A');close_speech()
    press('A');press('A');close_speech()
    inspect();press('A');screenshot('shop-counter.png');press('B')
    for _ in range(displays+1):inspect()
    press('A');screenshot('shop-consignment.png');press('A');close_speech()
    inspect();press('A');press('A');press('D_UP');screenshot('shop-asking-price.png');press('A');close_speech()
    press('A');press('D_DOWN');press('A');close_speech()
    press('A');press('A');screenshot('shop-list-item.png');press('A');press('A');close_speech()
    for view in range(3):
        os.write(controller,b'SET C .9 .5\n');time.sleep(2);os.write(controller,b'SET C .5 .5\n');time.sleep(1);screenshot(f'shop-camera-{view}.png')
    press('L');sequence=committed_card_sequence(root/'MemoryCardA.USA.raw');save_menu();after=wait_saved_after({'sequence':sequence})
    screenshot('shop-saved.png');press('B');check_runtime_failure()
    stock=after['shop']['stock'];state=after['shop']['state'];profile=after['profiles'][0]
    if stock[0]!=0 or stock[1]!=0 or state[35]!=1 or after.get('npc_spaces',[24])[0]==24:raise AssertionError('Player/NPC did not exhaust distinct physical displays and leave the shop')
    if state[2]!=0 or state[5]!=before['shop']['stock'][1]:raise AssertionError('Consignment purchase, removal or new listing failed')
    prices={10:1200,11:2400,12:1600,13:3000,25:1800}
    expected=20000-prices[before['shop']['stock'][1]]-800-1800
    if profile['wallet']!=expected or profile['pockets'].count(1)!=2 or 25 not in profile['pockets'] or 11 not in profile['pockets']:
        raise AssertionError('Native purchases did not preserve exact wallet and inventory outcomes')
    if after['profiles'][1]['savings']!=before['profiles'][1]['savings']+1800:raise AssertionError('Inactive seller did not receive native consignment proceeds')
    (root/'shop-validation.json').write_text(json.dumps({'before':before,'after':after,'native_shop_ui':True,'cabinet_repeat_buys':2,'npc_bought_and_left':True,'camera_quarter_turns':3},indent=2)+'\n')

def card_dev_config(path):
    """Decode the checksummed named record, including consolidated card storage."""
    import struct
    if not path.exists():return None
    try:payload=options_card_files(path).get('dev_config')
    except AssertionError:return None
    if not payload or len(payload)<4 or payload[:3]!=b'ATD' or payload[3] not in (1,2,3,4,5,6,7):return None
    version=payload[3];checksum_at=32 if version==1 else 40 if version>=6 else 36
    count={1:27,2:29,3:30,4:31,5:32,6:34,7:34}[version]
    if len(payload)!=checksum_at+4 or any(payload[4+count:checksum_at]):return None
    if version>=7 and payload[34]!=0:return None
    checksum=2166136261
    for value in payload[:checksum_at]:checksum=((checksum^value)*16777619)&0xffffffff
    if checksum!=struct.unpack_from('<I',payload,checksum_at)[0]:return None
    return {'version':version,'values':list(payload[4:4+count]),'retired_terrain_cache_value':payload[33] if version>=3 else None,'full_resolution_aa':4<=version<=6 and payload[34]==1,'ground_mip_bands':version>=5 and payload[35]==1,'retired_warm_prefetch_value':payload[36] if version>=6 else None}

def aram_model_cycles(text,after):
    """Match real retire/restore pairs in the current town session only."""
    import re
    session=text.rfind('ANIMALTOWN GAME START')
    if session<0:raise AssertionError('No traced town session for model residency evidence')
    cold={};retired=set();cycles=[]
    for event in re.finditer(r'ANIMALTOWN MODEL (COLD|WARM) (\d+)',text[session:]):
        species=int(event[2]);offset=session+event.start()
        if event[1]=='COLD':cold[species]=offset;retired.add(species)
        elif species in cold:
            retired_at=cold.pop(species)
            if offset>=after:cycles.append({'species':species,'cold_offset':retired_at,'warm_offset':offset})
    restored={event['species'] for event in cycles}
    return {'session_start':session,'cycles':cycles,'cold_species':sorted(cold),
            'retired_species':sorted(retired),'restored_species':sorted(restored),
            'unrestored_species':sorted((retired-restored)|set(cold))}

def aram_model_population(text,player_species):
    """Read the actual player/local/legacy/visitor roster of this boot."""
    import re
    session=text.rfind('ANIMALTOWN GAME START')
    if session<0:raise AssertionError('No traced town session for model population')
    current=text[session:];markers=list(re.finditer(r'ANIMALTOWN MODEL POPULATION[^\r\n]*',current))
    if len(markers)!=1:raise AssertionError('Expected one actual model population marker in the current town session')
    marker=markers[0];match=re.fullmatch(r'ANIMALTOWN MODEL POPULATION mask (\d+) actors (\d+)',marker[0])
    if match is None:raise AssertionError('Malformed actual model population marker')
    mask,actors=map(int,match.groups());ready=current.find('ANIMALTOWN READY ')
    if (not 0<mask<64 or not 1<=actors<=8 or mask.bit_count()>actors or
            not 0<=player_species<6 or not mask&(1<<player_species) or ready<0 or marker.start()>=ready):
        raise AssertionError('Invalid or late actual model population roster')
    return {'session_start':session,'mask':mask,'actors':actors,
            'species':[species for species in range(6) if mask&(1<<species)]}

def aram_actor_view(text,population,fresh_defaults=False):
    """Read live actors, keeping saved-home records separate from visitors."""
    import re
    match=re.search(r'ANIMALTOWN JOB VIEW (\S+) ((?:-?\d+ ?){10})[\r\n]',text)
    if match is None or 'ANIMALTOWN JOB VIEW END' not in text[match.end():]:raise AssertionError('Incomplete live actor view')
    values=list(map(int,match[2].split()));homes={};saved={}
    if values[0] not in (0,1,2,3,4,16,17,18,19,20,21,24,25,26,*range(32,44)) or not 0<=values[1]<128*1024 or not 0<=values[2]<120*1024:
        raise AssertionError('Invalid live player location')
    for row in re.findall(r'ANIMALTOWN JOB NPC (\d+) (-?\d+) (-?\d+) (-?\d+) (\d+) (-?\d+) (-?\d+)',text):
        slot,space,x,z,busy,hx,hz=map(int,row);homes[slot]=(hx,hz);saved[slot]=(slot,slot,space,x,z,busy)
    rows=re.findall(r'ANIMALTOWN JOB ACTOR (\d+) (\d+) (-?\d+) (-?\d+) (-?\d+) (\d+)',text)
    source='actual actors'
    if not rows:
        # Frozen pre-diagnostic ISOs can be probed only on a fresh no-visitor
        # town whose five unchanged default local identities are unambiguous.
        if not fresh_defaults or population['mask']!=31 or population['actors']!=6 or not all(i in saved for i in range(5)):
            raise AssertionError('Live actor species records required for legacy, remapped or visitor rosters')
        rows=[saved[i] for i in range(5)];source='guarded fresh defaults'
    actors={}
    for row in rows:
        slot,species,space,x,z,busy=map(int,row)
        if (slot in actors or not 0<=slot<7 or species not in population['species'] or
                space not in (0,1,2,3,4,16,17,18,19,20,21,24,25,26,*range(32,44)) or
                not 0<=x<128*1024 or not 0<=z<120*1024 or busy not in (0,1)):
            raise AssertionError('Invalid live actor identity or location')
        actors[slot]={'slot':slot,'species':species,'room':space,'x':x/1024,'z':z/1024,'busy':bool(busy)}
    if len(actors)+1!=population['actors']:raise AssertionError('Live actor view disagrees with the actual population')
    return {'screen':match[1],'room':values[0],'x':values[1]/1024,'z':values[2]/1024,'actors':actors,'homes':homes,'source':source}

def terrain_residency_cycles(text,after=0):
    """Validate physical cache/source ownership and complete doorway restores."""
    import re
    session=text.rfind('ANIMALTOWN GAME START')
    if session<0:raise AssertionError('No traced town session for terrain residency evidence')
    begin=max(session,after);cache=None;floor=None;restored_floor=None;cycles=[];cancelled=0
    pattern=(r'ANIMALTOWN TERRAIN CACHE (?:SUSPENDED cache bytes (\d+) reclaimed bytes (\d+) archive bytes (\d+)'
             r'|READY cache bytes (\d+) archive bytes (\d+))'
             r'|ANIMALTOWN FLOOR (?:SUSPENDED owned bytes (\d+) -> (\d+) source bytes (\d+) reclaimed bytes (\d+)'
             r'|RESTORED season (\d+) slots (\d+) owned bytes (\d+) source bytes (\d+))')
    for event in re.finditer(pattern,text[begin:]):
        offset=begin+event.start()
        if event[1] is not None:
            entry={'offset':offset,'cache_bytes':int(event[1]),'reclaimed_bytes':int(event[2]),'archive_bytes':int(event[3])}
            if entry['cache_bytes']!=0 or entry['reclaimed_bytes']!=1048576 or entry['archive_bytes']<=0:
                raise AssertionError('Terrain suspension did not physically reclaim exactly 1 MiB while retaining its archive')
            if cache is not None:
                if cache['archive_bytes']!=entry['archive_bytes']:raise AssertionError('Interrupted terrain restore changed its retained archive')
                cancelled+=1
            cache=entry
        elif event[6] is not None:
            entry={'offset':offset,'owned_before':int(event[6]),'owned_after':int(event[7]),
                   'source_bytes':int(event[8]),'reclaimed_bytes':int(event[9])}
            if entry['owned_before']<=0 or entry['owned_after']!=0 or entry['source_bytes']!=0 or entry['reclaimed_bytes']<entry['owned_before']:
                raise AssertionError('Floor suspension retained native source bytes or failed to release its owning leases')
            floor=entry;restored_floor=None
        elif event[10] is not None:
            entry={'offset':offset,'season':int(event[10]),'slots':int(event[11]),
                   'owned_bytes':int(event[12]),'source_bytes':int(event[13])}
            if not 0<=entry['season']<=3 or entry['slots']<=0 or entry['owned_bytes']<=0 or entry['source_bytes']!=entry['owned_bytes']:
                raise AssertionError('Floor restore did not publish matching real owner/source residency')
            if floor is not None:restored_floor=entry
        else:
            if cache is None:raise AssertionError('Terrain cache restore has no matching suspension in the observed route')
            ready={'offset':offset,'cache_bytes':int(event[4]),'archive_bytes':int(event[5])}
            if ready['cache_bytes']!=1048576 or ready['archive_bytes']!=cache['archive_bytes']:
                raise AssertionError('Terrain cache restore changed the retained archive or failed to allocate exactly 1 MiB')
            if floor is None or restored_floor is None or restored_floor['offset']>=offset:
                raise AssertionError('Terrain cache became ready before its floor sources were restored')
            cycles.append({'suspended':cache,'floor_suspended':floor,'floor_restored':restored_floor,'restored':ready})
            cache=floor=restored_floor=None
    return {'session_start':session,'cycles':cycles,'pending_cache':cache,'pending_floor':floor,'cancelled_restores':cancelled}

def aram_door_state(pixels):
    # A refusal bubble can cover an otherwise valid town view. Its broad body
    # extends left of the compact door cue; snow and UI grain are not cream.
    dialogue=region_color_count(pixels,(255,245,221),70,350,170,390)>1500
    action=not dialogue and region_color_count(pixels,(53,105,84),200,328,270,353)>150
    # Home names give an inspector acknowledgement without comparing animated
    # residents, moving shadows or the floating clock elsewhere in the scene.
    signature=bytes(max(pixels[(y*800+x)*3:(y*800+x)*3+3])<150
                    for y in range(326,349) for x in range(250,410)) if action else None
    return {'dialogue':dialogue,'action_signature':signature}

def aram_inspector_arrival(before,after):
    previous,current=aram_door_state(before),aram_door_state(after)
    if current['dialogue'] or current['action_signature'] is None:
        raise AssertionError('Building inspector did not reach an actionable world doorway')
    if previous['action_signature']==current['action_signature']:
        raise AssertionError('Building inspector did not acknowledge a different resident home')

def aram_residency_scenario():
    """Default-on physical memory reclamation and real doorway/model cycles."""
    import re
    global p
    sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(user/'Dump/Frames',track_presentations=True)
    card=root/'MemoryCardA.USA.raw';segments=[];report={'segments':segments,'clock':'NTSC VI chunks; software Dolphin, not hardware'}
    building_inspections=0
    def logs():return runtime_log_text()
    def alive():
        check_runtime_failure()
        if p.poll() is not None:raise AssertionError('Emulator stopped during ARAM residency scenario')
    def uncovered_models(evidence,population):
        # Prove every retired species can restore after actual gameplay demand.
        # A later retirement is correct for an unseen model, not lost coverage.
        return sorted(set(evidence['retired_species'])&set(population['species'])-
                      {event['species'] for event in evidence['cycles']})
    def wait_log(pattern,after=0,seconds=180):
        deadline=time.monotonic()+seconds
        while time.monotonic()<deadline:
            alive();match=re.search(pattern,logs()[after:])
            if match:return match
            time.sleep(.2)
        screenshot('aram-marker-timeout.png');raise AssertionError('Missing ARAM runtime marker: '+pattern)
    def guest(name,seconds):
        start=clock.frames();target=start+int(seconds*60000/1001+.999);deadline=time.monotonic()+max(60,seconds*20)
        while clock.frames()<target:
            alive()
            if time.monotonic()>deadline:raise AssertionError('Guest VI clock stopped during '+name)
            time.sleep(.1)
        end=clock.frames();present=[value for value in clock.presentations if start<value<=end]
        gaps=sorted((b-a)*1001/60 for a,b in zip(present,present[1:]))
        percentile=lambda q:gaps[min(len(gaps)-1,int((len(gaps)-1)*q))] if gaps else None
        segments.append({'name':name,'start_vi':start,'end_vi':end,'guest_seconds':(end-start)*1001/60000,'presentations':len(present),'frame_ms_p50':percentile(.5),'frame_ms_p95':percentile(.95),'frame_ms_max':max(gaps) if gaps else None})
        (root/'aram-residency.json').write_text(json.dumps(report,indent=2))
    def view(name,indoors):
        deadline=time.monotonic()+120
        while time.monotonic()<deadline:
            alive()
            try:
                building_view(name+'.png',indoors)
                # The dot loading screen shares the town's mint clear color.
                # It must not count as a restored, drawable outdoor scene.
                pixels=rgb(name+'.png')
                if indoors or color_count(pixels,(149,198,186))<150000:return
            except AssertionError:time.sleep(.5)
            time.sleep(.2)
        raise AssertionError('ARAM door did not reach '+name)
    def release_controls():
        os.write(controller,b'RELEASE A\nRELEASE B\nRELEASE L\nRELEASE R\nSET MAIN .5 .5\nSET C .5 .5\n')
    def clear_door_refusal(name,indoors):
        release_controls();view(name,indoors)
        pixels=rgb(name+'.png')
        if aram_door_state(pixels)['dialogue']:
            # B reveals an unfinished line, then closes it. It cannot request a
            # second crossing or advance the home inspector as A would.
            press_presented('B',clock);press_presented('B',clock)
            view(name,indoors);pixels=rgb(name+'.png')
            if aram_door_state(pixels)['dialogue']:raise AssertionError('Transient doorway refusal did not close')
            report.setdefault('cleared_door_refusals',[]).append(name)
        return pixels
    def inspect(indoors=False):
        nonlocal building_inspections
        name='aram-inspector-'+str(building_inspections)+('-exit' if indoors else '-home')
        clear_door_refusal(name+'-before',indoors)
        # Portal cooldown runs in simulation time. Spend it in the world after
        # closing speech, rather than assume wall time or a drawn exterior does.
        guest(name+'-cooldown',3)
        before=clear_door_refusal(name+'-ready',indoors)
        os.write(controller,b'PRESS L\nPRESS R\n')
        try:press_presented('A',clock)
        finally:release_controls()
        view(name+'-arrived',indoors);after=rgb(name+'-arrived.png')
        if indoors:
            if aram_door_state(after)['action_signature'] is None:raise AssertionError('Indoor inspector did not reach the real exit')
        else:
            aram_inspector_arrival(before,after)
            building_inspections+=1
            report.setdefault('acknowledged_home_inspections',[]).append(building_inspections)
    def actor_view():
        release_controls();offset=len(logs());os.write(controller,b'PRESS L\nPRESS R\n')
        try:press_presented('Y',clock)
        finally:release_controls()
        wait_log('ANIMALTOWN JOB VIEW END',offset)
        return aram_actor_view(logs()[offset:],population,fresh_defaults)
    def restore_live_actor(species,name):
        import math
        route={'species':species,'observations':[]};report.setdefault('live_actor_routes',[]).append(route)
        previous=None;stalls=0
        for attempt in range(80):
            current=actor_view()
            candidates=[actor for actor in current['actors'].values() if actor['species']==species]
            if not candidates:raise AssertionError('Retired species has no actual present actor: '+str(species))
            actor=min(candidates,key=lambda a:(a['room']!=current['room'],(a['x']-current['x'])**2+(a['z']-current['z'])**2))
            route['observations'].append({'player':{k:current[k] for k in ('room','x','z')},'actor':actor,'source':current['source']})
            if species not in uncovered_models(aram_model_cycles(logs(),baseline),population):
                route['restored']=True;screenshot(name+'-live-restored.png')
                if current['room']!=0:leave(name+'-live-returned')
                return
            if current['screen']!='world':press_presented('B',clock);press_presented('B',clock);continue
            if current['room']!=actor['room']:
                if current['room']!=0:
                    inspect(True);press_presented('A',clock);view(name+'-live-exit',False);continue
                if 16<=actor['room']<=21:
                    enter(name+'-live-home-'+str(attempt),target_species=actor['room']-16);continue
                if actor['room']==24:
                    os.write(controller,b'SET C .5 0\nPRESS L\nPRESS R\n')
                    try:press_presented('A',clock)
                    finally:release_controls()
                    view(name+'-live-shop',False);press_presented('A',clock);view(name+'-live-room',True);continue
                if actor['room']!=25:raise AssertionError('Unsupported actual actor space: '+str(actor['room']))
                target=(50.5,29.4)
            else:target=(actor['x'],min(119.5,actor['z']+5)) if current['room']==0 else (actor['x'],actor['z'])
            dx,dz=target[0]-current['x'],target[1]-current['z'];distance=math.hypot(dx,dz)
            if actor['room']==25 and current['room']==0 and distance<.5:
                os.write(controller,b'SET MAIN .5 .8\n');guest(name+'-live-face',.05);release_controls();press_presented('A',clock);view(name+'-live-post',True);continue
            position=(current['x'],current['z']);stalls=stalls+1 if previous is not None and math.dist(position,previous)<.08 else 0;previous=position
            if stalls>12:raise AssertionError('Ordinary walking could not approach actual actor: '+str(actor))
            if distance<.2:guest(name+'-live-ready',.5);continue
            if stalls:dx,dz=(-dz,dx) if stalls%4<2 else (dz,-dx)
            os.write(controller,f'SET MAIN {.5+dx/distance*.5} {.5-dz/distance*.5}\nPRESS B\n'.encode())
            try:guest(name+'-live-walk',min(.7,max(.08,distance/5.04)))
            finally:release_controls()
        raise AssertionError('No genuine gameplay restore after following actual actor: '+str(species))
    def enter(name,target_species=None):
        if target_species is not None:
            # The existing inspector cycles the six actual resident homes in
            # species order. Tour real camera locations to the cold target.
            for step in range((target_species-building_inspections)%6):
                inspect();view(name+'-camera-'+str(step),False)
        inspect();view(name+'-outside',False);press('A');view(name+'-inside',True)
        # The crossing lands 0.7 m from the exit. Walk into the room so the
        # early-exit prefetch guard does not deliberately keep outdoor models hot.
        os.write(controller,b'SET MAIN .5 .8\n')
        try:guest(name+'-walk-away-from-door',1)
        finally:os.write(controller,b'SET MAIN .5 .5\n')
        view(name+'-away-from-door',True)
    def leave(name,reversal=False):
        inspect(True) # the existing indoor inspector focuses the real exit socket
        started=time.monotonic();start_vi=clock.frames()
        os.write(controller,b'PRESS A\n');time.sleep(1);os.write(controller,b'RELEASE A\n')
        if reversal:
            # Try to reverse movement/action while the existing portal owns the
            # actor; this must neither publish a partial restore nor strand it.
            os.write(controller,b'SET MAIN .5 .8\n');guest(name+'-reversal-input',.3);os.write(controller,b'SET MAIN .5 .5\n');press('A')
        view(name,False);report[name+'_wall_seconds']=time.monotonic()-started
        report[name+'_guest_seconds']=(clock.frames()-start_vi)*1001/60000
        clear_door_refusal(name+'-world',False)
        guest(name+'-outdoor-settle',2)

    wait_title();initial=card_dev_config(card);report['initial_dev_config']=initial
    fresh_defaults=not committed_card_sequence(card)
    start_game(False,'aram');set_debug_visibility(True)
    startup=[]
    for index,session in enumerate(logs().split('ANIMALTOWN GAME START')[1:]):
        terrain=session.find('ANIMALTOWN STARTUP TERRAIN READY')
        building_begin=session.find('ANIMALTOWN BUILDINGS LOAD BEGIN')
        building_ready=session.find('ANIMALTOWN BUILDINGS LOAD READY')
        admissions=list(re.finditer(r'ANIMALTOWN MODEL LOAD BEGIN (\d+) ',session))
        model=admissions[0].start() if admissions else -1
        bake=session.find('ANIMALTOWN TERRAIN BAKE READY')
        if not 0<=bake<terrain<building_begin<building_ready<model:
            raise AssertionError('Session '+str(index)+' did not retire terrain scratch and compact the building kit before species admission')
        player=re.search(r'ANIMALTOWN PLAYER MODEL READY (\d+) ',session)
        first_ready=session.find('ANIMALTOWN MODEL LOAD READY '+admissions[0][1]+' ') if admissions else -1
        if (len(admissions)!=6 or len({event[1] for event in admissions})!=6 or player is None
                or player[1]!=admissions[0][1] or not model<player.start()<first_ready<admissions[1].start()):
            raise AssertionError('Session '+str(index)+' did not reserve the selected player instance before the remaining five species')
        startup.append({'session':index,'first_species':int(admissions[0][1]),
                        'bake_ready_before_terrain_ready_before_building_kit_before_first_model':True,
                        'selected_player_ready_before_other_species':True})
    if not startup:raise AssertionError('No traced startup ordering was observed')
    report['startup_order']=startup
    session_start=logs().rfind('ANIMALTOWN GAME START')
    wait_log(r'ANIMALTOWN READY ',session_start,seconds=300)
    population=aram_model_population(logs(),startup[-1]['first_species']);report['model_population']=population
    wait_log(r'Terrain archive: [0-9]+ ARAM bytes',session_start,seconds=300)
    wait_log(r'ANIMALTOWN ONBOARDING RELEASED',session_start,seconds=120)
    view('aram-game-started',False)
    baseline=cold=len(logs());enter('aram-default');guest('default-indoor-steady',16)
    wait_log(r'ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes (\d+) reclaimed bytes (\d+) archive bytes (\d+)',cold)
    wait_log(r'ANIMALTOWN FLOOR SUSPENDED owned bytes (\d+) -> (\d+) source bytes (\d+) reclaimed bytes (\d+)',cold)
    eviction=terrain_residency_cycles(logs(),cold)
    if eviction['pending_cache'] is None or eviction['pending_floor'] is None:raise AssertionError('Default indoor idle did not reclaim both terrain and floor sources')
    if 'ANIMALTOWN FLOOR RESTORED' in logs()[cold:]:raise AssertionError('Idle indoor scene restored unneeded outdoor floor sources')
    report['default_reclamation_without_toggle']=True
    # This assertion is independent of terrain suspension: automatic model paging
    # must actually retire at least one cold shared species, not only plan it.
    # Startup may already have retired it in this session before the first door.
    model=wait_log(r'ANIMALTOWN MODEL COLD (\d+) aram bytes (\d+) native budget bytes (\d+) max advance us (\d+)',session_start)
    if int(model[2])<=0:raise AssertionError('Cold model has no ARAM package backing')
    report['suspended']=eviction['pending_cache'];report['floor_suspended']=eviction['pending_floor']
    report['model_cold']={'species':int(model[1]),'aram_bytes':int(model[2]),'native_bytes':int(model[3]),'max_advance_us':int(model[4])}
    screenshot('aram-default-cold-indoor.png');warm=len(logs());leave('aram-restored-exterior',reversal=True)
    wait_log(r'ANIMALTOWN TERRAIN CACHE READY cache bytes (\d+) archive bytes (\d+)',warm)
    restored=terrain_residency_cycles(logs(),cold)
    if not restored['cycles']:raise AssertionError('The doorway did not restore its floor sources and unchanged terrain archive')
    report['restored']=restored['cycles'][-1]['restored'];report['floor_restored']=restored['cycles'][-1]['floor_restored']
    # Back-to-back crossings exercise drain/cancel and reuse of restored backing.
    rapid=len(logs());enter('aram-rapid-reentry');guest('rapid-reentry-indoor',3)
    wait_log(r'ANIMALTOWN FLOOR SUSPENDED owned bytes (\d+) -> (\d+) source bytes (\d+) reclaimed bytes (\d+)',rapid)
    leave('aram-rapid-return',reversal=True)
    rapid_cycles=terrain_residency_cycles(logs(),rapid)
    if not rapid_cycles['cycles']:raise AssertionError('Rapid reentry/reversal did not complete a second real memory reclamation cycle')
    report['rapid_reversal_cycles']=rapid_cycles['cycles']
    evidence=aram_model_cycles(logs(),baseline);toured=set();report['extra_cold_home_routes']=[]
    while uncovered_models(evidence,population):
        candidates=[species for species in uncovered_models(evidence,population) if species not in toured]
        if not candidates:raise AssertionError('Cold species lacked a real gameplay-demand restore: '+str(uncovered_models(evidence,population)))
        species=candidates[0];toured.add(species);name='aram-cold-species-'+str(species)
        route={'species':species,'resident_building':species+1};report['extra_cold_home_routes'].append(route)
        if species not in evidence['cold_species']:
            # A startup-only restore is insufficient. Idle in a different real
            # home to retire that shared model before demanding it during play.
            neutral=(species+1)%6;route['staging_resident_building']=neutral+1
            enter(name+'-staging',target_species=neutral);guest(name+'-staging-idle',16);leave(name+'-staging-exited')
            evidence=aram_model_cycles(logs(),baseline)
            if species not in uncovered_models(evidence,population):continue
        enter(name,target_species=species);guest(name+'-indoor',1);leave(name+'-returned')
        evidence=aram_model_cycles(logs(),baseline)
        if species in uncovered_models(evidence,population):
            restore_live_actor(species,name);evidence=aram_model_cycles(logs(),baseline)
    if evidence['session_start']!=session_start:raise AssertionError('Town session changed during residency acceptance')
    if not evidence['retired_species']:raise AssertionError('No actual model retirement was observed in the current town session')
    if not set(evidence['retired_species'])&set(population['species']):raise AssertionError('Only unused species packages retired; no present actor demonstrated paging')
    report['cold_species_restored_before_exterior_return']=[event['species'] for event in evidence['cycles']]
    report['cold_to_warm_after_gameplay_demand']=evidence['cycles']
    report['all_cold_species_restored_after_gameplay_demand']=not uncovered_models(evidence,population)
    report['currently_cold_species_after_routes']=evidence['cold_species']
    report['unused_retired_species']=sorted(set(evidence['retired_species'])-set(population['species']))
    report['unused_retired_species_still_cold']=sorted(set(report['unused_retired_species'])&set(evidence['cold_species']))
    report['runtime_residency_errors']=aram_residency_errors(logs())
    report['terrain_floor_cycles']=terrain_residency_cycles(logs(),baseline)
    report['runtime_markers']=re.findall(r'ANIMALTOWN (?:MODEL [^\r\n]+|TERRAIN CACHE [^\r\n]+|FLOOR [^\r\n]+)',logs()[session_start:])
    (root/'aram-runtime.log').write_text(logs())
    # Restart the same ISO/card without opening a debug configuration menu. The
    # retired stored slot must not disable the automatic memory policy.
    p.terminate();p.wait(timeout=10)
    frames=user/'Dump/Frames'
    if frames.exists():frames.rename(user/'Dump/Frames-residency')
    command=['/usr/games/dolphin-emu-nogui','-u',str(user),'-e',str(disc),'-v','OGL']
    if '--component-cable' in sys.argv:command+=['-C','SYSCONF.IPL.PGS=True']
    p=subprocess.Popen(command,env=env,stdout=log,stderr=subprocess.STDOUT)
    clock=AviGuestClock(frames,track_presentations=True);building_inspections=0
    wait_title();restarted=card_dev_config(card)
    if initial is not None and (restarted is None or restarted['values']!=initial['values']):
        raise AssertionError('Automatic residency acceptance unexpectedly changed persisted developer settings')
    report['restart_dev_config']=restarted
    start_game(False,'aram-restart');set_debug_visibility(True)
    report['restart_model_population']=aram_model_population(logs(),startup[-1]['first_species'])
    restart_cold=len(logs());enter('aram-restart-default');guest('restart-default-indoor-steady',6)
    wait_log(r'ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes (\d+) reclaimed bytes (\d+) archive bytes (\d+)',restart_cold)
    wait_log(r'ANIMALTOWN FLOOR SUSPENDED owned bytes (\d+) -> (\d+) source bytes (\d+) reclaimed bytes (\d+)',restart_cold)
    restarted_eviction=terrain_residency_cycles(logs(),restart_cold)
    if restarted_eviction['pending_cache'] is None or restarted_eviction['pending_floor'] is None:
        raise AssertionError('Automatic terrain/floor reclamation did not survive a clean boot')
    leave('aram-restart-restored-exterior',reversal=True)
    report['restart_terrain_floor_cycles']=terrain_residency_cycles(logs(),restart_cold)
    if not report['restart_terrain_floor_cycles']['cycles']:raise AssertionError('Post-reboot doorway restoration did not complete')
    report['default_after_restart_verified']=True
    screenshot('aram-restart-complete.png')
    (root/'aram-residency.json').write_text(json.dumps(report,indent=2))

def dialogue_cycles():
    import json
    def pixels(name):
        screenshot(name+'.png')
        return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
    def body(frame):return b''.join(frame[(row*800+78)*3:(row*800+540)*3] for row in range(344,414))
    def returned(frame):
        return sum(all(abs(frame[(row*800+col)*3+c]-target)<=3 for c,target in enumerate((221,231,188))) for row in range(160,181) for col in range(100,540,4))>100
    results=[]
    press('A');press('START');press('D_DOWN')
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
    save_menu();time.sleep(12);screenshot('dialogue-cycles-save.png')

def item_navigation(action_count=3):
    # Disposable fixture, pocket zero: Work shirt (3 rows) or 1000 acorns (4).
    results=[]
    def read(name):
        screenshot(name+'.png')
        pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/(name+'.png')),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
        scores=[sum(fill_matches(pixels[(y*800+x)*3:(y*800+x)*3+3],(221,231,188)) for y in range(187+i*18,204+i*18) for x in range(200,330,2)) for i in range(action_count)]
        return scores.index(max(scores)) if max(scores)>30 else -1
    def focus(name,expected):
        actual=read(name);results.append((name,actual==expected));print(f'{name}: expected {expected}, actual {actual}',flush=True)
    press('A');focus('actions-initial',0)
    press('D_DOWN');focus('actions-dpad-down',1)
    press('D_UP');focus('actions-dpad-up',0)
    # Dolphin allows one physical key to drive both mappings. Send both in the
    # same update, then release both, and require a visible navigation response.
    os.write(controller,b'PRESS D_DOWN\nSET MAIN 0.5 0.1\n');time.sleep(.18)
    os.write(controller,b'RELEASE D_DOWN\nSET MAIN 0.5 0.5\n');time.sleep(2)
    paired=read('actions-paired-input');results.append(('paired-input',paired==1));print(f'paired input row: {paired}',flush=True)
    press('B');press('A');focus('actions-reopened',0)
    os.write(controller,b'SET MAIN 0.5 0.1\n');samples=[]
    try:
        for n,interval in enumerate((.07,.17,.29,.11,.23,.37)):time.sleep(interval);samples.append(read(f'actions-stick-{n}'))
    finally:os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2)
    results.append(('stick-progress',all(row>=0 for row in samples) and len(set(samples))>1));print(f'action stick rows: {samples}',flush=True)
    press('B');press('A');focus('actions-reset',0)
    press('D_UP');focus('actions-wrap-up',action_count-1)
    press('D_DOWN');focus('actions-wrap-down',0)
    press('B');screenshot('actions-back.png')
    (root/'item-navigation.txt').write_text('\n'.join(f'{name}: {passed}' for name,passed in results)+f'\nPaired row: {paired}\nStick rows: {samples}\n')
    if not all(passed for _,passed in results):raise AssertionError('Item action navigation failed; see item-navigation.txt')

def settings_navigation():
    set_debug_visibility(True)
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
    press('D_DOWN');press('A');screenshot('settings-about.png');press('B');press('B');focus('settings-about-return',3)
    press('D_UP');focus('settings-dpad-up',2)
    press('A');screenshot('settings-controls.png');press('B');press('B');focus('settings-controls-return',2)
    press('D_UP');press('D_UP');press('D_UP');focus('settings-wrap',7)
    press('R');focus('testing-entry',0)
    press('A');focus('testing-performance-on',0)
    press('A');focus('testing-performance-off',0)
    press('D_DOWN');press('D_DOWN');focus('testing-uv-row',2)
    press('A');focus('testing-uv-colors-on',2)
    press('A');focus('testing-uv-colors-off',2)
    press('L');focus('settings-tab-return',0)
    press('B');screenshot('settings-close.png')
    press('X');press('L');press('L');focus('settings-backpack-entry',0)
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
MemcardBPath = '''+str(root/'MemoryCardB.USA.raw')+'''
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
if '--jobs' in sys.argv and (root/'MemoryCardB.USA.raw').is_file():
    config=user/'Config'/'Dolphin.ini';config.write_text(config.read_text().replace('SlotB = 255','SlotB = 1'))
if '--online-play' in sys.argv:
    if not os.environ.get('GITHUB_ACTIONS'):raise RuntimeError('Public online target qualification runs on the independent CI runner')
    config=user/'Config'/'Dolphin.ini';config.write_text(config.read_text().replace('SkipIPL = True','SkipIPL = True\nSerialPort1 = 12'))
if '--save-cards' in sys.argv:
    mode=sys.argv[sys.argv.index('--save-cards')+1]
    config=user/'Config'/'Dolphin.ini';text=config.read_text()
    if mode in ('slot-b','no-card'):text=text.replace('SlotA = 1','SlotA = 255')
    if mode!='no-card':text=text.replace('SlotB = 255','SlotB = 1')
    config.write_text(text)
if '--season' in sys.argv:
    season=sys.argv[sys.argv.index('--season')+1]
    month={'spring':4,'summer':7,'fall':10,'winter':1}[season]
    rtc=int(datetime(2027,month,15,5,59,30,tzinfo=timezone.utc).timestamp()) if '--rtc-rollover' in sys.argv else int(datetime(2027,month,15,12,tzinfo=timezone.utc).timestamp())
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text().replace('SkipIPL = True\n',f'SkipIPL = True\nEnableCustomRTC = True\nCustomRTCValue = {rtc}\n'))
if '--jobs' in sys.argv and sys.argv[sys.argv.index('--jobs')+1] in ('letter','letter-full'):
    import re
    rtc=int(datetime(2027,7,15,7,58,tzinfo=timezone.utc).timestamp());config=user/'Config'/'Dolphin.ini'
    text=re.sub(r'^(EnableCustomRTC|CustomRTCValue) = .*\n','',config.read_text(),flags=re.MULTILINE)
    config.write_text(text.replace('SkipIPL = True\n',f'SkipIPL = True\nEnableCustomRTC = True\nCustomRTCValue = {rtc}\n'))
if '--dsp-hle' in sys.argv:
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text().replace('DSPHLE = False','DSPHLE = True'))
if '--daylight-location' in sys.argv or '--daylight-clock' in sys.argv:
    import re
    if '--daylight-location' in sys.argv:
        when=datetime(2026,11,19,17,30,tzinfo=timezone.utc)
    else:
        value=sys.argv[sys.argv.index('--daylight-clock')+1]
        if value not in ('06:30','12:00','18:00','20:00'):raise ValueError('Unsupported daylight capture clock')
        hour,minute=map(int,value.split(':'));when=datetime(2027,4,15,hour,minute,tzinfo=timezone.utc)
    rtc=int(when.timestamp())
    config=user/'Config'/'Dolphin.ini';text=config.read_text()
    text=re.sub(r'^(EnableCustomRTC|CustomRTCValue) = .*\n','',text,flags=re.MULTILINE)
    config.write_text(text.replace('SkipIPL = True\n',f'SkipIPL = True\nEnableCustomRTC = True\nCustomRTCValue = {rtc}\n'))
# Full hashing observes sparse live I4/IA4 edits; sampled hashes can retain
# an old mask even after GX texture-cache invalidation.
(user/'Config'/'GFX.ini').write_text('''[Settings]
InternalResolution = 1
MSAA = 1
SafeTextureCacheColorSamples = 0
EFBScale = 1
DumpFrames = True
DumpFormat = avi
DumpCodec = ffv1
[Hacks]
EFBToTextureEnable = False
''')
if '--profile' in sys.argv or '--interaction-profile' in sys.argv or '--runtime-profile' in sys.argv or '--terrain-scroll' in sys.argv or '--lighting' in sys.argv or '--actor-shadows' in sys.argv or '--movement-profile' in sys.argv or '--stream-travel' in sys.argv or '--submissions' in sys.argv or '--aram-residency' in sys.argv or '--player-chat' in sys.argv or '--jobs' in sys.argv or '--villager-visuals' in sys.argv or '--online-play' in sys.argv:
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text()+'\n[Movie]\nDumpFrames = True\nDumpFramesSilent = True\n')
    config=user/'Config'/'GFX.ini'
    config.write_text(config.read_text().replace('DumpCodec = ffv1','DumpCodec = ffv1\nDumpEncoder = ffv1\nDumpPixelFormat = bgr0'))
(user/'Logs').mkdir(exist_ok=True)
(user/'Config'/'Logger.ini').write_text('[Options]\nWriteToFile = True\nWriteToConsole = True\nVerbosity = 3\n[Logs]\nBOOT = True\nCORE = True\nPOWERPC = True\nOSREPORT = True\nOSREPORT_HLE = True\nDVDINTERFACE = True\n')
xvfb,env=start_display()
def close_display():
    xvfb.terminate()
    try:xvfb.wait(timeout=5)
    except subprocess.TimeoutExpired:xvfb.kill();xvfb.wait()
atexit.register(close_display)
screenshot('display-ready.png') # Prove an actual X11 capture before booting.
with (root/'emulator.log').open('w') as log:
    command=['/usr/games/dolphin-emu-nogui','-u',str(user),'-e',str(disc),'-v','OGL']
    if '--component-cable' in sys.argv:command+=['-C','SYSCONF.IPL.PGS=True']
    p=subprocess.Popen(command,env=env,stdout=log,stderr=subprocess.STDOUT)
    try:
        if '--loading' in sys.argv:
            for n,seconds in enumerate((1,1,2,4,8,12,17)):
                time.sleep(seconds);screenshot(f'loading-{n}.png')
        elif any(flag in sys.argv for flag in ('--online-play','--villager-visuals','--jobs','--online-review','--aram-residency','--title-logo','--terrain-materials','--save-cards','--save-and-quit','--daylight-clock','--daylight-location','--player-chat')):wait_title()
        else:time.sleep(45)
        if p.poll() is not None:
            raise RuntimeError(f'Dolphin exited during boot ({p.returncode}); see {root / "emulator.log"}')
        screenshot('screen.png')
        if any(flag in sys.argv for flag in ('--title-menu','--title-load','--title-config','--title-update','--title-options','--title-options-load','--title-options-unsupported')):wait_title()
        if '--online-play' in sys.argv:
            import importlib.util
            online_spec=importlib.util.spec_from_file_location('online_play_smoke',str(Path(__file__).with_name('online-play-smoke.py')))
            online_module=importlib.util.module_from_spec(online_spec);online_spec.loader.exec_module(online_module)
            try:online_module.run(globals())
            finally:online_shutdown=stop_dolphin(p,timeout_seconds=30)
            online_module.finish(globals(),online_shutdown)
        elif '--online-review' in sys.argv:
            import importlib.util
            review_spec=importlib.util.spec_from_file_location('online_review_smoke',str(Path(__file__).with_name('online-review-smoke.py')))
            review_module=importlib.util.module_from_spec(review_spec);review_spec.loader.exec_module(review_module);review_module.run(globals())
        elif '--jobs' in sys.argv:
            run_jobs(globals())
        elif '--player-chat' in sys.argv:player_chat()
        elif '--daylight-location' in sys.argv:daylight_location()
        elif '--daylight-clock' in sys.argv:daylight_clock()
        elif '--save-cards' in sys.argv:memory_cards()
        elif '--save-and-quit' in sys.argv:save_and_quit()
        elif '--villager-visuals' in sys.argv:villager_visuals()
        elif '--onboarding' in sys.argv:
            onboarding()
        elif '--debug-visibility' in sys.argv:debug_visibility_scenario()
        elif '--debug-visibility-load' in sys.argv:debug_visibility_scenario(True)
        elif '--aram-residency' in sys.argv:
            aram_residency_scenario()
        elif '--profile-delete' in sys.argv:
            delete_profile()
        elif '--profile-cards' in sys.argv:
            profile_cards()
        elif '--shared-profiles' in sys.argv:
            shared_profiles()
        elif '--title-logo' in sys.argv:
            title_logo()
        elif '--terrain-materials' in sys.argv:
            terrain_materials()
        elif '--title-menu' in sys.argv:
            title_menu()
        elif '--title-config' in sys.argv:
            set_debug_visibility(True)
            press('D_UP');press('A');wait_title(modal=True);screenshot('title-dev-config-loaded.png');press('B');wait_title();screenshot('title-modal-closed.png')
        elif '--title-load' in sys.argv:
            set_debug_visibility(True)
            press('D_UP');press('A');wait_title(modal=True);screenshot('title-dev-config-loaded.png');press('B');wait_title();press('D_DOWN');press('A');wait_profiles('title-loaded-villager-picker.png');press('A');time.sleep(180);screenshot('title-continued-game.png')
        elif '--title-update' in sys.argv:
            press('D_DOWN');press('A');time.sleep(20);screenshot('title-updater.png')
        elif '--title-options' in sys.argv:title_options()
        elif '--title-options-load' in sys.argv:title_options_load()
        elif '--title-options-unsupported' in sys.argv:title_options_unsupported()
        elif '--submission-seed' in sys.argv:
            start_game(True,'submission-seed')
            for _ in range(90):
                time.sleep(1)
                if committed_card_sequence(root/'MemoryCardA.USA.raw')>0:break
            else:raise AssertionError('Disposable fixture seed failed to save')
            screenshot('submission-seed.png')
        elif '--submissions' in sys.argv:
            submission_scenario()
        elif '--cliff-minimap' in sys.argv:
            cliff_minimap()
        elif '--testing-menu' in sys.argv:
            testing_menu()
        elif '--building-reload' in sys.argv:
            press('A');time.sleep(5);building_view('building-reloaded-interior.png',True)
        elif '--shop' in sys.argv:town_shop()
        elif '--shop-load' in sys.argv:
            start_game(False,'shop-reload');check_runtime_failure();screenshot('shop-reloaded.png')
            after=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
            if after['shop']['stock'][1]!=0 or after['shop']['state'][35]!=1:raise AssertionError('Reload lost shop depletion or NPC purchase')
        elif '--buildings' in sys.argv:
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n')
            time.sleep(3);building_view('building-exterior.png',False)
            time.sleep(10);screenshot('building-npc-journey.png')
            os.write(controller,b'PRESS A\n');time.sleep(.5);screenshot('building-door-opening.png');os.write(controller,b'RELEASE A\n')
            time.sleep(4);building_view('building-interior-0.png',True)
            for view in range(1,5):
                os.write(controller,b'SET C 0.9 0.5\n');time.sleep(2);os.write(controller,b'SET C 0.5 0.5\n');time.sleep(1);screenshot(f'building-interior-{view}.png')
            press('L');time.sleep(1)
            building_save();press('B')
            press('A');time.sleep(4);building_view('building-exited.png',False)
        elif '--stream-travel' in sys.argv:
            start_game('--new-town' in sys.argv,'stream-travel')
            time.sleep(12)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2)
            os.write(controller,b'PRESS Z\n');time.sleep(1)
            if '--freeze-textures' in sys.argv:press('D_DOWN')
            if '--recreate-terrain-meshes' in sys.argv:press('X')
            os.write(controller,b'RELEASE Z\nRELEASE L\nRELEASE R\n');time.sleep(1)
            time.sleep(profile_seconds);screenshot('travel-start.png')
            def marker(name):
                pixels=subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])
                for origin in range(20,36):
                    for width in range(570,601):
                        def word(index):
                            result=0
                            for bit in range(16):
                                n=index*16+bit;x=origin+int((n%64+.5)*width/64);y=450+n//64*4
                                if pixels[(y*800+x)*3]>128:result|=1<<bit
                            return result
                        if word(0)!=0x4154:continue
                        values=[word(n) for n in range(32)];checksum=0
                        for value in values[:31]:checksum^=value
                        if values[1] in (5,6) and checksum==values[31]:return values[2]
                raise AssertionError('No valid travel telemetry in '+name)
            previous=marker('travel-start.png')
            travel_report={'disc':str(disc),'freeze_textures':'--freeze-textures' in sys.argv,'recreate_terrain_meshes':'--recreate-terrain-meshes' in sys.argv,'seconds_per_direction':profile_seconds*2,'frame_counters':[previous]}
            # No inspector teleports: any landscape transition in this capture
            # interrupts ordinary travel and must fail --check-streaming.
            for n,(x,y,running) in enumerate(((1,.5,False),(0,.5,True),(.5,0,False),(.5,1,True),(1,0,True),(0,1,True))):
                os.write(controller,f'SET MAIN {x} {y}\n'.encode())
                if running:os.write(controller,b'PRESS B\n')
                time.sleep(profile_seconds*2)
                os.write(controller,b'SET MAIN .5 .5\nRELEASE B\n');time.sleep(2);screenshot(f'travel-{n}.png')
                current=marker(f'travel-{n}.png')
                if ((current-previous)&65535)<30:raise AssertionError('Game stopped presenting frames during travel route '+str(n))
                previous=current
                travel_report['frame_counters'].append(current)
                (root/'travel-scenario.json').write_text(json.dumps(travel_report,indent=2))
        elif '--movement-profile' in sys.argv:
            start_game('--new-town' in sys.argv,'movement-profile')
            if '--ground-ring' in sys.argv:
                os.write(controller,b'SET C 0.5 0.95\nPRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\nSET C 0.5 0.5\n');time.sleep(2)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\n')
            time.sleep(profile_seconds);screenshot('movement-stationary.png')
            routes=[(.9,.5,False),(.1,.5,False),(.5,.1,False),(.5,.9,False),(.9,.1,True),(.1,.9,True)]
            for n,(x,y,running) in enumerate(routes):
                os.write(controller,f'SET MAIN {x} {y}\n'.encode())
                if running:os.write(controller,b'PRESS B\n')
                time.sleep(profile_seconds)
                os.write(controller,b'SET MAIN 0.5 0.5\nRELEASE B\n');time.sleep(2);screenshot(f'movement-route-{n}.png')
            # Existing developer ramp focus verifies north/south reprojection
            # and elevation changes on the same saved town.
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n')
            for n,y in enumerate((.9,.1)):
                os.write(controller,f'SET MAIN 0.5 {y}\n'.encode());time.sleep(3)
                os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot(f'movement-ramp-{n}.png')
            save_menu();time.sleep(12);screenshot('movement-save.png')
        elif '--runtime-profile' in sys.argv:
            import json
            start_game('--new-town' in sys.argv,'runtime-profile')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\n')
            for mode in range(8):
                time.sleep(profile_seconds);screenshot(f'runtime-{mode}.png')
                if mode<7:
                    os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('B');os.write(controller,b'RELEASE L\nRELEASE R\n')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('B');os.write(controller,b'RELEASE L\nRELEASE R\n')
            press('X');screenshot('runtime-backpack.png');press('B');save_menu();time.sleep(12);screenshot('runtime-save.png')
        elif '--compact-town' in sys.argv:
            start_game(True,'compact-town')
            before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
            if not before or before['dimensions']!=[128,120]:raise AssertionError('New town did not save compact bounds')
            press('Y');time.sleep(2);screenshot('compact-town-map.png');press('B')
            # Inspect actual generated terrain, then walk up its natural ramp.
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(3)
            screenshot('compact-town-ramp-low.png')
            os.write(controller,b'SET MAIN 0.5 0.9\n');time.sleep(1.7);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2)
            screenshot('compact-town-ramp-high.png')
            save_menu();after=wait_saved_after(before);time.sleep(3);screenshot('compact-town-saved.png')
            if after['dimensions']!=[128,120] or after['terrain_sha256']!=before['terrain_sha256']:raise AssertionError('Traversal/save changed compact terrain')
            (root/'compact-town.json').write_text(json.dumps(after,indent=2)+'\n')
        elif '--compact-town-resume' in sys.argv:
            before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
            if not before or before['dimensions']!=[128,120]:raise AssertionError('Compact reload requires a saved compact town')
            start_game(False,'compact-resume');time.sleep(3);screenshot('compact-town-resumed.png')
            press('Y');time.sleep(2);screenshot('compact-town-resumed-map.png');press('B')
            after=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
            if after!=before:raise AssertionError('Loading compact town changed its saved generation')
            (root/'compact-town.json').write_text(json.dumps(after,indent=2)+'\n')
        elif '--cliff-kit' in sys.argv:
            start_game('--new-town' in sys.argv,'cliff-kit')
            time.sleep(3);screenshot('cliff-normal.png')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_UP');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(3);screenshot('cliff-kit-eight.png')
            os.write(controller,b'SET MAIN 0.9 0.5\n');time.sleep(1);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('cliff-kit-uv-shift.png')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_UP');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(2);screenshot('cliff-kit-off.png')
            if '--new-town' in sys.argv:
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(3);screenshot('cliff-raised-walls.png')
        elif '--uv-debug' in sys.argv:
            press('A');time.sleep(3);screenshot('uv-normal.png')
            def setting(up,name):
                set_debug_visibility(True)
                press('START');press('R')
                for _ in range(up):press('D_DOWN')
                screenshot(name+'-setting.png');press('A');time.sleep(2);screenshot(name+'-enabled.png');press('B');time.sleep(3);screenshot(name+'.png')
            setting(2,'uv-colors')
            setting(3,'uv-combined')
            setting(2,'uv-lines')
            setting(3,'uv-restored')
            save_menu();time.sleep(12);screenshot('uv-save.png')
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
            save_menu();time.sleep(12);screenshot('movement-save.png')
        elif '--cloud-shadows' in sys.argv:
            start_game(True)
            time.sleep(2);screenshot('cloud-partial-a.png');time.sleep(20);screenshot('cloud-partial-b.png')
            press('X');screenshot('cloud-menu.png');press('B')
            press('A');time.sleep(2);screenshot('cloud-indoor-a.png');time.sleep(8);screenshot('cloud-indoor-b.png')
            press('A');time.sleep(2);screenshot('cloud-exterior-return.png')
        elif '--actor-shadows' in sys.argv:
            start_game(True)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\n')
            time.sleep(profile_seconds);screenshot('actor-shadow-standing.png')
            for name,axes in [('right',(.72,.5)),('down',(.5,.28)),('left',(.28,.5)),('up',(.5,.72))]:
                os.write(controller,f'SET MAIN {axes[0]} {axes[1]}\n'.encode());time.sleep(1.4)
                screenshot('actor-shadow-walking-'+name+'.png');os.write(controller,b'SET MAIN .5 .5\n');time.sleep(1)
            # A fresh doorway position avoids depending on collisions during
            # the walking loop. NPCs keep their normal journeys throughout.
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(12)
            press('A');time.sleep(5);building_view('actor-shadow-interior.png',True)
            os.write(controller,b'SET C .9 .5\n');time.sleep(2);os.write(controller,b'SET C .5 .5\n');time.sleep(1);screenshot('actor-shadow-indoor-camera.png')
            press('L');time.sleep(1);press('A')
            for sample in range(12):
                time.sleep(3)
                try:building_view('actor-shadow-exited.png',False);break
                except AssertionError:
                    if p.poll() is not None or sample==11:raise
                    if sample in (3,7):press('B');press('B');press('A')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15)
            screenshot('actor-shadow-ramp-low.png')
            os.write(controller,b'SET MAIN .5 .8\n');time.sleep(1.5);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(2);screenshot('actor-shadow-ramp-high.png')
            os.write(controller,b'SET MAIN .28 .5\n');time.sleep(1);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(2);screenshot('actor-shadow-cliff-edge.png')
            press('X');screenshot('actor-shadow-menu.png');press('B')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_LEFT');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15);screenshot('actor-shadow-water.png')
        elif '--full-resolution-aa' in sys.argv:
            wait_title();default_on=active_edge_smoothing()
            card=root/'MemoryCardA.USA.raw'
            fresh=not committed_card_sequence(card,details=True) and not options_card_files()
            if fresh and not default_on:raise AssertionError('A fresh card must start with edge smoothing enabled')
            # Also captures the real compact starter-plot preview on fresh cards.
            start_game(False,'aa-initial');before=committed_card_sequence(card,details=True)
            def restart_title():
                global p
                if not stop_dolphin(p,30)['clean_exit']:raise AssertionError('Dolphin did not flush the disposable card cleanly')
                offset=len(runtime_log_text());p=subprocess.Popen(command,env=env,stdout=log,stderr=subprocess.STDOUT)
                wait_title();return offset
            def selector(name):
                press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True)
                press('D_DOWN');press('D_DOWN');screenshot(name+'.png')
            def apply_aa(enabled,name):
                if active_edge_smoothing()!=enabled:press('D_RIGHT')
                saved=activate_video_setting(None,None,int(enabled))
                if active_edge_smoothing()!=enabled:raise AssertionError('Saved smoothing preference differs from the live display')
                screenshot(name+'.png');return list(saved)
            restart_title();selector('aa-menu-default')
            off=apply_aa(False,'aa-menu-off');on=apply_aa(True,'aa-menu-on')
            # Rejected 576p must not move the underline or save the draft choice.
            press('D_UP');press('D_UP');press('D_RIGHT');original=options_card_files().get('options')
            offset=len(runtime_log_text());press('A');screenshot('aa-576p-rejected.png')
            if options_card_files().get('options')!=original or not active_edge_smoothing() or 'ANIMALTOWN OPTIONS SAVED' in runtime_log_text()[offset:]:
                raise AssertionError('576p with smoothing enabled changed saved preferences or the live mode')
            press('B');press('D_UP');press('D_UP');start_game(False,'aa-on');screenshot('aa-world-on.png')
            restart_offset=restart_title();selector('aa-menu-restored')
            if 'ANIMALTOWN AA 1 render 640 480 parts 2' not in runtime_log_text()[restart_offset:]:raise AssertionError('Saved smoothing was not applied at startup')
            wait_options_saved(None,None,1)
            restored_off=apply_aa(False,'aa-menu-restored-off')
            press('B');press('D_UP');press('D_UP');start_game(False,'aa-off');screenshot('aa-world-off.png')
            if committed_card_sequence(card,details=True)!=before:raise AssertionError('AA setting changed the town save')
            check_runtime_failure()
            (root/'aa-option.json').write_text(json.dumps({'passed':True,'defaultOn':default_on,'freshCard':fresh,'savedOff':off,'savedOn':on,
                'restoredAfterRestart':True,'restoredOff':restored_off,'rejected576p':True,'townUnchanged':True,'hostMsaa':1,'hardwareTiming':False},indent=2)+'\n')
        elif '--lighting' in sys.argv:
            start_game(True)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15)
            # Cast shadows follow daylight; capture their motion and receivers.
            time.sleep(profile_seconds);building_view('lighting-standing.png',False)
            time.sleep(20);screenshot('lighting-cloud-drift.png')
            press('A');time.sleep(5);building_view('lighting-interior.png',True)
            os.write(controller,b'SET C 0.9 0.5\n');time.sleep(2);os.write(controller,b'SET C 0.5 0.5\n');time.sleep(1);screenshot('lighting-indoor-camera.png')
            press('L');time.sleep(1);press('A');time.sleep(7);building_view('lighting-exited.png',False)
            press('X');screenshot('lighting-menu.png');press('B')
            # A normal water/material pass immediately follows the model rim.
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_LEFT');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15);screenshot('lighting-water.png')
        elif '--terrain-scroll' in sys.argv:
            start_game(True)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(12)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('Z');os.write(controller,b'RELEASE L\nRELEASE R\n')
            time.sleep(profile_seconds);screenshot('scroll-ramp-start.png')
            # Slow axial motion includes fractional steps, cache boundaries and
            # the complete ramp. Then strafe on the terrace and return downhill.
            for name,axes in [('up',(0.5,0.72)),('right',(0.72,0.5)),('left',(0.28,0.5)),('down',(0.5,0.28))]:
                os.write(controller,f'SET MAIN {axes[0]} {axes[1]}\n'.encode());time.sleep(profile_seconds)
                os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(1);screenshot('scroll-'+name+'.png')
            time.sleep(profile_seconds);screenshot('scroll-rest.png')
            if '--stream-stress' in sys.argv:
                for name in ['river','corner','bridge','coast']:
                    os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_LEFT');os.write(controller,b'RELEASE L\nRELEASE R\n')
                    time.sleep(12+profile_seconds);screenshot('stream-'+name+'.png')
        elif '--river-coast' in sys.argv:
            start_game(True)
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
            start_game(True,'water')
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
            save_menu();time.sleep(12);screenshot('terrain-edge-save.png')
        elif '--interaction-profile' in sys.argv:
            press('A');time.sleep(2)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(1);press('Z')
            os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(profile_seconds);screenshot('interaction-world.png')
            press('A');screenshot('interaction-typing.png');time.sleep(profile_seconds);screenshot('interaction-dialogue.png')
            press('B');press('X');time.sleep(profile_seconds);screenshot('interaction-backpack.png')
            press('A');time.sleep(profile_seconds);screenshot('interaction-actions.png')
            press('B');press('START');press('D_DOWN');press('A');screenshot('interaction-long-typing.png')
            time.sleep(profile_seconds);screenshot('interaction-long-dialogue.png')
        elif '--terrain-life' in sys.argv:
            start_game(True)
            screenshot('life-world-before.png');time.sleep(20);screenshot('life-world-after.png')
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('START');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(2)
            screenshot('terrain-ramp-low.png')
            os.write(controller,b'SET MAIN 0.5 0.9\n');time.sleep(1.7);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('terrain-ramp-high.png')
            save_menu();time.sleep(12);screenshot('terrain-high-save.png');press('B')
            os.write(controller,b'SET MAIN 0.5 0.1\n');time.sleep(1.7);os.write(controller,b'SET MAIN 0.5 0.5\n');time.sleep(2);screenshot('terrain-ramp-return.png')
            press('Y');time.sleep(2);screenshot('life-map.png');press('B');save_menu();time.sleep(12);screenshot('terrain-life-save.png')
        elif '--play' in sys.argv:
            start_game(True);screenshot('bubble.png')
            time.sleep(2);screenshot('world.png')
            press('Y');time.sleep(1);screenshot('map.png');press('Y')
            press('X');time.sleep(1);screenshot('pockets.png');press('B')
            save_menu();time.sleep(12);screenshot('save.png')
        elif '--resume' in sys.argv:
            start_game(False,'resume');screenshot('resume.png')
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
                press('D_UP') # Quit is last for every filtered item menu.
                press('A');screenshot('backpack-quit.png')
            if '--item-navigation' in sys.argv:item_navigation()
            if '--money-navigation' in sys.argv:
                item_navigation(4)
                # Money fixture starts at 98,999: the 1,000 bag exactly fills
                # the wallet, then the 10,000 bag must remain after refusal.
                press('A');press('A');time.sleep(4);screenshot('money-wallet-filled.png');press('B');press('B');press('X')
                press('D_RIGHT');press('A');press('A');time.sleep(4);screenshot('money-wallet-overflow.png');press('B');press('B');press('X');press('D_RIGHT')
                screenshot('money-overflow-bag-retained.png')
            save_menu();time.sleep(12);screenshot('resume-save.png')
            if '--split-save' in sys.argv:
                press('B');time.sleep(5);save_menu();time.sleep(12);screenshot('split-save-second.png')
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
        stop_dolphin(p)
close_display();atexit.unregister(close_display)
os.close(controller)
print(root/'screen.png')
