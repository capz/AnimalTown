#define _POSIX_C_SOURCE 200809L
#include "dnd_managed.h"
#include "dnd_launch.h"
#include "dnd_storage.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

/* Production registry/path/async code with a host mount backend. The current
 * FST disc is represented by a dvd: directory; this deliberately does not
 * emulate a physical drive, SD adapter, launcher or FAT implementation. */
static const char *roots[]={"carda:","cardb:","sd:","dvd:","mca:","mcb:","gcloader:"};
static char attempts[64];
static unsigned attempt_length;
static int image_mounts,profile_calls,profile_reuse;
static bool exists(const char *path){struct stat info;return stat(path,&info)==0&&S_ISDIR(info.st_mode);}
static void attempted(int device){
    assert(attempt_length+3<sizeof(attempts));
    if(attempt_length)attempts[attempt_length++]=',';
    attempts[attempt_length++]=(char)('0'+device);attempts[attempt_length]=0;
}
int FAT_getAttr(const char *path){(void)path;return 0;}
int FAT_setAttr(const char *path,unsigned attributes){(void)path;(void)attributes;return 0;}
int dnd_gc_contiguous_extents(int device,const char *path,unsigned *extents)
{(void)device;(void)path;(void)extents;return -ENOTSUP;}
bool dnd_gc_mount_volume(const DndMountConfiguration *request,DndMountConfiguration *effective){
    int device=request->device;assert(device>=0&&device<7&&device!=3&&device!=4&&device!=5);attempted(device);
    if(!exists(roots[device]))return false;
    *effective=*request;effective->partition_lba=0;effective->filesystem=1;return true;
}
bool dnd_gc_mount_disc(int format){assert(format==2);attempted(3);return exists(roots[3]);}
bool dnd_gc_mount(int device){assert(device==3);return dnd_gc_mount_disc(2);}
bool dnd_gc_mount_disc_image(const char *path){(void)path;image_mounts++;return false;}
void dnd_gc_unmount(int device){(void)device;}
bool dnd_gc_storage_unmount(int device){dnd_gc_unmount(device);return true;}
int dnd_gc_storage_presence(int device){return exists(roots[device])?1:0;}
void dnd_gc_storage_invalidate(int device){(void)device;}
void dnd_performance_frame_reuse(int enabled){profile_calls++;profile_reuse=enabled;}
void dnd_console_write_line(const char *text){puts(text);}
void dnd_console_write_managed_line(DndString *text){
    char output[512];unsigned count=text->length<511?text->length:511;
    for(unsigned i=0;i<count;i++)output[i]=text->chars[i]<128?(char)text->chars[i]:'?';
    output[count]=0;puts(output);
}
uint16_t dnd_input_buttons_down(unsigned port){(void)port;return 0;}
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static int setting(const char *name,int fallback){const char *value=getenv(name);return value?atoi(value):fallback;}
int main(int argc,char **argv){
    dnd_launch_capture(argc-1,(const char *const *)(argv+1));
    static unsigned char memory[2*1024*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));
    dnd_gc_set_stress(getenv("ANIMALTOWN_CONTENT_GC_STRESS")!=NULL);
    int borrowed=setting("ANIMALTOWN_CONTENT_BORROW",-1);DndMountInfo before={0};
    if(borrowed>=0){
        assert(borrowed==3?dnd_storage_mount_disc(2):dnd_storage_mount(borrowed));
        assert(dnd_storage_get_mount_info(borrowed,&before));attempts[0]=0;attempt_length=0;
    }
    intptr_t result=dnd_aot_entry(&heap);int handles=dnd_fs_open_handles(),mask=0;
    for(int device=0;device<7;device++)if(dnd_storage_is_mounted(device))mask|=1<<device;
    const char *expected_attempts=getenv("ANIMALTOWN_CONTENT_ATTEMPTS");
    int expected=setting("ANIMALTOWN_CONTENT_RESULT",0),expected_mask=setting("ANIMALTOWN_CONTENT_MOUNTS",0);
    bool okay=result==expected&&!dnd_exception_pending()&&!handles&&mask==expected_mask&&!image_mounts&&
        expected_attempts&&!strcmp(attempts,expected_attempts)&&profile_calls==(expected==0?1:0)&&profile_reuse==0;
    if(borrowed>=0){DndMountInfo after={0};okay&=dnd_storage_get_mount_info(borrowed,&after)&&after.mount_id==before.mount_id&&dnd_storage_is_mounted(borrowed);}
    if(!okay){
        fprintf(stderr,"Content startup result=%ld expected=%d mounts=%d expected=%d attempts=%s expected=%s handles=%d profile_calls=%d image_mounts=%d exception=%s\n",
            (long)result,expected,mask,expected_mask,attempts,expected_attempts?expected_attempts:"unset",handles,profile_calls,image_mounts,dnd_exception_message());return 1;
    }
    dnd_storage_shutdown();assert(!dnd_exception_pending()&&!dnd_fs_open_handles());
    puts("Production bootstrap: profile policy, mount trace, lifetime, runtime config and sync/async reads passed");return 0;
}
