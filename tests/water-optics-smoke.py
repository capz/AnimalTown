"""Validate actual-draw evidence and water-image acceptance without Dolphin.

The production reader is loaded through AST to avoid starting the emulator.
Synthetic images exercise refraction colours and uniform-lighting controls;
they are not screenshots or evidence that the console renderer was executed.
"""
import ast
import math
from pathlib import Path

source=ast.parse(Path('tools/iso-smoke.py').read_text())
names={'water_draw_state','water_material_pixels','water_optics_pixels'}
functions=[node for node in source.body if isinstance(node,ast.FunctionDef) and node.name in names]
namespace={}
exec(compile(ast.Module(body=functions,type_ignores=[]),'<water inspection helpers>','exec'),namespace)
read=namespace['water_draw_state'];measure=namespace['water_material_pixels']

def draw(*,frame=31,scene=1,stages=12,indirect=3,texgens=7,blend=0,sun=255):
    shallow=scene in (1,2,3)
    constants=(f'k0=367f91{int(.18*sun+.5):02x} k1=d3f2ec1a ' if indirect==3
               else 'k0=367f91ff k1=d3f2ecff ') if shallow else 'k0=c4e4deff k1=e5f5edff '
    factors='1:0' if shallow and indirect==3 and blend==0 else '4:5'
    return (f'ANIMALTOWN WATER DRAW scene={scene} frame={frame} tev={stages} indirect={indirect} '
            f'strength={24 if shallow else 76} tex0=64x64 tex1=64x64 '
            +('uv0=2400000,0,0,2400000,200000,300000 uv1=2800000,0,0,2800000,400000,500000 '
              if shallow else 'uv0=1350000,0,0,1350000,200000,300000 uv1=1000000,0,0,1000000,400000,500000 ')
            +f'layers=0:0,1:0 texgens={texgens} matrices=30,33 '
            +constants+f'blend={blend}:{factors} depth=1:3:{0 if shallow else 1} layerstages=1,{3 if shallow else 2}\n')

def optics(**changes):
    fields=dict(scene=1,frame=31,ready=1,scope=2,part=91,captured=91,valid=1,
                size='320x240',source='640x480',bytes=169984,copies=91,draws='121,121,0',mask=7,base=4,failed=0,sun=255,environment='ffffff',
                textures='64x64:3,64x64:3,320x240:4,320x240:4',indirect='3:3,4:3',
                stages='5:256,5:4,255:255,6:5,6:6,255:255,6:5,255:255',
                generators='1:4:36,1:4:39,1:0:42,0:0:45',
                wave0='2200000,0,0,2200000,100000,200000',wave1='3100000,0,0,3100000,400000,500000',
                controls='140000,1500000,180000,100000')
    fields.update(changes)
    return 'ANIMALTOWN WATER OPTICS '+' '.join(f'{key}={value}' for key,value in fields.items())+'\n'

def rejected(log,fragment,*,legacy=False,require=True):
    try:read(log,1,legacy,require)
    except AssertionError as error:
        assert fragment in str(error),(fragment,str(error))
    else:raise AssertionError('Invalid water proof accepted: '+fragment)

normal=read(draw()+optics(),1,require_optics=True)
assert normal['surface']=='shallow' and normal['material_path']=='archived'
assert normal['optics']['part']==normal['optics']['captured']==91
assert read(draw(stages=13,sun=0)+optics(base=5,sun=0,environment='304060'),1,require_optics=True)['foam_constants']==['367f9100','d3f2ec1a']
assert read(draw(stages=13,sun=128)+optics(base=5,sun=128,environment='97c0c8'),1,require_optics=True)['foam_constants']==['367f9117','d3f2ec1a']
assert read(draw(stages=16)+optics(base=8),1,legacy=True,require_optics=True)['material_path']=='legacy'
assert read(draw(stages=16)+optics(base=8),1,require_optics=True) is None
assert read(draw()+optics(),1,legacy=True,require_optics=True) is None
assert read(draw(),1,require_optics=True) is None
assert read(draw()+optics(frame=30),1,require_optics=True) is None
assert read(draw()+optics()+draw(frame=61),1,require_optics=True) is None
assert read(draw()+optics().rstrip(),1,require_optics=True) is None
assert read(draw()+optics()[:-20],1,require_optics=True) is None
rejected(draw()+optics().replace(' controls=140000,1500000,180000,100000',''),'Incomplete water optics evidence')

