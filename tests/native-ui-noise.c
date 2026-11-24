#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include "draw_resources.c"
TownFrameCounters town_frame_counters;
static int pass,draws,seen[16];
static int streaming,stream_vertices,stream_draws;
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
    assert(v&&n>0&&prim==0x90&&world&&m);
    if(streaming){
        for(int i=0;i<n;i++){
            if(streaming==1)assert(v[i].x==(float)(stream_vertices+i)&&v[i].color==0x12345678);
            else assert(v[i].color==0x00ffaa88);
        }
        stream_vertices+=n;stream_draws++;return;
    }
    seen[draws++]=texture?animaltown_ui_noise_match(texture->data)?1:2:0;
}
static DndArray *array(int count,int size){DndArray *a=calloc(1,sizeof(*a)+(size_t)count*size);assert(a);a->length=count;a->element_size=size;return a;}
static void bounded_canvas(void){
    int baseline=dnd_draw_total_bytes(),large=dnd_canvas_create(4096);assert(large>0);
    int large_bytes=dnd_draw_bytes(large);assert(large_bytes==147456&&dnd_draw_release(large)==0);
    int canvas=dnd_canvas_create(1024),atlas=dnd_texture_allocate_native(8,4,1,1);assert(canvas&&atlas);
    int small_bytes=dnd_draw_bytes(canvas);assert(small_bytes==36864&&large_bytes-small_bytes==110592);
    DndArray *textures=array(128,4),*quads=array(128*24,4),*colors=array(128,4);
    int *ids=(int*)textures->data;uint32_t *tints=(uint32_t*)colors->data;float *points=(float*)quads->data;
    for(int i=0;i<128;i++){ids[i]=atlas;tints[i]=0x12345678;}
    streaming=1;stream_vertices=stream_draws=0;assert(dnd_canvas_begin(canvas,640,480)==0);
    /* Each real font-sized call is 128 quads. Several calls exceed both canvas
     * capacities; verify every vertex/color remains in painter order on flush. */
    for(int batch=0;batch<9;batch++){
        for(int vertex=0;vertex<768;vertex++)points[vertex*4]=(float)(batch*768+vertex);
        assert(dnd_canvas_sprites(canvas,textures,quads,colors,128)==0);
    }
    assert(dnd_canvas_end(canvas)==0&&stream_vertices==9*768&&stream_draws>1);
    streaming=2;stream_vertices=stream_draws=0;assert(dnd_canvas_begin(canvas,640,480)==0);
    for(int i=0;i<600;i++)assert(dnd_canvas_rectangle(canvas,(float)(i%640),0,1,1,0x00ffaa88)==0);
    assert(dnd_canvas_end(canvas)==0&&stream_vertices==600*6&&stream_draws>1);
    assert(dnd_canvas_begin(canvas,640,480)==0);
    int owned=dnd_draw_total_bytes();
    assert(dnd_canvas_vertices(canvas,atlas,quads,1026,0x00ffaa88)==-1&&dnd_draw_total_bytes()==owned);
    assert(dnd_canvas_rectangle(canvas,0,0,1,1,0x00ffaa88)==0&&dnd_canvas_end(canvas)==0);
    streaming=0;assert(dnd_draw_release(canvas)==0&&dnd_draw_release(atlas)==0&&dnd_draw_total_bytes()==baseline);
    free(textures);free(quads);free(colors);
    printf("UI canvas: native %d -> %d bytes; saved %d; bounded font batches/fills preserve vertices, order and failure cleanup\n",large_bytes,small_bytes,large_bytes-small_bytes);
}
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
    free(v);free(package);bounded_canvas();puts("UI grain: actual SDK allocation/replacement/disable, ordered scope flushes, untextured fill selection, atlas isolation and frame guards passed");
}
