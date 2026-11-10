#include "dnd_model_internal.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static int active,enabled=1,allow_memory=1,mesh_alive=1;
static uint16_t mesh_indices[]={2,1,0,3,4,5};
static unsigned char model_indices[66000*4];
static DndModel model;static DndMesh model_mesh;
bool dnd_device_active(void){return active!=0;}
int town_texture_feature(int bit){assert(bit==32);return enabled;}
int town_texture_memory_available(unsigned size){assert(size<=128*1024);return allow_memory;}
int town_geometry_mesh_source(int token,const void **indices,unsigned *count,unsigned *vertices,int *width){if(!mesh_alive||token<1||token>128)return 0;*indices=token==2?NULL:mesh_indices;*count=6;*vertices=6;*width=16;return 1;}
DndModel *dnd_model_find(int token,bool closed){(void)closed;return token==7?&model:NULL;}
#include "native-geometry-lists.h"
#define LIST_BUDGET (128u*1024u)
static unsigned list_attributes(unsigned mask){unsigned n=1;for(int i=0;i<4;i++)n+=(mask>>i)&1u;return n;}
static TownGeometryList build(int token,unsigned mask){
 TownGeometryList list;town_geometry_mesh_owner(token);assert(!town_geometry_mesh_list(mask,6,&list));active=1;assert(!town_geometry_pump());active=0;
 for(int n=0;n<100&&town_geometry_pump();n++)assert(n<99);
 assert(town_geometry_mesh_list(mask,6,&list));return list;
}
static void decode(TownGeometryList list,unsigned mask,const uint16_t *indices){
 assert(list.bytes%32==0&&list.primitives==1&&list.data[0]==0x90&&list.data[1]==0&&list.data[2]==6);unsigned at=3;
 for(unsigned i=0;i<6;i++)for(unsigned a=0;a<list_attributes(mask);a++){unsigned got=(unsigned)list.data[at]<<8|list.data[at+1];assert(got==(indices?indices[i]:i));at+=2;}
 while(at<list.bytes)assert(list.data[at++]==0);
}
int main(void){
 town_geometry_begin(1);town_geometry_scope(1);
 for(unsigned mask=0;mask<16;mask++){TownGeometryList list=build(1,mask);decode(list,mask,mesh_indices);}
 decode(build(2,0),0,NULL);assert(town_geometry_stats(3)==17);
 town_geometry_forget(0,1);TownGeometryList forgotten;town_geometry_mesh_owner(1);assert(!town_geometry_mesh_list(0,6,&forgotten));
 TownGeometryList list;town_geometry_mesh_owner(3);assert(!town_geometry_mesh_list(0,6,&list));mesh_alive=0;assert(town_geometry_pump());while(town_geometry_pump()){}mesh_alive=1;
 allow_memory=0;town_geometry_mesh_owner(4);assert(!town_geometry_mesh_list(0,6,&list));assert(town_geometry_pump());assert(!town_geometry_mesh_list(0,6,&list));allow_memory=1;
 town_geometry_clear();town_geometry_scope(1);
 for(int token=1;token<=80;token++){town_geometry_begin((unsigned)token+1);town_geometry_scope(1);decode(build(token,0),0,token==2?NULL:mesh_indices);assert((unsigned)town_geometry_stats(0)<=LIST_BUDGET);}
 memset(&model,0,sizeof model);model.ready=true;model.hash=99;model.meshes=1;model.mesh=&model_mesh;model_mesh.skin=-1;model_mesh.vertices=6;model_mesh.indices=6;model_mesh.index=model_indices;
 for(unsigned i=0;i<66000;i++){unsigned n=i%6;model_indices[i*4+3]=(unsigned char)n;}
 town_geometry_model_owner(7,0,99,1);assert(!town_geometry_model_list(5,6,&list));while(town_geometry_pump()){}assert(town_geometry_model_list(5,6,&list));decode(list,5,NULL);
 model.hash=100;town_geometry_model_owner(7,0,100,1);assert(!town_geometry_model_list(5,6,&list));while(town_geometry_pump()){}assert(town_geometry_model_list(5,6,&list));
 model_mesh.indices=66000;town_geometry_model_owner(7,0,100,1);assert(!town_geometry_model_list(0,66000,&list));assert(town_geometry_pump());assert((unsigned)town_geometry_stats(0)<=LIST_BUDGET);
 town_geometry_forget(1,7);enabled=0;assert(!town_geometry_model_list(5,6,&list));town_geometry_clear();assert(!town_geometry_stats(0));
 puts("GX geometry lists: native/big-endian/sequential indices, every attribute layout, padding, generation/disposal, LRU bound, exhausted budget and active-frame guards passed");return 0;
}
