"""Direct ISO boot: no separately selected DOL. Run inside animaltown-dolphin."""
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
    subprocess.run(['ffmpeg','-v','error','-f','x11grab','-draw_mouse','0','-video_size','800x600','-i',':93','-frames:v','1','-y',str(root/name)],env=env,check=True)


def rgb(name):
    return subprocess.check_output(['ffmpeg','-v','error','-i',str(root/name),'-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'])

def color_count(pixels,color,tolerance=5):
    return sum(all(abs(pixels[i+c]-value)<=tolerance for c,value in enumerate(color))
               for i in range(0,len(pixels),3))

def region_color_count(pixels,color,left,top,right,bottom,tolerance=5):
    return sum(all(abs(pixels[(y*800+x)*3+c]-value)<=tolerance for c,value in enumerate(color))
               for y in range(top,bottom) for x in range(left,right))

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
        if stage=='home':return rendered>60000 and header>5000
        # Gameplay's translucent green control strip and cream date/time
        # panel are absent from the placement/review/loading screens.
        controls=sum(35<=pixels[(y*800+x)*3]<=105
                     and 60<=pixels[(y*800+x)*3+1]<=130
                     and pixels[(y*800+x)*3+1]>pixels[(y*800+x)*3]+5
                     and pixels[(y*800+x)*3+1]>pixels[(y*800+x)*3+2]+5
                     for y in range(442,467) for x in range(65,415))
        date=region_color_count(pixels,(255,245,221),60,378,180,427)
        return rendered>60000 and header<5000 and controls>3000 and date>2500
    raise ValueError('Unknown onboarding stage: '+stage)

def wait_onboarding(stage,name=None):
    name=name or 'onboarding-'+stage+'.png'
    deadline=time.monotonic()+(360 if stage in ('home','world') else 180)
    loading=False
    while time.monotonic()<deadline:
        if p.poll() is not None:raise AssertionError('Emulator stopped before onboarding '+stage)
        screenshot(name);pixels=rgb(name)
        if color_count(pixels,(149,198,186))>150000 and color_count(pixels,(255,245,221))<100:loading=True
        if onboarding_signature(pixels,stage):return loading
        time.sleep(1)
    raise AssertionError('Onboarding did not reach '+stage+'; inspect '+name)

def wait_saved_after(before):
    deadline=time.monotonic()+240
    sequence=before['sequence'] if before else 0
    while time.monotonic()<deadline:
        after=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
        if after and after['sequence']>sequence:return after
        if p.poll() is not None:raise AssertionError('Emulator stopped during explicit onboarding save')
        time.sleep(1)
    screenshot('onboarding-save-failure.png')
    raise AssertionError('Save & begin did not publish a complete checksummed generation')

def finish_onboarding(prefix='new-villager',before=None):
    """Complete an already selected empty slot via the actual four stages."""
    before=before if before is not None else committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    wait_onboarding('name',prefix+'-name.png');press('A');press('START')
    wait_onboarding('look',prefix+'-look.png');press('A')
    loading=wait_onboarding('home',prefix+'-home.png');press('A')
    wait_onboarding('review',prefix+'-review.png');press('A')
    after=wait_saved_after(before);wait_onboarding('world',prefix+'-game.png')
    return after,loading

def start_game(new_town=False,prefix='start'):
    """Navigate Title and the resident picker before sending gameplay input."""
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    wait_title();press('A');wait_profiles(prefix+'-picker.png');press('A')
    active=before['active'] if before else 0
    if new_town or not before or not before['profiles'][active]['created']:
        return finish_onboarding(prefix,before)
    return before,wait_onboarding('world',prefix+'-game.png')

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
        if p.poll() is not None:raise AssertionError('Emulator stopped before title')
        name='title-modal-wait.png' if modal else 'title-wait.png'
        screenshot(name);pixels=rgb(name)
        cream=color_count(pixels,(255,245,221));leaf=color_count(pixels,(53,105,84))
        if cream>70000 and (leaf<1000 if modal else leaf>6000 and color_count(pixels,(149,198,186))>15000):return
        time.sleep(2)
    raise AssertionError('Debug modal did not appear' if modal else 'Original cream title/menu did not appear')

