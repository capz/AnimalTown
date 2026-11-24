/* The inspection transport must observe GX without altering its calls. */
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "dnd_draw.h"

typedef float Mtx[3][4];
typedef struct { unsigned char r,g,b,a; } GXColor;
enum { GX_TEXMTX0=30,GX_TEXMTX1=33,GX_TEXCOORD0=0,GX_TEXCOORD1=1 };
static unsigned matrix_calls,tev_calls,indirect_calls,texgen_calls,order_calls,coord_calls,begin_calls;
static unsigned last_slot,last_type,last_tev,last_indirect,last_texgens,last_order[4],last_coord[4],last_begin[3];
static Mtx forwarded_matrix;
static GXColor forwarded_kcolor;
static unsigned kcolor_calls,last_kcolor_slot;
static unsigned blend_calls,depth_calls,last_blend[4],last_depth[3];
static char reported[512];
static unsigned reports;
static void GX_LoadTexMtxImm(Mtx matrix,unsigned slot,unsigned type){
    matrix_calls++;memcpy(forwarded_matrix,matrix,sizeof forwarded_matrix);last_slot=slot;last_type=type;
}
static void GX_SetNumTevStages(unsigned count){tev_calls++;last_tev=count;}
static void GX_SetNumIndStages(unsigned count){indirect_calls++;last_indirect=count;}
static void GX_SetNumTexGens(unsigned count){texgen_calls++;last_texgens=count;}
static void GX_SetTevOrder(unsigned stage,unsigned coord,unsigned map,unsigned color){
    order_calls++;last_order[0]=stage;last_order[1]=coord;last_order[2]=map;last_order[3]=color;
}
static void record_texcoord(unsigned coord,unsigned type,unsigned source,unsigned matrix){
    coord_calls++;last_coord[0]=coord;last_coord[1]=type;last_coord[2]=source;last_coord[3]=matrix;
}
/* Match libogc's macro form as well as the ordinary GX function wrappers. */
#define GX_SetTexCoordGen(coord,type,source,matrix) record_texcoord(coord,type,source,matrix)
static void GX_Begin(unsigned primitive,unsigned format,unsigned count){
    begin_calls++;last_begin[0]=primitive;last_begin[1]=format;last_begin[2]=count;
}
static void GX_SetTevKColor(unsigned slot,GXColor color){kcolor_calls++;last_kcolor_slot=slot;forwarded_kcolor=color;}
static void GX_SetBlendMode(unsigned type,unsigned source,unsigned destination,unsigned operation){
    blend_calls++;last_blend[0]=type;last_blend[1]=source;last_blend[2]=destination;last_blend[3]=operation;
}
static void GX_SetZMode(unsigned enabled,unsigned function,unsigned write){
    depth_calls++;last_depth[0]=enabled;last_depth[1]=function;last_depth[2]=write;
}
void dnd_console_write_line(const char *line){
    assert(strlen(line)<sizeof reported);strcpy(reported,line);reports++;
}
#include "../tools/native-water-probe.inc"

static void bind(const DndDrawTexture *texture,const DndDrawMaterial *material,Mtx first,Mtx second){
    town_water_select(texture,material);
    GX_LoadTexMtxImm(first,GX_TEXMTX0,1);GX_LoadTexMtxImm(second,GX_TEXMTX1,1);
    GX_SetNumTevStages(4);GX_SetNumIndStages(1);GX_SetNumTexGens(2);
    GX_SetTevOrder(1,0,0,4);GX_SetTevOrder(2,1,0,4);
    GX_SetTexCoordGen(0,1,4,GX_TEXMTX0);GX_SetTexCoordGen(1,1,5,GX_TEXMTX1);
    GX_SetTevKColor(0,(GXColor){196,228,222,255});GX_SetTevKColor(1,(GXColor){229,245,237,255});
    GX_SetBlendMode(material->flags&1?1:0,4,5,15);GX_SetZMode(1,3,material->flags&1?0:1);
}
static void begin(void){GX_Begin(144,0,6);}

