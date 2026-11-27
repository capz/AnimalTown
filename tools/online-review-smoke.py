"""Controller capture of the real closure review; cancel before networking."""
import json
import os
import time


def run(context):
    root=context['root'];press=context['press'];screenshot=context['screenshot'];rgb=context['rgb']
    wait_title=context['wait_title'];check_runtime_failure=context['check_runtime_failure']
    controller=context['controller'];card=root/'MemoryCardA.USA.raw'
    before=context['committed_card_sequence'](card)
    if before<=0:raise AssertionError('Use the disposable online-review fixture card')

    def samples(pixels,bounds):
        x0,y0,x1,y1=bounds
        return [tuple(pixels[(y*800+x)*3:(y*800+x)*3+3]) for y in range(y0,y1,4) for x in range(x0,x1,4)]

    left=(50,100,300,350);right=(380,110,590,280)
    def cream(pixel):return pixel[0]>205 and pixel[1]>195 and pixel[2]>165 and pixel[0]>pixel[2]+12
    def difference(a,b,bounds):
        first=samples(a,bounds);second=samples(b,bounds)
        return sum(max(abs(x-y) for x,y in zip(p,q))>16 for p,q in zip(first,second))/len(first)

    # Options is the final visible title row with debug menus disabled.
    press('D_UP');press('A');wait_title(modal=True)
    for _ in range(3):press('D_DOWN')
    press('A');screenshot('online-review-erase-confirmation.png')
    press('D_RIGHT');press('A')
    ready=False;deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        check_runtime_failure();time.sleep(2);screenshot('online-review-loading.png')
        pixels=rgb('online-review-loading.png');scene=samples(pixels,left);panel=samples(pixels,right)
        if sum(cream(p) for p in panel)/len(panel)>.55 and sum(cream(p) for p in scene)/len(scene)<.35 and len(set(scene))>=80:
            ready=True;break
    if not ready:raise AssertionError('Current town and right-side closure UI did not become drawable')
    screenshot('online-review-current-town.png');initial=rgb('online-review-current-town.png')
    os.write(controller,b'SET MAIN 0.9 0.5\n')
    try:time.sleep(3)
    finally:os.write(controller,b'SET MAIN 0.5 0.5\n')
    time.sleep(2);screenshot('online-review-camera-inspection.png');inspected=rgb('online-review-camera-inspection.png')
    world_change=difference(initial,inspected,left);panel_change=difference(initial,inspected,right)
    if world_change<.01 or panel_change>.02:raise AssertionError('Camera inspection must change the left world while preserving the right review details')
    press('B');wait_title();screenshot('online-review-cancelled.png')
    after=context['committed_card_sequence'](card)
    if after!=before:raise AssertionError('Cancelling closure changed the committed home save')
    (root/'online-review-report.json').write_text(json.dumps({
        'current_world_left':True,'review_ui_right':True,'world_changed_fraction':world_change,
        'review_details_changed_fraction':panel_change,'card_sequence_before':before,'card_sequence_after':after,
        'cancelled_before_networking':True,'online_or_physical_transport_qualification':False,
    },indent=2)+'\n')
    print('Actual split review, camera inspection and cancellation retained the home save; this scenario does not qualify the online route.',flush=True)