def title_menu():
    before=committed_card_sequence(root/'MemoryCardA.USA.raw',details=True)
    active=before['active'] if before else 0
    creating=not before or not before['profiles'][active]['created']
    screenshot('title-dots-a.png');time.sleep(3);screenshot('title-dots-b.png')
    a,b=rgb('title-dots-a.png'),rgb('title-dots-b.png')
    # Background-only pixels must change while the game remains deferred.
    mint=lambda pixels,i: all(abs(pixels[i+c]-value)<=5 for c,value in enumerate((149,198,186)))
    changed=sum(a[i:i+3]!=b[i:i+3] and (mint(a,i) or mint(b,i)) for i in range(0,len(a),3))
    if changed<100:raise AssertionError('Title background is not scrolling')
    press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True);screenshot('title-debug-modal.png')
    press('D_RIGHT');screenshot('title-debug-edited.png')
    # Exercise paging, wraparound and slider rows, then return to row zero.
    press('D_UP');screenshot('title-debug-last-page.png');press('D_DOWN')
    press('B');wait_title();screenshot('title-dev-config-saved.png')
    press('A');wait_title(modal=True);screenshot('title-debug-reopened.png');press('B');wait_title()
    press('D_UP');press('D_UP')
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

def wait_profiles(name='profile-picker.png'):
    deadline=time.monotonic()+180
    while time.monotonic()<deadline:
        screenshot(name);pixels=rgb(name)
        count=sum(all(abs(pixels[(y*800+x)*3+c]-value)<=5 for c,value in enumerate((53,105,84))) for y in range(160,365) for x in range(120,165))
        cards=color_count(pixels,(107,151,90))>3000 and color_count(pixels,(255,183,64))>150
        if (count>150 or cards) and color_count(pixels,(255,245,221))>70000:return
        if p.poll() is not None:raise AssertionError('Emulator stopped before villager picker')
        time.sleep(1)
    raise AssertionError('Villager picker did not appear')

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
                    if words[0]==0x4154 and words[1]==5 and words[31]==checksum and words[30]==0:valid=True;break
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
    # Session-only weather toggle through the visible debug menu (row 16).
    press('START');press('R')
    for _ in range(16):press('D_DOWN')
    press('A');press('B');segment('rain',profile_seconds)
    press('START');press('R')
    for _ in range(16):press('D_DOWN')
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