int main(void){
    DndDrawEffect effect={.count=4,.indirect_strength=76};
    DndDrawTexture texture={.width=64,.height=64};
    DndDrawMaterial material={.flags=1,.effect=&effect,.secondary=&texture};
    Mtx primary={{1.35f,0,0,.2f},{0,1.35f,0,.3f},{0,0,1,0}};
    Mtx secondary={{1,0,0,.4f},{0,1,0,.5f},{0,0,1,0}};
    /* Disabled inspection still forwards every call, byte-for-byte. */
    bind(&texture,&material,primary,secondary);begin();
    assert(!reports&&begin_calls==1&&matrix_calls==2);
    assert(!memcmp(forwarded_matrix,secondary,sizeof secondary)&&last_slot==33&&last_type==1);
    assert(tev_calls==1&&indirect_calls==1&&texgen_calls==1&&order_calls==2&&coord_calls==2);
    assert(last_tev==4&&last_indirect==1&&last_texgens==2);
    assert(last_order[0]==2&&last_order[1]==1&&last_order[2]==0&&last_order[3]==4);
    assert(last_coord[0]==1&&last_coord[1]==1&&last_coord[2]==5&&last_coord[3]==33);
    assert(last_begin[0]==144&&last_begin[1]==0&&last_begin[2]==6);
    assert(kcolor_calls==2&&last_kcolor_slot==1&&forwarded_kcolor.r==229&&forwarded_kcolor.g==245&&forwarded_kcolor.b==237&&forwarded_kcolor.a==255);
    assert(blend_calls==1&&depth_calls==1&&last_blend[0]==1&&last_blend[1]==4&&last_blend[2]==5&&last_blend[3]==15);
    assert(last_depth[0]==1&&last_depth[1]==3&&last_depth[2]==0);

    assert(animaltown_water_probe_enable(1)==1);bind(&texture,&material,primary,secondary);
    assert(!reports);begin();assert(reports==1);
    assert(!strcmp(reported,"ANIMALTOWN WATER DRAW scene=1 frame=1 tev=4 indirect=1 strength=76 tex0=64x64 tex1=64x64 uv0=1350000,0,0,1350000,200000,300000 uv1=1000000,0,0,1000000,400000,500000 layers=0:0,1:0 texgens=2 matrices=30,33 k0=c4e4deff k1=e5f5edff blend=1:4:5 depth=1:3:0 layerstages=1,2"));
    /* More meshes, then 29 frames of frozen matrices, do not flood the log. */
    for(int mesh=0;mesh<20;mesh++)begin();
    for(int frame=0;frame<29;frame++){animaltown_water_probe_enable(1);town_water_select(&texture,&material);begin();}
    assert(reports==1);
    animaltown_water_probe_enable(1);begin();assert(reports==1); /* No current-frame material yet. */
    town_water_select(&texture,&material);begin();assert(reports==2&&strstr(reported,"frame=31 "));
    assert(strstr(reported,"uv0=1350000,0,0,1350000,200000,300000"));
    assert(strstr(reported,"layers=0:0,1:0")&&strstr(reported,"layerstages=1,2"));

    /* Report the new native matrix and appended tint stage on the next scene. */
    animaltown_water_probe_enable(4);material.flags=0;primary[0][1]=.125f;primary[1][0]=-.25f;primary[0][3]=.7f;
    bind(&texture,&material,primary,secondary);GX_SetNumTevStages(5);begin();
    assert(reports==3&&strstr(reported,"scene=4 frame=1 tev=5 "));
    assert(strstr(reported,"uv0=1350000,125000,-250000,1350000,700000,300000"));
    assert(town_water_fixed(NAN)==-2147483647&&town_water_fixed(INFINITY)==-2147483647);

    /* Observe the actual final K-color write, including a footer regression. */
    animaltown_water_probe_enable(3);material.flags=1;bind(&texture,&material,primary,secondary);
    GX_SetTevKColor(1,(GXColor){0,0,0,255});GX_SetTevKColor(2,(GXColor){4,5,6,7});begin();
    assert(reports==4&&strstr(reported,"k0=c4e4deff k1=000000ff"));
    assert(last_kcolor_slot==2&&forwarded_kcolor.r==4&&forwarded_kcolor.a==7);

    /* Actual shallow-water state reaches GX unchanged and is visible in the
     * trace, including depth testing without writes and source-alpha blend. */
    animaltown_water_probe_enable(2);bind(&texture,&material,primary,secondary);
    GX_SetBlendMode(1,4,5,15);GX_SetZMode(1,3,0);begin();
    assert(reports==5&&strstr(reported,"blend=1:4:5 depth=1:3:0"));
    assert(last_blend[0]==1&&last_blend[1]==4&&last_blend[2]==5&&last_blend[3]==15);
    assert(last_depth[0]==1&&last_depth[1]==3&&last_depth[2]==0);
    animaltown_water_probe_enable(1);town_water_select(&texture,&material);
    GX_SetTevOrder(1,0,0,4);GX_SetTevOrder(2,255,255,4);GX_SetTevOrder(3,1,0,4);begin();
    assert(reports==6&&strstr(reported,"layers=0:0,1:0")&&strstr(reported,"layerstages=1,3"));
    animaltown_water_probe_enable(3);town_water_select(&texture,&material);begin();
    assert(reports==7&&strstr(reported,"layers=0:0,1:0")&&strstr(reported,"layerstages=1,3"));

    /* Opaque ocean is submitted earlier even in a river view. It must not
     * consume the cadence that belongs to the late translucent river. */
    animaltown_water_probe_enable(1);material.flags=0;bind(&texture,&material,primary,secondary);begin();
    assert(reports==7);
    material.flags=1;bind(&texture,&material,primary,secondary);begin();
    assert(reports==8&&strstr(reported,"scene=1 frame=1")&&strstr(reported,"blend=1:4:5 depth=1:3:0"));

    /* A missing effect and inspection exit cannot produce false draw proof. */
    animaltown_water_probe_enable(2);material.effect=NULL;town_water_select(&texture,&material);begin();assert(reports==8);
    animaltown_water_probe_enable(0);material.effect=&effect;bind(&texture,&material,primary,secondary);begin();assert(reports==8);
    puts("Water probe: exact GX forwarding, actual-draw gating, matrix order, frozen values, final foam constants, bounded cadence and scene reset passed");
    return 0;
}
