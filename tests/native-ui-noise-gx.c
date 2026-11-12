#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "dnd_draw.h"
typedef float Mtx[3][4];
#define GX_TEXMTX9 57
#define GX_MTX2x4 0
#define GX_TEXCOORD0 0
#define GX_TG_MTX2x4 1
#define GX_TG_POS 0
#define GX_TEVSTAGE0 0
#define GX_CA_ZERO 7
#define GX_CA_RASA 5
#define GX_TEV_ADD 0
#define GX_TB_ZERO 0
#define GX_CS_SCALE_1 0
#define GX_TRUE 1
#define GX_TEVPREV 0
static int enabled,calls;static Mtx matrix;static unsigned char pixel;
int animaltown_ui_noise_match(const void *p){return enabled&&p==&pixel;}
static void GX_LoadTexMtxImm(Mtx m,int slot,int type){assert(slot==GX_TEXMTX9&&type==GX_MTX2x4);memcpy(matrix,m,sizeof(matrix));calls++;}
static void GX_SetTexCoordGen(int coord,int type,int source,int matrixSlot){assert(coord==GX_TEXCOORD0&&type==GX_TG_MTX2x4&&source==GX_TG_POS&&matrixSlot==GX_TEXMTX9);calls++;}
static void GX_SetTevAlphaIn(int stage,int a,int b,int c,int d){assert(stage==0&&a==GX_CA_ZERO&&b==GX_CA_ZERO&&c==GX_CA_ZERO&&d==GX_CA_RASA);calls++;}
static void GX_SetTevAlphaOp(int stage,int op,int bias,int scale,int clamp,int dest){assert(stage==0&&op==0&&bias==0&&scale==0&&clamp==1&&dest==0);calls++;}
#include "../tools/native-ui-noise-gx.inc"
int main(void){
    float world[16]={1,0,0,0,0,1,0,0,0,0,1,0,23,31,0,1};DndDrawTexture texture={0};texture.data=&pixel;
    town_ui_noise_apply(&texture,world);assert(calls==0);
    enabled=1;town_ui_noise_apply(NULL,world);assert(calls==0);
    unsigned char atlas;texture.data=&atlas;town_ui_noise_apply(&texture,world);assert(calls==0);
    texture.data=&pixel;town_ui_noise_apply(&texture,world);assert(calls==4);
    assert(matrix[0][0]==1.f/32&&matrix[0][3]==23.f/32&&matrix[1][1]==1.f/32&&matrix[1][3]==31.f/32);
    float u=matrix[0][0]*9+matrix[0][3],v=matrix[1][1]*1+matrix[1][3];assert(u==1&&v==1);
    enabled=0;town_ui_noise_apply(&texture,world);assert(calls==4);
    puts("UI grain GX: viewport-position UV period/translation, atlas/scope isolation and raster-alpha preservation passed");
}