def committed_card_sequence(path, expected_day=None, details=False):
    """Read a complete generation from either redundant directory/BAT pair."""
    import struct
    import zlib
    if not path.exists():return 0
    card=path.read_bytes();sector=8192
    if len(card)%sector:raise AssertionError('Truncated emulator memory card')
    def be16(at):return struct.unpack_from('>H',card,at)[0]
    def record(payload,kind):
        if len(payload)<32:raise ValueError('Short save record')
        magic,version,catalog,actual,sequence,seed,length=struct.unpack_from('<7I',payload)
        crc=struct.unpack_from('<I',payload,len(payload)-4)[0]
        if magic!=0x50535441 or version not in (1,2,3,4,5) or actual!=kind or length!=len(payload) or zlib.crc32(payload[:-4])!=crc:
            raise ValueError('Invalid save record')
        return version,catalog,sequence,seed,crc
    best=0;best_day=None;best_details=None
    for directory in (1,2):
        for bat in (3,4):
            files={}
            try:
                for i in range(127):
                    at=directory*sector+i*64
                    if card[at:at+6]!=b'AMTE01':continue
                    name=card[at+8:at+40].split(b'\0',1)[0].decode('ascii')
                    if not name.startswith('AT-'):continue
                    block,count=be16(at+54),be16(at+56);seen=set();data=bytearray()
                    for _ in range(count):
                        if block<5 or block>=len(card)//sector or block in seen:raise ValueError('Invalid card chain')
                        seen.add(block);data.extend(card[block*sector:(block+1)*sector])
                        block=be16(bat*sector+10+(block-5)*2)
                    if block!=65535 or data[:8]!=b'DNDSAVE1':raise ValueError('Invalid card container')
                    length,offset=struct.unpack_from('>II',data,8)
                    if offset<32 or offset+length>len(data):raise ValueError('Truncated card container')
                    files[name]=bytes(data[offset:offset+length])
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
                                        if pi[:4]!=(4,identity[1],sequence,identity[3]) or len(part)!=length or pi[4]!=crc:continue
                                        selected=name;break
                                    except (KeyError,ValueError,struct.error):continue
                                if selected is None:raise ValueError('Missing exact referenced revision')
                                if i==7:runtime=files[selected]
                                else:names.append(selected)
                            if len(runtime)!=300:raise ValueError('Invalid resident runtime size')
                        else:
                            if len(commit)!=32+8*len(names) or not all(name in files for name in names):continue
                            for kind,name in enumerate(names):
                                part=files[name];part_identity=record(part,kind if kind<4 else kind+1)
                                expected_length,expected_crc=struct.unpack_from('<II',commit,28+kind*8)
                                if part_identity[:4]!=identity[:4] or len(part)!=expected_length or part_identity[4]!=expected_crc:
                                    raise ValueError('Mixed save generation')
                        if identity[2]>best:
                            best=identity[2];town=files[names[0]];best_day=struct.unpack_from('<i',town,40)[0]
                            if identity[0]>=4:
                                at=48
                                for _ in range(2):at+=4+struct.unpack_from('<I',town,at)[0]*4
                                terrain_hash=__import__('hashlib').sha256(town[48:at]).hexdigest()
                                for _ in range(6):at+=8;at+=4+struct.unpack_from('<I',town,at)[0]*4;at+=8
                                for _ in range(2):at+=4+struct.unpack_from('<I',town,at)[0]*24
                                active=struct.unpack_from('<I',runtime,124)[0] if runtime is not None else struct.unpack_from('<I',town,at)[0];profiles=[]
                                for name in names[3:]:
                                    part=files[name];length=struct.unpack_from('<I',part,160)[0]
                                    label=''.join(chr(struct.unpack_from('<I',part,164+i*4)[0]) for i in range(length))
                                    profiles.append({'name':label,'created':struct.unpack_from('<I',part,28)[0],'wallet':struct.unpack_from('<I',part,40)[0],'species':struct.unpack_from('<I',part,44)[0]})
                                plots=[dict(zip(('placed','x','z'),struct.unpack_from('<3i',files[names[1]],28+i*12))) for i in range(4)]
                                profile_hashes=[__import__('hashlib').sha256(files[name][28:-4]).hexdigest() for name in names[3:]]
                                best_details={'sequence':best,'active':active,'profiles':profiles,'profile_payload_sha256':profile_hashes,'plots':plots,'terrain_sha256':terrain_hash,'houses_sha256':__import__('hashlib').sha256(files[names[1]][28:-4]).hexdigest()}
                    except (ValueError,KeyError,UnicodeError,struct.error):continue
            except (ValueError,UnicodeError,struct.error):continue
    if expected_day is not None and best_day!=expected_day:
        raise AssertionError(f'Explicit save did not retain the new rollover day: expected {expected_day}, got {best_day}')
    return best_details if details else best

def testing_menu():
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
    for _ in range(5):press('D_DOWN')
    label('testing-settings-debug-entry',5);press('A')
    manual_only='--manual-save-only' in sys.argv
    for row in range(1 if manual_only else 14):
        original=label(f'testing-row-{row}-before',row)
        press('A');changed=label(f'testing-row-{row}-after',row)
        if original==changed:raise AssertionError(f'Testing row {row} did not change its displayed state')
        # Restore the initial setting, including the three/six-state previews.
        for step in range(2 if row==8 else 5 if row==10 else 1):
            press('A');label(f'testing-row-{row}-restore-{step}',row)
        if row==10:
            # A water preview must allow Start to reopen the menu while active.
            press('A');label('testing-water-active',row);press('B');time.sleep(15);press('START');press('R')
            for _ in range(10):press('D_DOWN')
            label('testing-water-menu-return',row)
            for step in range(5):
                press('A');label(f'testing-water-off-{step}',row)
        press('D_DOWN')
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
    (root/'testing-menu.txt').write_text('Completed onboarding with explicit Save & begin; Settings never saved automatically.\nVisible Settings entry opened Debug / testing.\n'+('Performance display toggled/restored.\n' if manual_only else 'All 14 debug toggles changed state and were restored; water preview allowed menu re-entry.\n')+f'{wait_seconds}s idle play preserved the card bytes.\n'+'Only Save now committed the next generation of records and their matching checksummed commit.\nReopening Settings left the saved generation unchanged.\n')

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

