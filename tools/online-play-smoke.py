"""Real GameCube SDK HTTPS/WSS on Dolphin's broadband adapter, through CI's tunnel."""
import json
import os
import re
from pathlib import Path
import sys
import time
import struct
import zlib


def run(context):
    c=context;root=c['root'];fixture=Path(sys.argv[sys.argv.index('--online-play-fixture')+1]).resolve()
    sys.path.insert(0,str(Path(c['__file__']).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(c['user']/'Dump/Frames',track_presentations=True)
    report={'scenario':'online-play','passed':False,'target':'Dolphin 2503 / broadband adapter HLE',
            'physical_hardware':False,'screenshots':[]}
    def alive():
        c['check_runtime_failure']()
        if c['p'].poll() is not None:raise AssertionError('Dolphin stopped during online qualification')
        log=c['runtime_log_text']()
        for phase in ('LINK','FLOW','RETURN'):
            if 'ANIMALTOWN ONLINE '+phase+' FAILED' in log:
                shot(phase.lower()+'-failure');setup_context()
                raise AssertionError('Native online operation failed; see the redacted setup checkpoints')
    def status():
        return json.loads((fixture/'status.json').read_text())
    def shot(name):
        name='online-play-'+name+'.png';c['screenshot'](name);report['screenshots'].append(name)
    def setup_context():
        lines=c['runtime_log_text']().splitlines()
        checkpoints=[line for line in lines if 'ANIMALTOWN ONLINE ' in line or 'ANIMALTOWN SAVE TOTAL:' in line or 'SAVE ERROR:' in line]
        print('Online setup checkpoints:\n'+'\n'.join(checkpoints[-80:]),flush=True)
    def press(button):c['press_presented'](button,clock)
    def wait(predicate,label,seconds=240,activate=False):
        deadline=time.monotonic()+seconds
        while time.monotonic()<deadline:
            alive()
            if predicate():
                print('Online target checkpoint: '+label,flush=True)
                return
            if activate:press('A')
            else:time.sleep(.25)
        shot('failure-'+label)
        setup_context()
        print('Online runtime context:\n'+'\n'.join(line for line in c['runtime_log_text']().splitlines() if 'ANIMALTOWN' in line)[-6000:],flush=True)
        raise AssertionError('Online target did not reach '+label)
    def world(count,label):
        wait(lambda:c['runtime_log_text']().count('ANIMALTOWN READY ')>=count,label)
        # Required world assets and terrain can complete on separate frames.
        deadline=time.monotonic()+180
        while time.monotonic()<deadline:
            alive();c['screenshot']('online-play-loading.png');pixels=c['rgb']('online-play-loading.png')
            if c['onboarding_signature'](pixels,'world'):
                shot(label);return
            time.sleep(1)
        raise AssertionError('Online world remained behind a loading panel')
    try:
        report['card_sequence_before']=c['committed_card_sequence'](root/'MemoryCardA.USA.raw')
        press('A');c['wait_profiles']();press('A');world(1,'home')
        # The disposable home is at the real helipad. Z keeps the existing
        # card-travel controls and enters the approved online flow.
        press('A');press('Z')
        wait(lambda:status().get('renewed',False),'sdk-https-renewal',activate=True)
        # Initial card sync uploads and downloads bounded pages through real
        # console TLS handshakes. Keep each request's 20-second deadline, but
        # allow the full setup ten minutes; fail immediately on native errors.
        wait(lambda:status().get('gate',False),'gate-open',seconds=600,activate=True)
        wait(lambda:status().get('joined',False) and status().get('actors',0)==3,'three-humans')
        world(2,'three-humans')
        wait(lambda:all(re.search(r'ANIMALTOWN ONLINE ACTOR DRAW slot '+str(slot)+r' species ',c['runtime_log_text']()) for slot in (1,2)),'both-remote-actors-drawn')
        report['remote_actors_drawn']=True
        report['remote_actor_samples']=[dict(slot=int(slot),species=int(species),instance_bytes=int(size),sampled_mem1_free_bytes=int(free))
            for slot,species,size,free in re.findall(r'ANIMALTOWN ONLINE ACTOR READY slot (\d+) species (\d+) instance bytes (\d+) sampled free (\d+)',c['runtime_log_text']())]
        shot('remote-actors-drawn')
        report['sdk_https']=True;report['sdk_wss']=True;report['three_humans']=True
        os.write(c['controller'],b'SET MAIN 0.5 0.1\n')
        try:wait(lambda:status().get('moved',False),'sdk-movement',seconds=45)
        finally:os.write(c['controller'],b'SET MAIN 0.5 0.5\n')
        shot('movement');report['sdk_movement']=True
        press('R');press('A');press('START')
        wait(lambda:status().get('chatReceived',False),'chat-received-by-both-guests')
        shot('chat');report['chat_both_guests']=True
        (fixture/'disconnect').write_text('Fence only the disposable console connection.\n')
        wait(lambda:status().get('fenced',False),'connection-fenced')
        wait(lambda:status().get('reconnected',False),'same-endpoint-reconnect',activate=True)
        world(3,'reconnected');report['same_endpoint_reconnect']=True
        press('START')
        for _ in range(3):press('D_DOWN')
        press('A')
        wait(lambda:status().get('returned',False),'card-published-and-acknowledged',seconds=300)
        def ready_journal():
            try:
                records=c['options_card_files'](root/'MemoryCardA.USA.raw');banks=[]
                for name in ('AnimalTown-Online0','AnimalTown-Online1'):
                    data=records.get(name,b'')
                    if len(data)==512 and struct.unpack_from('<I',data)[0]==0x31564e4f and zlib.crc32(data[:-4])==struct.unpack_from('<I',data,508)[0]:
                        banks.append((struct.unpack_from('<I',data,4)[0],struct.unpack_from('<I',data,8)[0],struct.unpack_from('<q',data,12)[0]))
                return bool(banks) and max(banks)[1:]==(0,status()['revision'])
            except (ValueError,OSError):return False
        wait(ready_journal,'ready-journal-after-acknowledgment',seconds=90)
        report['card_sequence_after']=c['committed_card_sequence'](root/'MemoryCardA.USA.raw')
        if report['card_sequence_after']<=report['card_sequence_before']:raise AssertionError('Online return did not publish a newer home card')
        shot('returned');report['published_and_acknowledged']=True;report['passed']=True
    finally:
        os.write(c['controller'],b'SET MAIN 0.5 0.5\n')
        (root/'online-play-report.json').write_text(json.dumps(report,indent=2)+'\n')


def finish(context,shutdown):
    # Reuse the production card extractor after Dolphin closes its file. Keep
    # the exported credential/travel records private and out of artifacts.
    c=context;root=c['root'];report=json.loads((root/'online-play-report.json').read_text())
    if not report['passed']:return
    if shutdown['forced_kill'] or not shutdown['clean_exit'] or shutdown['returncode']!=0:raise AssertionError('Online card verification requires a clean emulator shutdown')
    from jobs_smoke import write_records
    write_records(root/'private-records',c['options_card_files'](root/'MemoryCardA.USA.raw'))
