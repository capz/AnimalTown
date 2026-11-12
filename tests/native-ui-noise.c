#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include "draw_resources.c"
TownFrameCounters town_frame_counters;
static int pass,draws,seen[16];
int dnd_raw_depth(void){return 0;}
bool dnd_device_active(void){return pass!=0;}
int dnd_device_pass(void){return pass;}
int dnd_device_pass_begin(int p){if(pass)return -1;pass=p;return 0;}
int dnd_device_pass_end(int p){if(pass!=p)return -1;pass=0;return 0;}
int dnd_device_width(void){return 640;}
int dnd_device_height(void){return 480;}
void dnd_draw_backend_wait(void){}
void dnd_performance_add_triangles(uint32_t n){(void)n;}
void dnd_performance_drawing_api(void){}
void dnd_draw_backend_flush(void *p,int n,int op){(void)p;(void)n;(void)op;}
void dnd_draw_backend_2d(int w,int h){assert(w==640&&h==480);}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int prim,const DndDrawTexture *texture,const DndDrawMaterial *m,const float *world){
    assert(v&&n>0&&prim==0x90&&world&&m);seen[draws++]=texture?animaltown_ui_noise_match(texture->data)?1:2:0;
}
static DndArray *array(int count,int size){DndArray *a=calloc(1,sizeof(*a)+(size_t)count*size);assert(a);a->length=count;a->element_size=size;return a;}
int main(void){
    /* A complete tiny I8 package exercises the actual SDK resource allocator. */
    DndArray *package=array(160,1);unsigned char *p=package->data;
    memcpy(p,"DNTX",4);p[4]=2;p[8]=1;p[12]=8;p[16]=4;p[20]=128;p[24]=4;memset(p+32,240,128);
    assert(animaltown_ui_noise_prepare(package)==0&&dnd_draw_live()==1);
    int first=town_ui_noise_texture;assert(dnd_draw_bytes(first)==128);
    int canvas=dnd_canvas_create(120),atlas=dnd_texture_allocate_native(8,4,1,1);assert(canvas&&atlas);
    assert(animaltown_ui_noise_scope(1)==-1);
    assert(dnd_canvas_begin(canvas,640,480)==0);
    assert(animaltown_ui_noise_prepare(package)==-1);
    assert(dnd_canvas_rectangle(canvas,0,0,20,20,0xff000080)==0);
    assert(animaltown_ui_noise_scope(1)==0&&draws==1&&seen[0]==0);
    assert(animaltown_ui_noise_scope(1)==0&&draws==1);
    assert(dnd_canvas_rectangle(canvas,5,5,15,15,0xffffff80)==0);
    DndArray *v=array(12,4);float *points=(float*)v->data;points[4]=10;points[9]=10;
    assert(dnd_canvas_vertices(canvas,atlas,v,3,0xffffffff)==0&&draws==2&&seen[1]==1);
    assert(animaltown_ui_noise_scope(0)==0&&draws==3&&seen[2]==2);
    assert(dnd_canvas_rectangle(canvas,0,0,10,10,0xffffffff)==0);
    assert(dnd_canvas_end(canvas)==0&&draws==4&&seen[3]==0);
    DndArray *meshVertices=array(3,24),*world=array(16,4);float *matrix=(float*)world->data;
    matrix[0]=matrix[5]=matrix[10]=matrix[15]=1;
    int mesh=dnd_mesh_create(meshVertices,NULL,1,0);assert(mesh>0);
    assert(dnd_canvas_begin(canvas,640,480)==0&&animaltown_ui_noise_scope(1)==0);
    assert(dnd_canvas_geometry(canvas,mesh,0,world,0xffffffff)==0&&draws==5&&seen[4]==1);
    assert(animaltown_ui_noise_scope(0)==0&&dnd_canvas_end(canvas)==0);
    assert(dnd_draw_release(mesh)==0);free(world);free(meshVertices);
    assert(animaltown_ui_noise_prepare(package)==0&&town_ui_noise_texture!=first);
    assert(animaltown_ui_noise_prepare(NULL)==0&&town_ui_noise_texture==0);
    assert(dnd_draw_release(canvas)==0&&dnd_draw_release(atlas)==0&&dnd_draw_live()==0);
    free(v);free(package);puts("UI grain: actual SDK allocation/replacement/disable, ordered scope flushes, untextured fill selection, atlas isolation and frame guards passed");
}