def card_dev_config(path):
    """Decode the independent, checksummed dev_config item in a raw GC card."""
    import struct
    if not path.exists():return None
    card=path.read_bytes();sector=8192;candidates=[]
    if len(card)%sector:raise AssertionError('Truncated emulator memory card')
    def be16(at):return struct.unpack_from('>H',card,at)[0]
    for directory in (1,2):
        for bat in (3,4):
            try:
                for entry in range(127):
                    at=directory*sector+entry*64
                    if card[at:at+6]!=b'AMTE01' or card[at+8:at+40].split(b'\0',1)[0]!=b'dev_config':continue
                    block,count=be16(at+54),be16(at+56);seen=set();data=bytearray()
                    for _ in range(count):
                        if block<5 or block>=len(card)//sector or block in seen:raise ValueError('Invalid card chain')
                        seen.add(block);data.extend(card[block*sector:(block+1)*sector]);block=be16(bat*sector+10+(block-5)*2)
                    if block!=65535 or data[:8]!=b'DNDSAVE1':continue
                    length,offset=struct.unpack_from('>II',data,8)
                    if length not in (36,40) or offset<32 or offset+length>len(data):continue
                    payload=bytes(data[offset:offset+length])
                    if payload[:3]!=b'ATD' or payload[3] not in (1,2,3):continue
                    checksum_at=32 if payload[3]==1 else 36
                    count=27 if payload[3]==1 else 29 if payload[3]==2 else 30
                    if length!=checksum_at+4 or any(payload[4+count:checksum_at]):continue
                    checksum=2166136261
                    for value in payload[:checksum_at]:checksum=((checksum^value)*16777619)&0xffffffff
                    if checksum!=struct.unpack_from('<I',payload,checksum_at)[0]:continue
                    candidates.append((be16(directory*sector+0x1ffa),payload))
            except (ValueError,struct.error):continue
    if not candidates:return None
    # Compare the redundant directory update counters modulo 16 bits.
    newest=candidates[0]
    for candidate in candidates[1:]:
        if 0<((candidate[0]-newest[0])&65535)<32768:newest=candidate
    payload=newest[1]
    count=27 if payload[3]==1 else 29 if payload[3]==2 else 30
    return {'version':payload[3],'values':list(payload[4:4+count]),'discard_terrain_cache_when_indoors':payload[3]==3 and payload[33]==1}

