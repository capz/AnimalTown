#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include "../tools/native-shadow-union.h"
static SuState *s;
static const float rect[]={0,0,8,0,8,8,0,0,8,8,0,8};
static const float bounds[]={1,1,7,7};
static int assertions;
#define CHECK(x) do{assert(x);assertions++;}while(0)
static void reset(void){su_init(s);memset(s->shapes,255,sizeof(s->shapes));su_begin(s,0,1,0);}
static void caster(int i,int profile,const float *uv,int n,const float *b){su_mesh(s,i,n,profile,uv,b);su_active(s,i,1,4,4);}
static void next(void){su_begin(s,0,1,0);}
static int pixel(float x,float z){int p=su_find(s,(int)floorf(x/8),(int)floorf(z/8));CHECK(p>=0);int a=(int)((x-s->page[p].x*8)*62/8+1),b=(int)((z-s->page[p].z*8)*62/8+1);return s->pixels[p*SU_BYTES+su_offset(a,b)];}
static int credits;
static int allow_work(void *unused){(void)unused;return credits-->0;}
int main(void){
    s=calloc(1,su_storage_bytes());CHECK(s!=NULL);CHECK(su_storage_bytes()<1536*1024);
    reset();caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    uint8_t original[SU_BYTES];int page=su_find(s,0,0);memcpy(original,s->pixels+page*SU_BYTES,SU_BYTES);
    for(int i=1;i<32;i++)caster(i,i%4,rect,6,bounds);
    su_prepare(s,SU_PAGES);CHECK(memcmp(original,s->pixels+page*SU_BYTES,SU_BYTES)==0);
    /* Duplicate trees/houses and opaque moving circles cannot compound. */
    for(int i=32;i<39;i++)caster(i,4,rect,6,bounds);
    su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    for(int i=0;i<SU_BYTES;i++)CHECK(s->pixels[page*SU_BYTES+i]<=SU_OPACITY);
    /* Idling does not republish or rasterize mask pages. */
    next();for(int i=32;i<39;i++)su_active(s,i,1,4,4);su_prepare(s,SU_PAGES);CHECK(s->changed==0);
    /* Remove scenery, move every circle, and verify the previous footprint
     * is restored from the static cache rather than leaving dark trails. */
    for(int i=0;i<32;i++)su_active(s,i,0,0,0);
    next();for(int i=32;i<39;i++)su_active(s,i,1,6,6);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==0);CHECK(pixel(6,6)==SU_OPACITY);
    next();su_prepare(s,SU_PAGES);CHECK(pixel(6,6)==0);
    /* Soft masks merge by maximum, independently of caster draw order. */
    reset();memset(s->shapes,128,4096);memset(s->shapes+4096,192,4096);
    caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);int faint=pixel(4,4);
    caster(1,1,rect,6,bounds);su_prepare(s,SU_PAGES);int strong=pixel(4,4);CHECK(strong>faint&&strong<SU_OPACITY);
    su_active(s,0,0,0,0);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==strong);
    /* Adjacent triangle diagonals and overlapping mat/floor geometry union
     * before the support guard, without cracks or duplicate darkening. */
    reset();caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(3,3)==SU_OPACITY);CHECK(pixel(5,5)==SU_OPACITY);
    float duplicate[24];memcpy(duplicate,rect,sizeof(rect));memcpy(duplicate+12,rect,sizeof(rect));
    su_mesh(s,0,12,0,duplicate,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    /* A cliff half-plane leaves a guard against bilinear sampling onto the
     * disconnected lower surface. No coverage outside the clipped receiver. */
    reset();const float half[]={0,0,8,0,0,8};const float all[]={0,0,8,8};caster(0,0,half,3,all);su_prepare(s,SU_PAGES);
    CHECK(pixel(1,1)==SU_OPACITY);CHECK(pixel(6,6)==0);CHECK(pixel(4,4)==0);
    /* Texture gutters on both sides of a page boundary use identical world
     * sample positions. Native placement never aliases distant world tiles. */
    reset();float across[12];for(int i=0;i<6;i++){across[i*2]=rect[i*2]+4;across[i*2+1]=rect[i*2+1];}
    const float ab[]={6,1,10,7};caster(0,0,across,6,ab);su_prepare(s,SU_PAGES);int left=su_find(s,0,0),right=su_find(s,1,0);CHECK(left>=0&&right>=0);
    CHECK(s->pixels[left*SU_BYTES+su_offset(62,31)]==s->pixels[right*SU_BYTES+su_offset(0,31)]);
    CHECK(s->pixels[left*SU_BYTES+su_offset(63,31)]==s->pixels[right*SU_BYTES+su_offset(1,31)]);
    su_begin(s,3,1,4);su_prepare(s,SU_PAGES);CHECK(su_find(s,0,0)<0);su_begin(s,0,1,0);su_prepare(s,SU_PAGES);CHECK(pixel(7,4)==SU_OPACITY);
    /* Worst-case separated footprints: four pages each, 39 casters. */
    reset();for(int i=0;i<39;i++){
        float x=(i%8+1)*16.f,z=(i/8+1)*16.f,uv[12],b[4]={x-1,z-1,x+1,z+1};
        for(int j=0;j<6;j++){uv[j*2]=rect[j*2]+x-4;uv[j*2+1]=rect[j*2+1]+z-4;}
        su_mesh(s,i,6,i<32?0:4,uv,b);su_active(s,i,1,x,z);
    }su_prepare(s,SU_PAGES);int used=0;for(int i=0;i<SU_PAGES;i++)used+=s->page[i].needed;
    CHECK(used==156);CHECK(s->overflow==0);
    /* Static work stays bounded while dynamic coverage remains current. */
    reset();caster(0,0,rect,6,bounds);caster(32,4,rect,6,bounds);
    su_prepare(s,0);CHECK(s->static_rasters==0);CHECK(pixel(4,4)==SU_OPACITY);
    next();su_active(s,32,1,5,5);su_prepare(s,1);CHECK(s->static_rasters<=SU_STATIC);CHECK(pixel(5,5)==SU_OPACITY);
    next();su_prepare(s,1);CHECK(pixel(4,4)==SU_OPACITY);
    /* Hash collisions, deletion within a probe cluster, and slot recycling. */
    reset();int keys[3],found=0;unsigned hash=su_hash(0,0,0);
    for(int x=0;found<3;x++)if(su_hash(0,x,0)==hash)keys[found++]=x;
    for(int i=0;i<3;i++){s->page[i]=(SuPage){.used=1,.x=keys[i]};su_index(s,i);}
    for(int i=0;i<3;i++)CHECK(su_find(s,keys[i],0)==i);
    su_unindex(s,0);s->page[0].used=0;
    CHECK(su_find(s,keys[0],0)==-1);CHECK(su_find(s,keys[1],0)==1);CHECK(su_find(s,keys[2],0)==2);
    su_unindex(s,1);s->page[1].used=0;CHECK(su_find(s,keys[2],0)==2);
    /* Travel forces repeated eviction; every hash entry must agree with the
     * page keys across outdoor/indoor spaces and resized room pages. */
    reset();for(int frame=0;frame<240;frame++){
        int space=frame%17==0?3:0;su_begin(s,space,1,space?(frame>230?12:4):0);
        float x=frame*8.f+4,z=(frame%7)*8.f+4,uv[12],b[4]={x-3,z-3,x+3,z+3};
        for(int j=0;j<6;j++){uv[j*2]=rect[j*2]+x-4;uv[j*2+1]=rect[j*2+1]+z-4;}
        su_mesh(s,32,6,4,uv,b);su_active(s,32,1,x,z);su_prepare(s,1);
        CHECK(!s->overflow);CHECK(su_find(s,su_floor(x/s->span),su_floor(z/s->span))>=0);
        for(int i=0;i<SU_PAGES;i++)if(s->page[i].used){
            int current=s->space;s->space=s->page[i].space;
            CHECK(su_find(s,s->page[i].x,s->page[i].z)==i);s->space=current;
        }
        su_prepare(s,1);CHECK(!s->layout_dirty);
    }
    /* Moving a circle reuses support; changing its receiver invalidates it. */
    reset();caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    next();su_active(s,32,1,4.1f,4);su_prepare(s,SU_PAGES);
    CHECK(s->support_builds==0&&s->support_hits>0);
    su_mesh(s,32,3,4,half,all);su_active(s,32,1,6,6);su_prepare(s,SU_PAGES);
    CHECK(s->support_builds>0);CHECK(pixel(6,6)==0);
    /* An exhausted deadline removes the old moving footprint, retains the
     * job, and can finish it on a later frame without trails. */
    reset();caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    next();su_active(s,32,1,6,6);credits=0;su_prepare_budget(s,1,allow_work,NULL);
    CHECK(s->deferred>0);CHECK(pixel(4,4)==0);CHECK(pixel(6,6)==0);
    credits=1000;su_prepare_budget(s,1,allow_work,NULL);CHECK(pixel(6,6)==SU_OPACITY);CHECK(pixel(4,4)==0);
    /* Static composition yields between casters. Invalidating a paused job
     * discards its scratch union, so a removed tree cannot reappear. */
    reset();caster(0,0,rect,6,bounds);credits=2;su_prepare_budget(s,1,allow_work,NULL);
    CHECK(s->static_page>=0&&s->static_caster<SU_STATIC);CHECK(pixel(4,4)==0);
    su_active(s,0,0,0,0);credits=1000;su_prepare_budget(s,1,allow_work,NULL);
    credits=1000;su_prepare_budget(s,1,allow_work,NULL);CHECK(pixel(4,4)==0);
    /* The two-triangle flat shortcut and the old subdivided receiver must
     * produce identical final pixels, including the erosion guard. */
    reset();float grid[432];int at=0;
    for(int z=0;z<6;z++)for(int x=0;x<6;x++){
        float v[]={x,z,x+1,z,x+1,z+1,x,z,x+1,z+1,x,z+1};memcpy(grid+at,v,sizeof(v));at+=12;
    }
    const float square[]={0,0,6,0,6,6,0,0,6,6,0,6},box[]={0,0,6,6};
    caster(32,4,grid,216,box);su_prepare(s,SU_PAGES);
    uint8_t *pixels=malloc(SU_PAGES*SU_BYTES);CHECK(pixels!=NULL);memcpy(pixels,s->pixels,SU_PAGES*SU_BYTES);
    su_mesh(s,32,6,4,square,box);su_active(s,32,1,4,4);su_prepare(s,SU_PAGES);
    CHECK(memcmp(pixels,s->pixels,SU_PAGES*SU_BYTES)==0);free(pixels);
    free(s);printf("Shadow union: %d checks; max overlap, soft edges, trails, clipping, seams, rooms and 156-page bound passed\n",assertions);return 0;
}
