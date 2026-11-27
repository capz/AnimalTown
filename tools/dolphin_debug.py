"""Read fixed native diagnostics from the disposable child Dolphin process.

Dolphin 2503 keeps MEM1 at offset zero of its unlinked shared-memory file.
Only the known probe and bounded stack linkage words are read. Reports retain
code addresses and symbols, never general registers, heap bytes or RAM dumps.
"""
from bisect import bisect_right
from contextlib import contextmanager
from pathlib import Path
import json
import os
import re
import resource
import struct
import subprocess
import time

MEM1_START, MEM1_END = 0x80000000, 0x81800000
PROBE_MAGIC = 0x41544e50
STAGES = {1:'before-residency',2:'before-input',3:'before-online-tick',
          4:'after-online-tick',5:'before-draw',6:'after-draw',
          7:'before-service',8:'after-service',10:'tls-read',11:'tls-read-returned',
          12:'tls-write',13:'tls-write-returned',14:'tls-random',15:'tls-random-returned',
          30:'pose-pump',31:'pose-pump-returned',40:'close-begin',
          41:'pose-disposed',42:'websocket-disposed',43:'tls-disposed',
          50:'town-dispose',51:'before-model-stop',52:'after-model-stop',
          53:'online-actors-disposed',54:'town-disposed'}

def mem1(address,count):
    return 0<count<=64 and MEM1_START<=address<=MEM1_END-count

class NativeSymbols:
    def __init__(self,text):
        section=re.search(r'(?m)^\s*\.text\s+0x([0-9a-fA-F]+)\s+0x([0-9a-fA-F]+)',text)
        if section is None:raise ValueError('Native map has no text section')
        self.start=int(section[1],16);self.end=self.start+int(section[2],16)
        self.symbols={}
        for match in re.finditer(r'(?m)^\s*0x([0-9a-fA-F]+)\s+([A-Za-z_][A-Za-z0-9_.$]*)\s*$',text):
            address=int(match[1],16)
            if mem1(address,4):self.symbols[match[2]]=address
        self.code=sorted((address,name) for name,address in self.symbols.items() if self.start<=address<self.end)
        self.addresses=[address for address,_ in self.code]
    def describe(self,address):
        if not self.start<=address<self.end:return None
        index=bisect_right(self.addresses,address)-1
        if index<0:return None
        start,name=self.code[index]
        return {'address':f'0x{address:08x}','symbol':name[:240],'offset':address-start}

class DolphinMemory:
    def __init__(self,descriptor):self.descriptor=descriptor
    @classmethod
    def open(cls,process):
        if process.poll() is not None:raise RuntimeError('Diagnostic emulator is no longer running')
        expected=f'/dev/shm/dolphin-emu.{process.pid} (deleted)'
        for path in (Path('/proc')/str(process.pid)/'fd').iterdir():
            descriptor=-1
            try:
                if os.readlink(path)!=expected:continue
                descriptor=os.open(path,os.O_RDONLY)
                # Check again after opening to reject descriptor reuse.
                if os.readlink(f'/proc/self/fd/{descriptor}')==expected and os.fstat(descriptor).st_size>=MEM1_END-MEM1_START:
                    result=cls(descriptor);descriptor=-1;return result
            except (FileNotFoundError,PermissionError):continue
            finally:
                if descriptor>=0:os.close(descriptor)
        raise RuntimeError('Dolphin shared MEM1 file was not found')
    def read_memory(self,address,count):
        if not mem1(address,count):raise ValueError('Diagnostic memory range is invalid')
        data=os.pread(self.descriptor,count,address-MEM1_START)
        if len(data)!=count:raise ValueError('Diagnostic memory read was truncated')
        return data
    def __enter__(self):return self
    def __exit__(self,*ignored):os.close(self.descriptor)

def stack_trace(target,symbols,stack):
    result=[];seen=set()
    for _ in range(16):
        if stack in seen or stack&7 or not mem1(stack,8):break
        seen.add(stack)
        following,link=struct.unpack('>II',target.read_memory(stack,8))
        code=symbols.describe(link)
        if code is not None:result.append({'stack':f'0x{stack:08x}',**code})
        if following<=stack or following-stack>1024*1024:break
        stack=following
    return result

