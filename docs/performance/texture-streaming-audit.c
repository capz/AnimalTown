/* Read-only asset inventory using the pinned production loader/evaluator.
 * Run from the repository root with an extracted release model directory.
 * Pixel/package payload sizes are exact; host-sized metadata is not a
 * GameCube memory reading. No renderer, card writes or timing claims. */
#include "dnd_model_internal.h"
#include <assert.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
bool dnd_device_active(void){return false;}
int dnd_fs_close(int handle){(void)handle;return 0;}
static unsigned charge(unsigned n){return (n+63u)&~31u;}
int main(int argc,char **argv){
 assert(argc==2);
 const char *names[]={"fox","bunny","giraffe","cat","hamster","spaniel","cottage-exterior-tex128","cottage-door-tex128","cottage-floor-tex128","cottage-north-tex128","cottage-east-tex128","cottage-south-tex128","cottage-west-tex128"};
 unsigned totalPackage=0,totalImages=0,totalModel=0,totalInstance=0,totalSkinned=0;int first=0,last=0;
 printf("{\"method\":\"Pinned production loader and evaluator on a 64-bit host; byte payloads exact, metadata/instance accounting uses host pointer sizes; no GPU timing measurement\",\"models\":[");
 for(unsigned i=0;i<sizeof(names)/sizeof(*names);i++){
  char path[256];snprintf(path,sizeof(path),"%s/%s.dndmodel",argv[1],names[i]);FILE *f=fopen(path,"rb");assert(f&&!fseek(f,0,SEEK_END));long n=ftell(f);assert(n>0&&!fseek(f,0,SEEK_SET));void *p=malloc((size_t)n);assert(p&&fread(p,1,(size_t)n,f)==(size_t)n&&!fclose(f));
  int token=dnd_model_begin_data(p,(unsigned)n,3*1024*1024);free(p);assert(token);if(!i)first=token;last=token;DndModel *m=dnd_model_find(token,false);assert(m&&m->ready);
  int instance=dnd_model_instance(token,512*1024);assert(instance);DndInstance *in=dnd_instance_find(instance);
  unsigned images=0,textureCharges=0,skinned=0,triangles=0,vertices=0;
  for(unsigned t=0;t<m->textures;t++){images+=m->texture[t].size;textureCharges+=charge(m->texture[t].size);}
  for(unsigned j=0;j<m->meshes;j++){DndMesh *mesh=m->mesh+j;vertices+=mesh->vertices;triangles+=mesh->indices/3;if(mesh->skin>=0)skinned+=mesh->vertices*24;}
  totalPackage+=m->length;totalImages+=images;totalModel+=m->bytes;totalInstance+=in->bytes;totalSkinned+=skinned;
  printf("%s{\"name\":\"%s\",\"package_bytes\":%u,\"embedded_texture_bytes\":%u,\"non_texture_package_bytes\":%u,\"budgeted_model_bytes_host\":%u,\"model_metadata_charge_host\":%u,\"instance_bytes_host\":%u,\"skinned_vertex_bytes\":%u,\"nodes\":%u,\"meshes\":%u,\"vertices\":%u,\"triangles\":%u,\"textures\":[",i?",":"",names[i],m->length,images,m->length-images,m->bytes,m->bytes-charge(m->length)-textureCharges,in->bytes,skinned,m->nodes,m->meshes,vertices,triangles);
  for(unsigned t=0;t<m->textures;t++){DndTexture *tx=m->texture+t;printf("%s{\"width\":%u,\"height\":%u,\"levels\":%u,\"bytes\":%u,\"fnv\":%u}",t?",":"",tx->width,tx->height,tx->levels,tx->size,dnd_model_hash(tx->data,tx->size));}printf("]}");
 }
 int player=dnd_model_instance(first,512*1024);assert(player);DndInstance *in=dnd_instance_find(player);unsigned playerSkinned=0;for(unsigned j=0;j<in->model->meshes;j++)if(in->model->mesh[j].skin>=0)playerSkinned+=in->model->mesh[j].vertices*24;
 printf("],\"totals\":{\"package_bytes\":%u,\"embedded_texture_bytes\":%u,\"shared_texture_cache_charged_bytes\":%u,\"budgeted_model_bytes_host\":%u,\"instance_bytes_host_one_per_model\":%u,\"additional_player_instance_bytes_host\":%u,\"skinned_vertex_bytes_one_per_species\":%u,\"additional_player_skinned_bytes\":%u}}\n",totalPackage,totalImages,(unsigned)dnd_model_stat(last,6),totalModel,totalInstance,in->bytes,totalSkinned,playerSkinned);
 return 0;
}
