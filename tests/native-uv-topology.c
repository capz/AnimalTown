#include <assert.h>
#include <stdio.h>
#include "../tools/native-uv-topology.h"
static void put32(unsigned char *p,uint32_t v){p[0]=v>>24;p[1]=v>>16;p[2]=v>>8;p[3]=v;}
static void putfloat(unsigned char *p,float f){uint32_t v;memcpy(&v,&f,4);put32(p,v);}
static void vertex(unsigned char *data,unsigned i,float x,float y,float u,float v){putfloat(data+i*80,x);putfloat(data+i*80+4,y);putfloat(data+i*80+24,u);putfloat(data+i*80+28,v);}
int main(void){
  unsigned char data[6*80]={0},index[6*4];unsigned ids[]={0,1,2,0,2,3};for(unsigned i=0;i<6;i++)put32(index+i*4,ids[i]);
  vertex(data,0,0,0,0,0);vertex(data,1,1,0,1,0);vertex(data,2,1,1,1,1);vertex(data,3,0,1,0,1);TownUvTopology t={0};
  assert(town_uv_build(data,4,index,6,&t)==0);assert(t.triangles==2&&t.islands==1&&t.edges==4&&t.island[0]==t.island[1]);town_uv_free(&t);
  /* A split normal vertex with identical position/UV is still continuous. */
  vertex(data,4,0,0,0,0);vertex(data,5,1,1,1,1);put32(index+12,4);put32(index+16,5);
  assert(town_uv_build(data,6,index,6,&t)==0);assert(t.islands==1&&t.edges==4);town_uv_free(&t);
  /* Same physical edge, different UV endpoints: two UV islands, six edges. */
  vertex(data,4,0,0,.25f,0);vertex(data,5,1,1,.75f,1);
  assert(town_uv_build(data,6,index,6,&t)==0);assert(t.islands==2&&t.edges==6&&t.island[0]!=t.island[1]);town_uv_free(&t);
  put32(index,99);assert(town_uv_build(data,6,index,6,&t)<0);assert(!t.edge&&!t.island);
  assert(town_uv_build(data,6,index,5,&t)<0);assert(town_uv_build(data,6,index,75003,&t)<0);
  puts("UV topology checks passed: islands, seam edges, normal splits, triangulation suppression and bounds");return 0;
}