def aram_residency_scenario():
    """Real door/cache/model transitions, plus separate-card config roundtrips."""
    import re
    global p
    sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(user/'Dump/Frames',track_presentations=True)
    card=root/'MemoryCardA.USA.raw';segments=[];report={'segments':segments,'clock':'NTSC VI chunks; software Dolphin, not hardware'}
    def logs():
        # OSReport normally goes to dolphin.log; stdout is a fallback for builds
        # that route it to the launcher. Avoid counting the same events twice.
        files=list((user/'Logs').glob('*.log'))
        return '\n'.join(path.read_text(errors='replace') for path in files) if files else (root/'emulator.log').read_text(errors='replace')
    def alive():
        if p.poll() is not None:raise AssertionError('Emulator stopped during ARAM residency scenario')
        if 'ANIMALTOWN ASSET LOAD FAILED' in logs():raise AssertionError('AnimalTown assets failed to load')
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
    def config(expected):
        deadline=time.monotonic()+60
        while time.monotonic()<deadline:
            alive();value=card_dev_config(card)
            if value and value['discard_terrain_cache_when_indoors']==expected:return value
            time.sleep(.2)
        raise AssertionError('Separate dev config item did not persist terrain option '+str(expected))
    def title_terrain_row(name):
        press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True)
        # The appended session-only UI grain control follows persisted options.
        press('D_UP');press('D_UP');screenshot(name+'.png') # config index 29
    def inspect():
        os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n')
    def enter(name):
        inspect();view(name+'-outside',False);press('A');view(name+'-inside',True)
        # The crossing lands 0.7 m from the exit. Walk into the room so the
        # early-exit prefetch guard does not deliberately keep outdoor models hot.
        os.write(controller,b'SET MAIN .5 .8\n')
        try:guest(name+'-walk-away-from-door',1)
        finally:os.write(controller,b'SET MAIN .5 .5\n')
        view(name+'-away-from-door',True)
    def leave(name,reversal=False):
        inspect() # the existing indoor inspector focuses the real exit socket
        started=time.monotonic();start_vi=clock.frames()
        os.write(controller,b'PRESS A\n');time.sleep(1);os.write(controller,b'RELEASE A\n')
        if reversal:
            # Try to reverse movement/action while the existing portal owns the
            # actor; this must neither publish a partial restore nor strand it.
            os.write(controller,b'SET MAIN .5 .8\n');guest(name+'-reversal-input',.3);os.write(controller,b'SET MAIN .5 .5\n');press('A')
        view(name,False);report[name+'_wall_seconds']=time.monotonic()-started
        report[name+'_guest_seconds']=(clock.frames()-start_vi)*1001/60000
        guest(name+'-outdoor-settle',2)

    wait_title();initial=card_dev_config(card);report['initial_dev_config']=initial
    title_terrain_row('aram-title-row29-baseline')
    if initial and initial['discard_terrain_cache_when_indoors']:press('A')
    press('B');wait_title()
    if initial:config(False)
    press('D_UP');press('D_UP');start_game(False,'aram')
    wait_log(r'ANIMALTOWN READY ',seconds=300)
    wait_log(r'Terrain archive: [0-9]+ ARAM bytes',seconds=300)
    wait_log(r'ANIMALTOWN ONBOARDING RELEASED',seconds=120)
    view('aram-game-started',False)
    baseline=len(logs());enter('aram-flag-off');guest('flag-off-indoor-steady',8)
    if 'ANIMALTOWN TERRAIN CACHE SUSPENDED' in logs()[baseline:]:raise AssertionError('Default-off terrain option suspended the cache')
    screenshot('aram-flag-off-indoor.png');leave('aram-flag-off-exited')
    # Testing has 31 rows; Up wraps to new row 30 after tree and UI grain rows.
    press('START');press('R');press('D_UP');screenshot('aram-game-row30-off.png');press('A')
    report['ingame_saved_config']=config(True);screenshot('aram-game-row30-on.png');press('B')
    cold=len(logs());enter('aram-flag-on');guest('flag-on-indoor-steady',6)
    suspended=wait_log(r'ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes (\d+) reclaimed bytes (\d+) archive bytes (\d+)',cold)
    if tuple(map(int,suspended.groups()[:2]))!=(0,1048576):raise AssertionError('Terrain cache did not physically release exactly 1 MiB')
    # This assertion is independent of the terrain flag: automatic model paging
    # must actually retire at least one cold shared species, not only plan it.
    model=wait_log(r'ANIMALTOWN MODEL COLD (\d+) aram bytes (\d+) native budget bytes (\d+) max advance us (\d+)',baseline)
    if int(model[2])<=0:raise AssertionError('Cold model has no ARAM package backing')
    report['suspended']={'cache_bytes':int(suspended[1]),'reclaimed_bytes':int(suspended[2]),'archive_bytes':int(suspended[3])}
    report['model_cold']={'species':int(model[1]),'aram_bytes':int(model[2]),'native_bytes':int(model[3]),'max_advance_us':int(model[4])}
    screenshot('aram-flag-on-cold-indoor.png');warm=len(logs());leave('aram-restored-exterior',reversal=True)
    restored=wait_log(r'ANIMALTOWN TERRAIN CACHE READY cache bytes (\d+) archive bytes (\d+)',warm)
    if int(restored[1])!=1048576 or int(restored[2])!=int(suspended[3]):raise AssertionError('Terrain cache restore changed the retained archive')
    report['restored']={'cache_bytes':int(restored[1]),'archive_bytes':int(restored[2])}
    # Back-to-back crossings exercise drain/cancel and reuse of restored backing.
    enter('aram-rapid-reentry');guest('rapid-reentry-indoor',1);leave('aram-rapid-return',reversal=True)
    cold_species=set();consumed=[]
    for event in re.finditer(r'ANIMALTOWN MODEL (COLD|WARM) (\d+)',logs()[baseline:]):
        species=int(event[2])
        if event[1]=='COLD':cold_species.add(species)
        elif species in cold_species:consumed.append(species)
    if not consumed:raise AssertionError('No actually cold model was restored before the drawable exterior returned')
    report['cold_species_restored_before_exterior_return']=consumed
    report['runtime_markers']=re.findall(r'ANIMALTOWN (?:MODEL [^\r\n]+|TERRAIN CACHE [^\r\n]+)',logs())
    (root/'aram-runtime.log').write_text(logs())
    # Restart the same ISO/card. Toggling the title's last row once must produce
    # Off in the binary record; that proves startup loaded the prior On setting.
    p.terminate();p.wait(timeout=10)
    frames=user/'Dump/Frames'
    if frames.exists():frames.rename(user/'Dump/Frames-residency')
    p=subprocess.Popen(['/usr/games/dolphin-emu-nogui','-u',str(user),'-e',str(disc),'-v','OGL'],env=env,stdout=log,stderr=subprocess.STDOUT)
    wait_title();wait_log(r'ANIMALTOWN DEV CONFIG LOADED')
    title_terrain_row('aram-title-row29-loaded-on');press('A');press('B');wait_title();config(False)
    # Leave the option enabled and prove the corresponding title row roundtrip.
    press('A');wait_title(modal=True);screenshot('aram-title-row29-off.png');press('A');press('B');wait_title()
    report['restart_saved_config']=config(True);report['startup_load_verified']=True
    screenshot('aram-title-restart-complete.png')
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
    press('D_UP');press('D_UP');focus('settings-wrap',5)
    press('R');focus('testing-entry',0)
    press('A');focus('testing-performance-on',0)
    press('A');focus('testing-performance-off',0)
    press('D_DOWN');focus('testing-model-row',1)
    press('A');focus('testing-model-direct',1)
    press('A');focus('testing-model-indexed',1)
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
    rtc=int(datetime(2027,month,15,5,59,30,tzinfo=timezone.utc).timestamp()) if '--rtc-rollover' in sys.argv else int(datetime(2027,month,15,12,tzinfo=timezone.utc).timestamp())
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
if '--profile' in sys.argv or '--interaction-profile' in sys.argv or '--runtime-profile' in sys.argv or '--terrain-scroll' in sys.argv or '--lighting' in sys.argv or '--actor-shadows' in sys.argv or '--movement-profile' in sys.argv or '--stream-travel' in sys.argv or '--submissions' in sys.argv or '--aram-residency' in sys.argv:
    config=user/'Config'/'Dolphin.ini'
    config.write_text(config.read_text()+'\n[Movie]\nDumpFrames = True\nDumpFramesSilent = True\n')
    config=user/'Config'/'GFX.ini'
    config.write_text(config.read_text().replace('DumpCodec = ffv1','DumpCodec = ffv1\nDumpEncoder = ffv1\nDumpPixelFormat = bgr0'))
