#include <assert.h>
#include <stdio.h>
#include "dnd_draw.h"
#include "native-resource-limits.h"
int dnd_raw_depth(void){return 0;}
bool dnd_device_active(void){return false;}
void dnd_draw_backend_wait(void){}
int main(void){
    int handles[ANIMALTOWN_DRAW_RESOURCE_SLOTS];
    assert(dnd_draw_live()==0);
    for(int i=0;i<ANIMALTOWN_DRAW_RESOURCE_SLOTS;i++){
        handles[i]=dnd_buffer_create(32,32);assert(handles[i]>0);
        assert((handles[i]&1023)>0&&(handles[i]&1023)<=ANIMALTOWN_DRAW_RESOURCE_SLOTS);
        for(int j=0;j<i;j++)assert(handles[i]!=handles[j]);
    }
    assert(dnd_draw_live()==ANIMALTOWN_DRAW_RESOURCE_SLOTS);
    assert(dnd_draw_total_bytes()==ANIMALTOWN_DRAW_RESOURCE_SLOTS*32);
    assert(dnd_buffer_create(32,32)==0);
    int stale=handles[0];
    for(int i=0;i<ANIMALTOWN_DRAW_RESOURCE_SLOTS;i+=2)assert(dnd_draw_release(handles[i])==0);
    for(int i=0;i<ANIMALTOWN_DRAW_RESOURCE_SLOTS;i+=2){handles[i]=dnd_buffer_create(32,32);assert(handles[i]>0&&handles[i]!=stale);}
    assert(dnd_draw_release(stale)==-1);
    assert(dnd_buffer_hold(handles[1])==0);assert(dnd_draw_release(handles[1])==0);
    for(int i=0;i<ANIMALTOWN_DRAW_RESOURCE_SLOTS;i++)if(i!=1)assert(dnd_draw_release(handles[i])==0);
    assert(dnd_draw_live()==1);assert(dnd_buffer_unhold(handles[1])==0);
    assert(dnd_draw_live()==0&&dnd_draw_total_bytes()==0);
    printf("Native resource slots: %d unique handles, full-table rejection, reuse, stale-token rejection and pinned release passed\n",ANIMALTOWN_DRAW_RESOURCE_SLOTS);
    return 0;
}
