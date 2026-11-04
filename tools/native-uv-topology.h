#ifndef ANIMALTOWN_UV_TOPOLOGY_H
#define ANIMALTOWN_UV_TOPOLOGY_H
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

typedef struct {unsigned triangles,islands,edges;unsigned *island,*edge;} TownUvTopology;
typedef struct {uint32_t key[10];unsigned triangle,a,b;} TownUvEdge;
static uint32_t town_uv_u32(const unsigned char *p){return (uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];}
static unsigned town_uv_root(unsigned *parent,unsigned n){while(parent[n]!=n){parent[n]=parent[parent[n]];n=parent[n];}return n;}
static int town_uv_compare(const void *a,const void *b){return memcmp(((const TownUvEdge*)a)->key,((const TownUvEdge*)b)->key,40);}
static void town_uv_free(TownUvTopology *t){free(t->island);free(t->edge);memset(t,0,sizeof(*t));}
static void town_uv_vertex_key(const unsigned char *v,uint32_t key[5]){
  for(unsigned i=0;i<5;i++){uint32_t bits=town_uv_u32(v+(i<3?i*4:24+(i-3)*4));key[i]=(bits&0x7fffffff)?bits:0;}
}
/* Match geometric edge AND UV endpoints, including duplicated normal vertices.
 * Ordinary triangulation edges join islands; UV seams/open edges remain lines.
 * One mesh at a time bounds scratch memory; no frame-time topology work. */
static int town_uv_build(const unsigned char *vertices,unsigned vertex_count,const unsigned char *indices,unsigned index_count,TownUvTopology *out){
  if(!vertices||!indices||!out||index_count%3||index_count>75000||!vertex_count)return -1;
  memset(out,0,sizeof(*out));unsigned triangles=index_count/3;
  if(!triangles)return 0;
  TownUvEdge *edges=malloc((size_t)index_count*sizeof(*edges));unsigned *parent=malloc((size_t)triangles*sizeof(*parent));
  if(!edges||!parent){free(edges);free(parent);return -1;}
  out->island=malloc((size_t)triangles*sizeof(unsigned));out->edge=malloc((size_t)index_count*2*sizeof(unsigned));
  if(!out->island||!out->edge){free(edges);free(parent);town_uv_free(out);return -1;}
  for(unsigned t=0;t<triangles;t++){
    parent[t]=t;unsigned v[3];for(unsigned j=0;j<3;j++){v[j]=town_uv_u32(indices+(t*3+j)*4);if(v[j]>=vertex_count){free(edges);free(parent);town_uv_free(out);return -1;}}
    for(unsigned j=0;j<3;j++){
      TownUvEdge *e=&edges[t*3+j];e->triangle=t;e->a=v[j];e->b=v[(j+1)%3];uint32_t a[5],b[5];town_uv_vertex_key(vertices+e->a*80,a);town_uv_vertex_key(vertices+e->b*80,b);
      if(memcmp(a,b,20)>0){memcpy(e->key,b,20);memcpy(e->key+5,a,20);}else{memcpy(e->key,a,20);memcpy(e->key+5,b,20);}
    }
  }
  qsort(edges,index_count,sizeof(*edges),town_uv_compare);
  for(unsigned i=0;i<index_count;){unsigned end=i+1;while(end<index_count&&!memcmp(edges[i].key,edges[end].key,40))end++;
    if(end-i==1){out->edge[out->edges*2]=edges[i].a;out->edge[out->edges*2+1]=edges[i].b;out->edges++;}
    else for(unsigned j=i+1;j<end;j++){unsigned a=town_uv_root(parent,edges[i].triangle),b=town_uv_root(parent,edges[j].triangle);if(a!=b)parent[a>b?a:b]=a>b?b:a;}
    i=end;
  }
  out->triangles=triangles;for(unsigned t=0;t<triangles;t++){out->island[t]=town_uv_root(parent,t);if(out->island[t]==t)out->islands++;}
  free(edges);free(parent);return 0;
}
#endif
