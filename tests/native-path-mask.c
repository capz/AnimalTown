#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include "dnd_draw.h"
#include "../tools/native-path-mask.h"
static void *allocation;
static int live,active,create_error,release_error,flushes;
bool dnd_device_active(void){return active!=0;}
int dnd_buffer_create(int bytes,int alignment){assert(!live&&alignment==32&&bytes==PATH_BYTES);if(create_error)return 0;allocation=aligned_alloc(32,PATH_BYTES);assert(allocation);live=1;return 1;}
uint64_t dnd_buffer_address(int token,int offset){assert(token==1&&offset==0&&live);return (uintptr_t)allocation;}
int dnd_draw_release(int token){assert(token==1&&live&&!active);if(release_error)return -1;free(allocation);allocation=NULL;live=0;return 0;}
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){assert(data==allocation&&bytes==PATH_BYTES&&!invalidate);flushes++;}
void dnd_draw_backend_publish_texture(void){}
#include "../tools/native-path-mask.c"
static int texel(int x,int z){return (path_pixels[path_offset(x,z)]>>(x%2?0:4))&15;}
int main(void){
    DndArray *a=calloc(1,sizeof(*a)+PATH_PACKED);assert(a);a->length=PATH_PACKED;a->element_size=1;
    assert(animaltown_paths_prepare(a,0,0)==1&&!live);
    create_error=1;assert(!animaltown_paths_prepare(a,1,1)&&!live);create_error=0;
    for(int i=0;i<PATH_SIDE*PATH_HEIGHT;i++)if(i%3==0)((uint8_t*)a->data)[i/4]|=(uint8_t)(1<<((i%4)*2));
    assert(animaltown_paths_prepare(a,1,1)==1&&live&&flushes==1);
    for(int z=0;z<128;z++)for(int x=0;x<128;x++)assert(texel(x,z)==(z<120&&(z*128+x)%3==0?15:0));
    memset(a->data,0,PATH_PACKED);assert(animaltown_paths_prepare(a,1,1)&&flushes==1&&texel(0,0)==15);
    assert(animaltown_paths_prepare(a,1,2)&&flushes==2&&texel(0,0)==0);
    active=1;assert(!animaltown_paths_prepare(a,0,0)&&live);active=0;
    release_error=1;assert(!animaltown_paths_prepare(a,0,0)&&live);release_error=0;
    assert(animaltown_paths_prepare(a,0,0)&&!live&&!animaltown_path_mask());
    assert(animaltown_paths_prepare(a,1,2)&&flushes==3);assert(animaltown_paths_prepare(a,0,0));
    a->length--;assert(!animaltown_paths_prepare(a,1,3)&&!live);free(a);
    puts("Paths: exact GX I4 cells/padding, 8192 charged bytes, revision-only uploads, safe retirement and retry passed");return 0;
}
