/* Exercise the shipped mount overlay, including libogc's failed-but-attached
 * state. No physical card, formatting or save deletion is involved. */
#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define CARD_WORKAREA (5*8192)
#define CARD_ERROR_FATAL_ERROR -128
#define CARD_ERROR_BUSY -1
static void *workareas[2],*attached;
static char games[2][5],companies[2][3];
static bool card_initialized;
static int animaltown_card_debug,mount_result,recovery_result,manager_active;
int animaltown_card_mount_error,animaltown_card_recovery_error;
static int initialized,mounts,unmounts,allocations,recoveries,allocation_failure;
static int dnd_cmgr_active(int slot){assert(slot==0||slot==1);return manager_active;}
static void CARD_Init(const char *game,const char *company)
{assert(!strcmp(game,"AMTE")&&!strcmp(company,"01"));initialized++;}
static void *test_allocate(size_t alignment,size_t size)
{
    assert(alignment==32&&size==CARD_WORKAREA);
    if(allocation_failure)return NULL;
    void *area=aligned_alloc(alignment,size);assert(area);allocations++;return area;
}
static void test_free(void *area)
{assert(area&&attached!=area);free(area);allocations--;}
static int CARD_Mount(int slot,void *area,void *callback)
{assert(slot==0&&callback==NULL&&!attached);attached=area;mounts++;return mount_result;}
static void CARD_Unmount(int slot)
{assert(slot==0&&attached);attached=NULL;unmounts++;}
static int dnd_gc_card_recover(int device)
{
    assert(device==4&&attached==workareas[0]);
    assert(!strcmp(games[0],"AMTE")&&!strcmp(companies[0],"01"));
    recoveries++;animaltown_card_debug=2;return recovery_result;
}
#define memalign test_allocate
#define free test_free
#include "../tools/native-card-mount.c.inc"
#undef free
#undef memalign
int main(void)
{
    const int failures[]={-1,-2,-3,-5,-6,-13,-128};
    for(unsigned i=0;i<sizeof(failures)/sizeof(failures[0]);i++){
        animaltown_card_debug=2;mount_result=failures[i];
        assert(!dnd_gc_card_mount(4,"AMTE","01"));
        assert(animaltown_card_mount_error==failures[i]&&animaltown_card_recovery_error==0);
        assert(animaltown_card_debug==0&&!attached&&!workareas[0]&&allocations==0&&recoveries==0);
    }
    assert(initialized==1&&mounts==7&&unmounts==7);
    mount_result=0;
    const int recovery_failures[]={-28,-5,-12};
    for(unsigned i=0;i<sizeof(recovery_failures)/sizeof(recovery_failures[0]);i++){
        recovery_result=recovery_failures[i];
        assert(!dnd_gc_card_mount(4,"AMTE","01"));
        assert(animaltown_card_mount_error==0&&animaltown_card_recovery_error==recovery_result);
        assert(animaltown_card_debug==2&&!attached&&!workareas[0]&&allocations==0);
    }
    recovery_result=0;allocation_failure=1;
    assert(!dnd_gc_card_mount(4,"AMTE","01")&&animaltown_card_mount_error==-2001&&allocations==0);
    allocation_failure=0;manager_active=1;
    assert(!dnd_gc_card_mount(4,"AMTE","01")&&animaltown_card_mount_error==-1);
    manager_active=0;
    assert(!dnd_gc_card_mount(3,"AMTE","01")&&animaltown_card_mount_error==-128);
    assert(!dnd_gc_card_mount(6,"AMTE","01")&&animaltown_card_mount_error==-128);
    assert(dnd_gc_card_mount(4,"AMTE","01")&&allocations==1&&attached==workareas[0]);
    assert(animaltown_card_mount_error==0&&animaltown_card_recovery_error==0);
    CARD_Unmount(0);test_free(workareas[0]);workareas[0]=NULL;
    puts("Card mount: failed attachment cleanup, exact driver/recovery errors, ownership, allocation and retry passed (host fixture).");
    return 0;
}
