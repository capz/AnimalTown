/* Read-only inspection of the commands sent to GX by archived/direct water.
 * Include before the shared render/TEV backends. Every wrapper forwards the
 * original call unchanged; no material, texture, matrix or timing is changed.
 * The managed Enable call advances once per outdoor frame, and emission waits
 * for a real primitive so a skipped draw cannot masquerade as rendered water. */
#include <stdio.h>

static int town_water_scene;
static unsigned town_water_frame,town_water_next;
static unsigned town_water_tev,town_water_indirect,town_water_texgens;
static unsigned town_water_coord[2],town_water_map[2],town_water_matrix[2],town_water_layer_stage[2];
static unsigned town_water_stage_coord[16]={255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255};
static unsigned town_water_stage_map[16]={255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255};
static unsigned town_water_kcolor[2];
static unsigned town_water_blend[3],town_water_depth[3];
static float town_water_uv[2][6];
static const DndDrawTexture *town_water_primary,*town_water_secondary;
static unsigned town_water_strength;

int animaltown_water_probe_enable(int scene){
    if(scene!=town_water_scene){town_water_scene=scene;town_water_frame=0;town_water_next=1;}
    town_water_strength=0;town_water_primary=town_water_secondary=NULL;
    if(scene>0)town_water_frame++;
    return scene;
}
static void town_water_select(const DndDrawTexture *texture,const DndDrawMaterial *material){
    town_water_strength=material->effect?material->effect->indirect_strength:0;
    town_water_primary=texture;town_water_secondary=material->secondary;
}
static void town_water_load_matrix(Mtx matrix,unsigned slot,unsigned type){
    GX_LoadTexMtxImm(matrix,slot,type);
    if(town_water_scene>0&&(slot==GX_TEXMTX0||slot==GX_TEXMTX1)){
        float *out=town_water_uv[slot==GX_TEXMTX1];
        out[0]=matrix[0][0];out[1]=matrix[0][1];out[2]=matrix[1][0];
        out[3]=matrix[1][1];out[4]=matrix[0][3];out[5]=matrix[1][3];
    }
}
static void town_water_set_tev(unsigned count){GX_SetNumTevStages(count);town_water_tev=count;}
static void town_water_set_indirect(unsigned count){GX_SetNumIndStages(count);town_water_indirect=count;}
static void town_water_set_texgens(unsigned count){GX_SetNumTexGens(count);town_water_texgens=count;}
static void town_water_set_order(unsigned stage,unsigned coord,unsigned map,unsigned color){
    GX_SetTevOrder(stage,coord,map,color);
    /* Retain actual GX state across cached draws, which do not rebind TEV.
     * The active stage count at GX_Begin excludes unused older stages. */
    if(stage<16){town_water_stage_coord[stage]=coord;town_water_stage_map[stage]=map;}
}
static void town_water_set_coord(unsigned coord,unsigned type,unsigned source,unsigned matrix){
    GX_SetTexCoordGen(coord,type,source,matrix);
    if(coord==GX_TEXCOORD0||coord==GX_TEXCOORD1)town_water_matrix[coord==GX_TEXCOORD1]=matrix;
}
static void town_water_set_kcolor(unsigned slot,GXColor color){
    GX_SetTevKColor(slot,color);
    if(slot<2)town_water_kcolor[slot]=(unsigned)color.r<<24|(unsigned)color.g<<16|(unsigned)color.b<<8|color.a;
}
static void town_water_set_blend(unsigned type,unsigned source,unsigned destination,unsigned operation){
    GX_SetBlendMode(type,source,destination,operation);
    town_water_blend[0]=type;town_water_blend[1]=source;town_water_blend[2]=destination;
}
static void town_water_set_depth(unsigned enabled,unsigned function,unsigned write){
    GX_SetZMode(enabled,function,write);
    town_water_depth[0]=enabled;town_water_depth[1]=function;town_water_depth[2]=write;
}
static int town_water_fixed(float value){
    /* Invalid matrices remain visible without undefined float-to-int casts. */
    return isfinite(value)&&fabsf(value)<2000?(int)lroundf(value*1000000.f):-2147483647;
}
static void town_water_begin(unsigned primitive,unsigned format,unsigned count){
    /* River inspection can also submit the external ocean first. Sampling
     * that draw would consume the frame's cadence before the late surface.
     * Scenes1..3 inspect inland alpha water;4..5 inspect the opaque coast. */
    unsigned expected_blend=town_water_scene<=3?1:0;
    if(town_water_scene>0&&town_water_strength&&town_water_blend[0]==expected_blend&&town_water_frame>=town_water_next){
        for(int i=0;i<2;i++)town_water_coord[i]=town_water_map[i]=town_water_layer_stage[i]=255;
        for(unsigned stage=1;stage<town_water_tev&&stage<16;stage++){
            unsigned coord=town_water_stage_coord[stage],map=town_water_stage_map[stage];
            if(coord<2&&map==0&&town_water_layer_stage[coord]==255){
                town_water_coord[coord]=coord;town_water_map[coord]=map;town_water_layer_stage[coord]=stage;
            }
        }
        const DndDrawTexture *a=town_water_primary,*b=town_water_secondary;
        const float *u=town_water_uv[0],*v=town_water_uv[1];
        char line[512];
        snprintf(line,sizeof line,
            "ANIMALTOWN WATER DRAW scene=%d frame=%u tev=%u indirect=%u strength=%u tex0=%dx%d tex1=%dx%d "
            "uv0=%d,%d,%d,%d,%d,%d uv1=%d,%d,%d,%d,%d,%d layers=%u:%u,%u:%u texgens=%u matrices=%u,%u k0=%08x k1=%08x blend=%u:%u:%u depth=%u:%u:%u layerstages=%u,%u",
            town_water_scene,town_water_frame,town_water_tev,town_water_indirect,town_water_strength,
            a?a->width:0,a?a->height:0,b?b->width:0,b?b->height:0,
            town_water_fixed(u[0]),town_water_fixed(u[1]),town_water_fixed(u[2]),town_water_fixed(u[3]),town_water_fixed(u[4]),town_water_fixed(u[5]),
            town_water_fixed(v[0]),town_water_fixed(v[1]),town_water_fixed(v[2]),town_water_fixed(v[3]),town_water_fixed(v[4]),town_water_fixed(v[5]),
            town_water_coord[0],town_water_map[0],town_water_coord[1],town_water_map[1],town_water_texgens,town_water_matrix[0],town_water_matrix[1],town_water_kcolor[0],town_water_kcolor[1],
            town_water_blend[0],town_water_blend[1],town_water_blend[2],town_water_depth[0],town_water_depth[1],town_water_depth[2],town_water_layer_stage[0],town_water_layer_stage[1]);
        dnd_console_write_line(line);
        town_water_next=town_water_frame+30;
    }
    GX_Begin(primitive,format,count);
}
/* Some libogc releases expose these as function-like macros. Their original
 * expansion has already been used in the forwarding bodies above. */
#undef GX_LoadTexMtxImm
#undef GX_SetNumTevStages
#undef GX_SetNumIndStages
#undef GX_SetNumTexGens
#undef GX_SetTevOrder
#undef GX_SetTexCoordGen
#undef GX_SetTevKColor
#undef GX_SetBlendMode
#undef GX_SetZMode
#undef GX_Begin
#define GX_LoadTexMtxImm town_water_load_matrix
#define GX_SetNumTevStages town_water_set_tev
#define GX_SetNumIndStages town_water_set_indirect
#define GX_SetNumTexGens town_water_set_texgens
#define GX_SetTevOrder town_water_set_order
#define GX_SetTexCoordGen town_water_set_coord
#define GX_SetTevKColor town_water_set_kcolor
#define GX_SetBlendMode town_water_set_blend
#define GX_SetZMode town_water_set_depth
#define GX_Begin town_water_begin