(user/'Config'/'Logger.ini').write_text('[Options]\nWriteToFile = True\nVerbosity = 4\n[Logs]\nBOOT = True\nOSREPORT = True\nDVDINTERFACE = True\n')
env=dict(os.environ,DISPLAY=':93',QT_QPA_PLATFORM='xcb')
xvfb=subprocess.Popen(['Xvfb',':93','-nocursor','-screen','0','800x600x24'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
def close_display():
    xvfb.terminate()
    try:xvfb.wait(timeout=5)
    except subprocess.TimeoutExpired:xvfb.kill();xvfb.wait()
atexit.register(close_display)
time.sleep(1)
with (root/'emulator.log').open('w') as log:
    p=subprocess.Popen(['/usr/games/dolphin-emu-nogui','-u',str(user),'-e',str(disc),'-v','OGL'],env=env,stdout=log,stderr=subprocess.STDOUT)
    try:
        if '--loading' in sys.argv:
            for n,seconds in enumerate((1,1,2,4,8,12,17)):
                time.sleep(seconds);screenshot(f'loading-{n}.png')
        else:time.sleep(45)
        if p.poll() is not None:
            raise RuntimeError(f'Dolphin exited during boot ({p.returncode}); see {root / "emulator.log"}')
        screenshot('screen.png')
        if any(flag in sys.argv for flag in ('--title-menu','--title-load','--title-config','--title-update')):wait_title()
        if '--onboarding' in sys.argv:
            onboarding()
        elif '--aram-residency' in sys.argv:
            aram_residency_scenario()
        elif '--profile-delete' in sys.argv:
            delete_profile()
        elif '--profile-cards' in sys.argv:
            profile_cards()
        elif '--shared-profiles' in sys.argv:
            shared_profiles()
        elif '--title-menu' in sys.argv:
            title_menu()
        elif '--title-config' in sys.argv:
            press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True);screenshot('title-dev-config-loaded.png');press('B');wait_title();screenshot('title-modal-closed.png')
        elif '--title-load' in sys.argv:
            press('D_DOWN');press('D_DOWN');press('A');wait_title(modal=True);screenshot('title-dev-config-loaded.png');press('B');wait_title();press('D_UP');press('D_UP');press('A');wait_profiles('title-loaded-villager-picker.png');press('A');time.sleep(180);screenshot('title-continued-game.png')
        elif '--title-update' in sys.argv:
            press('D_DOWN');press('A');time.sleep(20);screenshot('title-updater.png')
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
                        if values[1]==5 and checksum==values[31]:return values[2]
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
            def cloud_cover():
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('D_RIGHT');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(2)
            cloud_cover();screenshot('cloud-clear.png')
            cloud_cover();screenshot('cloud-overcast.png')
            cloud_cover();screenshot('cloud-restored.png')
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
        elif '--lighting' in sys.argv:
            start_game(True)
            os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2);press('A');os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(15)
            # Z enables telemetry; while held, Y cycles both/off/rim/shadows.
            os.write(controller,b'PRESS L\nPRESS R\nPRESS Z\n');time.sleep(1)
            time.sleep(profile_seconds);building_view('lighting-both.png',False)
            for name in ['off','rim','shadows','restored']:
                press('Y');time.sleep(profile_seconds);screenshot('lighting-'+name+'.png')
            os.write(controller,b'RELEASE Z\nRELEASE L\nRELEASE R\n')
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
        elif '--water' in sys.argv or '--water-ab' in sys.argv:
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
            if '--water-ab' in sys.argv:
                # Same camera and save, no scene/terrain regeneration. From the
                # first Testing row, D_UP wraps straight to the final selector.
                press('START');press('R');press('D_UP')
                screenshot('water-ab-selector-windwaker.png')
                press('A');screenshot('water-ab-selector-sunshine.png')
                press('B');time.sleep(3)
                sun0=water_frame('water-sunshine-a')
                time.sleep(7);sun1=water_frame('water-sunshine-b')
                press('START');press('R');press('D_UP')
                press('A');screenshot('water-ab-selector-restored.png')
                press('B');time.sleep(3)
                restored=water_frame('water-windwaker-restored')
                # These measurements are diagnostics, not an assertion that
                # animated frames are pixel identical before/after switching.
                changed_mode=changed_animation=0
                for y in range(120,430):
                    for x in range(20,300):
                        i=(y*800+x)*3
                        if first[i:i+3]!=sun0[i:i+3]:changed_mode+=1
                        if sun0[i:i+3]!=sun1[i:i+3]:changed_animation+=1
                (root/'water-ab-comparison.json').write_text(json.dumps({
                    'comparison':'Wind Waker vs Sunshine, same camera and world',
                    'water_style_rows':25,
                    'changed_pixels_between_styles':changed_mode,
                    'sunshine_animated_pixels':changed_animation,
                    'restored_windwaker_frame':len(restored),
                    'screenshots':['water-a.png','water-sunshine-a.png',
                        'water-sunshine-b.png','water-windwaker-restored.png',
                        'water-ab-selector-windwaker.png','water-ab-selector-sunshine.png',
                        'water-ab-selector-restored.png']
                },indent=2))
                if changed_mode<500 or changed_animation<50:
                    raise AssertionError('Sunshine renderer did not visibly differ/animate; inspect water-ab-comparison.json')
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
        p.terminate()
        try:p.wait(timeout=5)
        except subprocess.TimeoutExpired:p.kill();p.wait()
close_display();atexit.unregister(close_display)
os.close(controller)
print(root/'screen.png')