# Ordinary coast/fallback programs retain the established exact contract.
assert read(draw(scene=4,stages=4,indirect=1,texgens=2),4)['surface']=='ocean'
fallback=draw(stages=4,indirect=1,texgens=2,blend=1)
assert read(fallback,1)['blend']==[1,4,5]
assert read(draw(stages=8,indirect=1,texgens=3,blend=1),1,legacy=True)['material_path']=='legacy'
failed=fallback+optics(ready=0,valid=0,mask=0,bytes=0,draws='0,0,1',failed=1)
assert read(failed,1)['optics']['failed']==1
rejected(failed,'safe fallback')
bump_only=draw(stages=7,texgens=6,blend=1)+optics(mask=3,valid=0,captured=90,
    stages='5:256,5:4,255:255,255:255,255:255,255:255,255:255,255:255')
assert read(bump_only,1)['optics']['mask']==3
rejected(bump_only,'safe fallback')
rejected(fallback+'ANIMALTOWN WATER OPTICS unavailable; keeping transparent water\n','allocation failed')

# Neither arbitrary larger budgets nor planned-but-unbound resources count as
# evidence. A current-part capture is mandatory whenever the shader samples it.
for changed in (draw(stages=10),draw(indirect=2),draw(texgens=5)):
    rejected(changed+optics(),'stage budget')
rejected(draw()+optics(base=6),'stage budget')
rejected(draw()+optics(environment='304060'),'stage budget')
rejected(draw()+optics(indirect='3:3,4:4'),'expected GX resources')
rejected(draw()+optics(textures='64x64:4,64x64:3,320x240:4,320x240:4'),'expected GX resources')
rejected(draw()+optics(stages='5:0,5:4,255:255,6:5,6:6,255:255,6:5,255:255'),'coordinate/stage bindings')
rejected(draw()+optics(stages='5:256,5:4,255:255,6:5,6:5,255:255,6:5,255:255'),'coordinate/stage bindings')
rejected(draw()+optics(generators='1:4:36,1:4:39,1:0:42,1:0:45'),'coordinate/stage bindings')
rejected(draw()+optics(wave0='2200000,0,0,2200000,-2147483647,0'),'periodic range')
rejected(draw()+optics(wave1='2200000,0,0,2200000,400000,500000'),'wrong scale')
rejected(draw()+optics(controls='140000,0,180000,100000'),'reviewed material defaults')
for changes in ({'captured':90},{'valid':0},{'copies':0},{'part':0,'captured':0},{'source':'640x528'},
                {'textures':'64x64:3,64x64:3,320x240:3,320x240:4'},
                {'textures':'64x64:3,64x64:3,320x240:4,320x240:3'},{'bytes':16384}):
    rejected(draw()+optics(**changes),'current-part capture')
rejected(draw(blend=1)+optics(),'blend/depth state')
rejected(draw().replace('blend=0:1:0','blend=0:4:5')+optics(),'blend/depth state')
rejected(draw(sun=0)+optics(),'ripple colour constant')
rejected(draw().replace('367f912e','007f912e')+optics(),'ripple colour constant')
rejected(draw()+optics(draws='5,6,0'),'draw accounting')
rejected(draw()+optics(scope=0),'scope/capabilities')
rejected(draw()+optics(sun=256),'scope/capabilities')
print('Water optics evidence: exact budgets/bindings, same-part freshness, complete pairs, unchanged coast and safe-fallback detection passed')

def image(phase=0,*,exposure=1,offset=0,flat=False):
    pixels=bytearray(bytes((90,145,65))*800*480)
    for y in range(80,290):
        for x in range(90,570):
            # Beige/grey pebbles under turquoise water intentionally occupy a
            # different colour family from the earlier two ripple colours.
            u=x+3*math.sin(x*.11+y*.07+phase)
            v=y+2*math.cos(x*.05-y*.13+phase)
            wave=0 if flat else math.sin(u*.35)+.5*math.cos(v*.31)
            base=(140+8*wave,168+9*wave,156+7*wave)
            at=(y*800+x)*3
            pixels[at:at+3]=bytes(max(0,min(255,round(value*exposure+offset))) for value in base)
    return bytes(pixels)

