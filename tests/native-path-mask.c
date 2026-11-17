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
static uint32_t stamp(void){uint32_t n;memcpy(&n,path_pixels+PATH_STAMP,sizeof n);return n;}
/* Representative sparse hash probes: a live cell edit can miss every probe.
 * The padding word makes each actual publication observable to that cache. */
static uint32_t sampled(const uint8_t *p){uint32_t h=2166136261u;for(int at=0;at<PATH_BYTES;at+=64)for(int i=0;i<8;i++)h=(h^p[at+i])*16777619u;return h;}
int main(void){
    DndArray *a=calloc(1,sizeof(*a)+PATH_PACKED);assert(a);a->length=PATH_PACKED;a->element_size=1;
    DndArray *v=calloc(1,sizeof(*v)+PATH_VISITS);assert(v);v->length=PATH_VISITS;v->element_size=1;
    memset(v->data,255,PATH_VISITS);
    assert(animaltown_paths_prepare(a,v,0,0,0)==1&&!live);
    create_error=1;assert(!animaltown_paths_prepare(a,v,1,1,0)&&!live);create_error=0;
    for(int i=0;i<PATH_SIDE*PATH_HEIGHT;i++)if(i%3==0)((uint8_t*)a->data)[i/4]|=(uint8_t)(1<<((i%4)*2));
    assert(animaltown_paths_prepare(a,v,1,1,0)==1&&live&&flushes==1);
    for(int z=0;z<PATH_HEIGHT;z++)for(int x=0;x<PATH_SIDE;x++)assert(texel(x,z)==((z*128+x)%3==0?15:0));
    assert(PATH_STAMP==path_offset(0,PATH_HEIGHT)&&PATH_STAMP%64==0&&stamp()==1);
    for(int at=PATH_STAMP+4;at<PATH_BYTES;at++)assert(path_pixels[at]==0);
    memset(a->data,0,PATH_PACKED);assert(animaltown_paths_prepare(a,v,1,1,0)&&flushes==1&&texel(0,0)==15);
    assert(animaltown_paths_prepare(a,v,1,2,0)&&flushes==2&&texel(0,0)==0);
    uint8_t plain[PATH_BYTES];path_expand(plain,a->data,v->data);uint32_t old_plain=sampled(plain),old_live=sampled(path_pixels);
    int cell=36*PATH_SIDE+22;((uint8_t*)a->data)[cell/4]|=(uint8_t)(1<<((cell%4)*2));
    path_expand(plain,a->data,v->data);assert(sampled(plain)==old_plain); // edit lies between all sparse probes
    assert(animaltown_paths_prepare(a,v,1,3,0)&&flushes==3&&texel(22,36)==15&&sampled(path_pixels)!=old_live);
    // A complete byte-count sweep checks both nibbles, row boundaries and
    // the exact hidden/hint/full milestones without allocating another image.
    for(int i=0;i<256;i++){((uint8_t*)a->data)[i/4]|=(uint8_t)(1<<((i%4)*2));((uint8_t*)v->data)[i]=(uint8_t)i;}
    assert(animaltown_paths_prepare(a,v,1,4,0)&&flushes==4);
    int previous=0;
    for(int i=0;i<256;i++){int level=texel(i%128,i/128);assert(level>=previous&&level<=15);if(i<35)assert(level==0);if(i>=100)assert(level==15);previous=level;}
    assert(texel(35,0)==2&&texel(50,0)==5&&texel(70,0)==9&&texel(99,0)==14);
    ((uint8_t*)v->data)[35]=36;
    assert(animaltown_paths_prepare(a,v,1,4,0)&&flushes==4&&texel(35,0)==2);
    ((uint8_t*)v->data)[35]=40;
    assert(animaltown_paths_prepare(a,v,1,4,1)&&flushes==5&&texel(35,0)==3);
    assert(animaltown_paths_prepare(a,v,1,4,1)&&flushes==5);
    uint32_t last=stamp();
    active=1;assert(!animaltown_paths_prepare(a,v,0,0,0)&&live);active=0;
    release_error=1;assert(!animaltown_paths_prepare(a,v,0,0,0)&&live);release_error=0;
    assert(animaltown_paths_prepare(a,v,0,0,0)&&!live&&!animaltown_path_mask());
    assert(animaltown_paths_prepare(a,v,1,4,1)&&flushes==6&&stamp()!=last&&texel(35,0)==3);assert(animaltown_paths_prepare(a,v,0,0,0));
    a->length--;assert(!animaltown_paths_prepare(a,v,1,4,1)&&!live);a->length++;
    v->length--;assert(!animaltown_paths_prepare(a,v,1,4,1)&&!live);v->length++;
    v->elements_are_references=1;assert(!animaltown_paths_prepare(a,v,1,4,1)&&!live);
    free(a);free(v);
    puts("Paths: 35-visit gate, traffic-faded GX I4 cells, sparse-cache stamp, 8192 charged bytes, selection/opacity-only uploads and safe retirement passed");return 0;
}