def native_sample(target,symbols):
    address=symbols.symbols['animaltown_online_probe_state']
    before=target.read_memory(address,20)
    magic,stage,stack,link,sequence=struct.unpack('>IIIII',before)
    if magic!=PROBE_MAGIC:raise ValueError('Native probe signature changed')
    result={'stage':stage,'name':STAGES.get(stage,'unknown'),'sequence':sequence,
            'caller':symbols.describe(link),'consistent':False,'stack':[]}
    if sequence&1:return result
    frames=stack_trace(target,symbols,stack)
    result['consistent']=target.read_memory(address,20)==before
    if result['consistent']:result['stack']=frames
    return result

def capture_native_stall(process,map_path,report_path):
    report={'version':1,'samples':[]}
    try:
        symbols=NativeSymbols(Path(map_path).read_text())
        with DolphinMemory.open(process) as memory:
            for index in range(2):
                report['samples'].append(native_sample(memory,symbols))
                if index==0:time.sleep(.2)
        report['progressed']=report['samples'][0]['sequence']!=report['samples'][1]['sequence']
    except Exception as error:report['diagnostic_error']=type(error).__name__
    threads=[]
    try:
        for thread in sorted((Path('/proc')/str(process.pid)/'task').iterdir())[:64]:
            try:
                wait=(thread/'wchan').read_text().strip()
                if re.fullmatch(r'[A-Za-z0-9_]+',wait) is None:wait='unavailable'
                threads.append({'id':int(thread.name),'name':(thread/'comm').read_text().strip()[:32],'wait':wait})
            except OSError:pass
    except OSError:pass
    report['host_threads']=threads
    Path(report_path).write_text(json.dumps(report,sort_keys=True,indent=2)+'\n')
    print('Online native stall diagnostic: '+json.dumps(report,sort_keys=True),flush=True)
    return report

@contextmanager
def trace_host_shutdown(process,report_path):
    """Trace a failing host teardown without accepting an unclean card flush.

    Attach only after the controller scenario ends, to this disposable child.
    The debugger prints bounded symbolic stacks with arguments/locals disabled,
    then passes the original fatal signal. No core file or memory dump is made.
    """
    report_path=Path(report_path);debugger=None
    with report_path.open('w') as output:
        try:
            if process.poll() is None:
                resource.prlimit(process.pid,resource.RLIMIT_CORE,(0,0))
                debugger=subprocess.Popen(
                    ['gdb','--quiet','--batch','--nx','--nh',
                     '--init-eval-command=set debuginfod enabled off',
                     '--init-eval-command=set auto-load off',
                     '--init-eval-command=set print frame-arguments none',
                     '--init-eval-command=set print entry-values no',
                     '--init-eval-command=set print thread-events off',
                     '--pid='+str(process.pid),
                     '--command='+str(Path(__file__).with_name('dolphin-shutdown.gdb'))],
                    env=dict(os.environ,DEBUGINFOD_URLS=''),stdout=output,stderr=subprocess.STDOUT)
                deadline=time.monotonic()+5
                while debugger.poll() is None and time.monotonic()<deadline:
                    if 'ANIMALTOWN HOST SHUTDOWN TRACE READY' in report_path.read_text(errors='replace'):
                        break
                    time.sleep(.025)
                else:
                    print('Online host shutdown debugger did not attach; clean-exit checks remain required.',flush=True)
                    debugger.terminate()
                    try:debugger.wait(timeout=5)
                    except subprocess.TimeoutExpired:debugger.kill();debugger.wait()
                    debugger=None
        except OSError as error:
            print('Online host shutdown diagnostic unavailable: '+type(error).__name__,flush=True)
        try:
            yield
        finally:
            if debugger is not None:
                try:debugger.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    debugger.terminate()
                    try:debugger.wait(timeout=5)
                    except subprocess.TimeoutExpired:debugger.kill();debugger.wait()
    diagnostic=report_path.read_text(errors='replace')
    if len(diagnostic)>16000:diagnostic=diagnostic[:8000]+'\n[bounded shutdown trace]\n'+diagnostic[-8000:]
    print('Online host shutdown trace:\n'+diagnostic,flush=True)