still=image();moved=image(1.8)
frozen=measure(still,still,optics=True)
motion=measure(still,moved,optics=True)
lighting=measure(still,image(exposure=.8,offset=6),optics=True)
flash=measure(image(flat=True),image(flat=True,offset=8),optics=True)
assert frozen['interior_samples']>10000 and frozen['local_pattern_changed_samples']==0 and frozen['local_edge_sign_changes']==0
assert motion['rgb_changed_samples']>1000 and motion['local_pattern_changed_samples']>250 and motion['local_edge_sign_changes']>100
assert lighting['local_pattern_changed_samples']<motion['local_pattern_changed_samples']//20
assert lighting['local_edge_sign_changes']==0
assert flash['rgb_changed_samples']>10000 and flash['local_pattern_changed_samples']==0 and flash['local_edge_sign_changes']==0
empty=measure(bytes(800*480*3),bytes(800*480*3),optics=True)
assert empty['interior_samples']==0

# Exercise the production camera polling without starting the emulator.
scenario=next(node for node in source.body if isinstance(node,ast.FunctionDef) and node.name=='terrain_materials')
navigation=[node for node in scenario.body if isinstance(node,ast.FunctionDef)
            and node.name in {'water_probe','water_wait','water_advance'}]
exec(compile(ast.Module(body=navigation,type_ignores=[]),'<water camera navigation>','exec'),namespace)

class Camera:
    def __init__(self,*,stalled=False,failed=False):
        self.now=0;self.scene=1;self.pending=None;self.polls=0;self.steps=[]
        self.stalled=stalled;self.failed=failed
    def monotonic(self):return self.now
    def sleep(self,seconds):self.now+=seconds
    def chord(self,button):
        assert button=='D_LEFT'
        assert self.pending is None,'A shortcut was sent before the previous camera drew'
        self.pending=self.scene+1;self.polls=0;self.steps.append(self.pending)
    def alive(self):
        if self.failed:raise AssertionError('Emulator stopped during terrain material capture')
    def log(self):
        self.polls+=1
        # Terrain residency can take longer than the chord's fixed sleeps.
        # Until then, the log and world screenshot still describe the old view.
        if self.pending is not None and self.polls>=26 and not self.stalled:
            self.scene=self.pending;self.pending=None
        return draw(scene=self.scene,stages=4,indirect=1,texgens=2,
                    blend=1 if self.scene in (1,2,3) else 0)
    def install(self):
        namespace.update(time=self,chord=self.chord,alive=self.alive,runtime_log_text=self.log)

camera=Camera();camera.install()
for scene in (2,3,4):
    acknowledged=namespace['water_advance'](scene)
    assert acknowledged['scene']==scene and camera.pending is None
assert camera.steps==[2,3,4] and camera.now>14.9
# The navigation acknowledgement can use safe fallback on an intermediate
# camera, but the final inland material acceptance still requires real optics.
camera.scene=1
assert namespace['water_probe'](1) is None,'Navigation weakened the final water optics gate'
for camera,fragment in ((Camera(stalled=True),'scene 2 (archived)'),
                        (Camera(failed=True),'Emulator stopped')):
    camera.install()
    try:namespace['water_advance'](2)
    except AssertionError as error:assert fragment in str(error),(fragment,str(error))
    else:raise AssertionError('Missing camera acknowledgement was accepted')
    assert camera.steps==[2]
    if camera.stalled:assert 180<=camera.now<181
print('Water camera navigation: delayed actual draws acknowledge every step; stalled/stopped scenes fail without skipping, and final optics acceptance stays strict')

# A colour flash, frozen scene, or equally moving control cannot establish waves.
gate=next(node for node in scenario.body if isinstance(node,ast.FunctionDef) and node.name=='moving_water')
def visual_assert(condition,message):
    if not condition:raise AssertionError(message)
namespace['visual_assert']=visual_assert
exec(compile(ast.Module(body=[gate],type_ignores=[]),'<water motion gate>','exec'),namespace)
namespace['moving_water']('synthetic refracted bed',motion,frozen)
for sample,control in ((frozen,frozen),(lighting,frozen),(flash,frozen),(motion,motion)):
    try:namespace['moving_water']('negative control',sample,control)
    except AssertionError:pass
    else:raise AssertionError('Non-water motion established a passing animation check')
print('Water optics pixels: refracted pebble colours and moving detail pass; frozen/exposure/colour-flash/empty and moving-control cases cannot prove waves')
