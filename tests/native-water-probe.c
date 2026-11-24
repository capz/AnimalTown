/* The inspection transport must observe GX without altering its calls. */
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include "dnd_draw.h"

typedef float Mtx[3][4];
typedef struct { unsigned char r,g,b,a; } GXColor;
typedef struct { unsigned width,height,format; } GXTexObj;
enum { GX_TEXMTX0=30,GX_TEXMTX1=33,GX_TEXCOORD0=0,GX_TEXCOORD1=1 };
static unsigned matrix_calls,tev_calls,indirect_calls,texgen_calls,order_calls,coord_calls,begin_calls;
static unsigned last_slot,last_type,last_tev,last_indirect,last_texgens,last_order[4],last_coord[4],last_begin[3];
static Mtx forwarded_matrix;
static GXColor forwarded_kcolor;
static unsigned kcolor_calls,last_kcolor_slot;
static unsigned blend_calls,depth_calls,last_blend[4],last_depth[3];
static char reported[1024],previous_report[1024];
static unsigned reports;
static int optics_stats[21];
static float optics_settings[12];
static unsigned indirect_order_calls,indirect_order[3],texture_calls,texture_map,coord2_calls,coord2_normalize,coord2_post;
static GXTexObj *forwarded_texture;
int animaltown_water_stats(int kind){assert(kind>=0&&kind<21);return optics_stats[kind];}
float animaltown_water_setting(int index){assert(index>=0&&index<12);return optics_settings[index];}
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
static void GX_SetTexCoordGen2(unsigned coord,unsigned type,unsigned source,unsigned matrix,unsigned normalize,unsigned post){
    record_texcoord(coord,type,source,matrix);coord2_calls++;coord2_normalize=normalize;coord2_post=post;
}
static void GX_SetIndTexOrder(unsigned stage,unsigned coord,unsigned map){
    indirect_order_calls++;indirect_order[0]=stage;indirect_order[1]=coord;indirect_order[2]=map;
}
static void GX_LoadTexObj(GXTexObj *texture,unsigned map){texture_calls++;forwarded_texture=texture;texture_map=map;}
static unsigned GX_GetTexObjWidth(GXTexObj *texture){return texture->width;}
static unsigned GX_GetTexObjHeight(GXTexObj *texture){return texture->height;}
static unsigned GX_GetTexObjFmt(GXTexObj *texture){return texture->format;}
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
    assert(strlen(line)<sizeof reported);strcpy(previous_report,reported);strcpy(reported,line);reports++;
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

    /* The new refraction pass is still an alpha material in the renderer,
     * but its final GX stage has already composed the captured background.
     * Report that actual opaque blend without mistaking the earlier sea for
     * the inland pass. The old no-optics records above remain byte-exact. */
    optics_stats[0]=169984;optics_stats[1]=1;optics_stats[2]=8;optics_stats[3]=7;optics_stats[4]=7;
    optics_stats[6]=12;optics_stats[7]=3;optics_stats[8]=320;optics_stats[9]=240;
    optics_stats[10]=1;optics_stats[11]=2;optics_stats[12]=640;optics_stats[13]=480;
    optics_stats[14]=optics_stats[15]=8;optics_stats[16]=7;optics_stats[17]=4;optics_stats[19]=255;optics_stats[20]=0xffffff;
    optics_settings[8]=.14f;optics_settings[9]=1.5f;optics_settings[10]=.18f;optics_settings[11]=.10f;
    animaltown_water_probe_enable(1);material.flags=0;bind(&texture,&material,primary,secondary);begin();assert(reports==8);
    material.flags=1;bind(&texture,&material,primary,secondary);
    Mtx wave0={{3.6f,0,0,.25f},{0,3.6f,0,.5f},{0,0,1,0}};
    Mtx wave1={{4.8f,0,0,.75f},{0,4.8f,0,.125f},{0,0,1,0}};
    GX_LoadTexMtxImm(wave0,36,1);GX_LoadTexMtxImm(wave1,39,1);
    GX_SetTexCoordGen(3,1,4,36);GX_SetTexCoordGen(4,1,4,39);
    GX_SetTexCoordGen(5,1,0,42);GX_SetTexCoordGen2(6,0,0,45,0,125);
    assert(coord2_calls==1&&coord2_normalize==0&&coord2_post==125);
    GXTexObj slopes={64,64,3},lookup={64,64,3},capture={320,240,4};
    GX_LoadTexObj(&slopes,3);GX_LoadTexObj(&lookup,4);GX_LoadTexObj(&capture,5);GX_LoadTexObj(&capture,6);
    assert(texture_calls==4&&forwarded_texture==&capture&&texture_map==6);
    GX_SetIndTexOrder(1,3,3);GX_SetIndTexOrder(2,4,3);
    assert(indirect_order_calls==2&&indirect_order[0]==2&&indirect_order[1]==4&&indirect_order[2]==3);
    GX_SetTevOrder(4,5,256,4);GX_SetTevOrder(5,5,4,4);GX_SetTevOrder(6,255,255,4);GX_SetTevOrder(7,6,5,4);
    GX_SetTevOrder(8,6,6,4);GX_SetTevOrder(9,255,255,4);GX_SetTevOrder(10,6,5,4);GX_SetTevOrder(11,255,255,4);
    GX_SetNumTevStages(12);GX_SetNumIndStages(3);GX_SetNumTexGens(7);
    GX_SetTevKColor(0,(GXColor){0x36,0x7f,0x91,0x2e});GX_SetTevKColor(1,(GXColor){0xd3,0xf2,0xec,0x1a});
    GX_SetBlendMode(0,1,0,15);GX_SetZMode(1,3,0);begin();assert(reports==10);
    assert(strstr(previous_report,"WATER DRAW scene=1 frame=1 tev=12 indirect=3")&&strstr(previous_report,"blend=0:1:0 depth=1:3:0"));
    assert(strstr(previous_report,"k0=367f912e k1=d3f2ec1a"));
    assert(strstr(reported,"WATER OPTICS scene=1 frame=1 ready=1 scope=2 part=8 captured=8 valid=1"));
    assert(strstr(reported,"size=320x240 source=640x480 bytes=169984 copies=8 draws=7,7,0 mask=7 base=4 failed=0 sun=255 environment=ffffff"));
    assert(strstr(reported,"textures=64x64:3,64x64:3,320x240:4,320x240:4 indirect=3:3,4:3 stages=5:256,5:4,255:255,6:5,6:6,255:255,6:5,255:255"));
    assert(strstr(reported,"generators=1:4:36,1:4:39,1:0:42,0:0:45"));
    assert(strstr(reported,"wave0=3600000,0,0,3600000,250000,500000 wave1=4800000,0,0,4800000,750000,125000 controls=140000,1500000,180000,100000"));

    /* A later cached draw keeps the captured GX bindings, while a failed
     * current-part capture is reported honestly and retains alpha blending. */
    for(int i=0;i<30;i++)animaltown_water_probe_enable(1);
    town_water_select(&texture,&material);optics_stats[1]=0;optics_stats[14]=9;optics_stats[16]=3;
    GX_SetNumTevStages(7);GX_SetNumTexGens(6);GX_SetBlendMode(1,4,5,15);begin();assert(reports==12);
    assert(strstr(previous_report,"blend=1:4:5 depth=1:3:0")&&strstr(reported,"part=9 captured=8 valid=0"));
    assert(strstr(reported,"mask=3 base=4")&&strstr(reported,"stages=5:256,5:4,255:255,255:255,255:255,255:255,255:255,255:255"));
    assert(strstr(reported,"wave0=3600000,0,0,3600000,250000,500000"));
    animaltown_water_probe_enable(2);optics_stats[10]=0;optics_stats[16]=0;optics_stats[18]=1;
    bind(&texture,&material,primary,secondary);begin();assert(reports==14);
    assert(strstr(reported,"ready=0 scope=2")&&strstr(reported,"mask=0 base=4 failed=1"));
    animaltown_water_probe_enable(0);begin();assert(reports==14);
    puts("Water probe: exact GX forwarding, actual-draw gating, matrix order, frozen values, final foam constants, bounded cadence and scene reset passed");
    puts("Water optics probe: actual texture/indirect/matrix bindings, opaque composition, capture-part age, cached state and allocation fallback passed");
    return 0;
}
