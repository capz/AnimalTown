/* Read-only inspection of the commands sent to GX by archived/direct water.
 * Include before the shared render/TEV backends. Every wrapper forwards the
 * original call unchanged; no material, texture, matrix or timing is changed.
 * The managed Enable call advances once per outdoor frame, and emission waits
 * for a real primitive so a skipped draw cannot masquerade as rendered water. */
#include <stdio.h>

/* Queried only when inspection emits an actual inland draw. These functions
 * report resource/capture ownership; the bindings below still come from GX. */
extern int animaltown_water_stats(int kind);
extern float animaltown_water_setting(int index);

static int town_water_scene;
static unsigned town_water_frame,town_water_next;
static unsigned town_water_tev,town_water_indirect,town_water_texgens;
static unsigned town_water_coord[2],town_water_map[2],town_water_matrix[2],town_water_layer_stage[2];
static unsigned town_water_stage_coord[16]={255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255};
static unsigned town_water_stage_map[16]={255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255};
static unsigned town_water_kcolor[2];
static unsigned town_water_blend[3],town_water_depth[3];
static float town_water_uv[2][6];
static float town_water_extra_uv[10][6];
static unsigned town_water_gen_matrix[8],town_water_gen_type[8],town_water_gen_source[8];
static unsigned town_water_ind_coord[4],town_water_ind_map[4];
static unsigned town_water_tex_width[8],town_water_tex_height[8],town_water_tex_format[8];
static const DndDrawTexture *town_water_primary,*town_water_secondary;
static unsigned town_water_strength,town_water_selected_flags;

int animaltown_water_probe_enable(int scene){
    if(scene!=town_water_scene){town_water_scene=scene;town_water_frame=0;town_water_next=1;}
    town_water_strength=0;town_water_primary=town_water_secondary=NULL;
    if(scene>0)town_water_frame++;
    return scene;
}
static void town_water_select(const DndDrawTexture *texture,const DndDrawMaterial *material){
    town_water_strength=material->effect?material->effect->indirect_strength:0;
    town_water_primary=texture;town_water_secondary=material->secondary;
    town_water_selected_flags=(unsigned)material->flags;
}
static void town_water_load_matrix(Mtx matrix,unsigned slot,unsigned type){
    GX_LoadTexMtxImm(matrix,slot,type);
    if(town_water_scene>0&&(slot==GX_TEXMTX0||slot==GX_TEXMTX1)){
        float *out=town_water_uv[slot==GX_TEXMTX1];
        out[0]=matrix[0][0];out[1]=matrix[0][1];out[2]=matrix[1][0];
        out[3]=matrix[1][1];out[4]=matrix[0][3];out[5]=matrix[1][3];
    }
    if(town_water_scene>0&&slot>=GX_TEXMTX0&&slot<GX_TEXMTX0+30&&(slot-GX_TEXMTX0)%3==0){
        float *out=town_water_extra_uv[(slot-GX_TEXMTX0)/3];
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
    if(coord<8){town_water_gen_matrix[coord]=matrix;town_water_gen_type[coord]=type;town_water_gen_source[coord]=source;}
}
static void town_water_set_coord2(unsigned coord,unsigned type,unsigned source,unsigned matrix,unsigned normalize,unsigned post){
    GX_SetTexCoordGen2(coord,type,source,matrix,normalize,post);
    if(coord==GX_TEXCOORD0||coord==GX_TEXCOORD1)town_water_matrix[coord==GX_TEXCOORD1]=matrix;
    if(coord<8){town_water_gen_matrix[coord]=matrix;town_water_gen_type[coord]=type;town_water_gen_source[coord]=source;}
}
static void town_water_set_ind_order(unsigned stage,unsigned coord,unsigned map){
    GX_SetIndTexOrder(stage,coord,map);
    if(stage<4){town_water_ind_coord[stage]=coord;town_water_ind_map[stage]=map;}
}
static void town_water_load_texture(GXTexObj *texture,unsigned map){
    GX_LoadTexObj(texture,map);
    if(town_water_scene>0&&map<8){
        town_water_tex_width[map]=GX_GetTexObjWidth(texture);
        town_water_tex_height[map]=GX_GetTexObjHeight(texture);
        town_water_tex_format[map]=GX_GetTexObjFmt(texture);
    }
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
static const float *town_water_slope_uv(unsigned coord){
    unsigned slot=town_water_gen_matrix[coord];
    static const float invalid[6]={NAN,NAN,NAN,NAN,NAN,NAN};
    return slot>=GX_TEXMTX0&&slot<GX_TEXMTX0+30&&(slot-GX_TEXMTX0)%3==0
        ?town_water_extra_uv[(slot-GX_TEXMTX0)/3]:invalid;
}
static void town_water_report_optics(void){
    const float *u=town_water_slope_uv(3),*v=town_water_slope_uv(4);
    int base=animaltown_water_stats(17);
    unsigned bindings[16];
    for(int i=0;i<8;i++){
        int stage=base+i;
        bindings[i*2]=stage>=0&&stage<(int)town_water_tev&&stage<16?town_water_stage_coord[stage]:255;
        bindings[i*2+1]=stage>=0&&stage<(int)town_water_tev&&stage<16?town_water_stage_map[stage]:255;
    }
    char line[1024];
    snprintf(line,sizeof line,
        "ANIMALTOWN WATER OPTICS scene=%d frame=%u ready=%d scope=%d part=%d captured=%d valid=%d "
        "size=%dx%d source=%dx%d bytes=%d copies=%d draws=%d,%d,%d mask=%d base=%d failed=%d sun=%d environment=%06x "
        "textures=%ux%u:%u,%ux%u:%u,%ux%u:%u,%ux%u:%u indirect=%u:%u,%u:%u stages=%u:%u,%u:%u,%u:%u,%u:%u,%u:%u,%u:%u,%u:%u,%u:%u "
        "generators=%u:%u:%u,%u:%u:%u,%u:%u:%u,%u:%u:%u "
        "wave0=%d,%d,%d,%d,%d,%d wave1=%d,%d,%d,%d,%d,%d controls=%d,%d,%d,%d",
        town_water_scene,town_water_frame,animaltown_water_stats(10),animaltown_water_stats(11),
        animaltown_water_stats(14),animaltown_water_stats(15),animaltown_water_stats(1),
        animaltown_water_stats(8),animaltown_water_stats(9),animaltown_water_stats(12),animaltown_water_stats(13),
        animaltown_water_stats(0),animaltown_water_stats(2),animaltown_water_stats(3),animaltown_water_stats(4),animaltown_water_stats(5),
        animaltown_water_stats(16),base,animaltown_water_stats(18),animaltown_water_stats(19),(unsigned)animaltown_water_stats(20),
        town_water_tex_width[3],town_water_tex_height[3],town_water_tex_format[3],
        town_water_tex_width[4],town_water_tex_height[4],town_water_tex_format[4],
        town_water_tex_width[5],town_water_tex_height[5],town_water_tex_format[5],
        town_water_tex_width[6],town_water_tex_height[6],town_water_tex_format[6],
        town_water_ind_coord[1],town_water_ind_map[1],town_water_ind_coord[2],town_water_ind_map[2],
        bindings[0],bindings[1],bindings[2],bindings[3],bindings[4],bindings[5],bindings[6],bindings[7],
        bindings[8],bindings[9],bindings[10],bindings[11],bindings[12],bindings[13],bindings[14],bindings[15],
        town_water_gen_type[3],town_water_gen_source[3],town_water_gen_matrix[3],
        town_water_gen_type[4],town_water_gen_source[4],town_water_gen_matrix[4],
        town_water_gen_type[5],town_water_gen_source[5],town_water_gen_matrix[5],
        town_water_gen_type[6],town_water_gen_source[6],town_water_gen_matrix[6],
        town_water_fixed(u[0]),town_water_fixed(u[1]),town_water_fixed(u[2]),town_water_fixed(u[3]),town_water_fixed(u[4]),town_water_fixed(u[5]),
        town_water_fixed(v[0]),town_water_fixed(v[1]),town_water_fixed(v[2]),town_water_fixed(v[3]),town_water_fixed(v[4]),town_water_fixed(v[5]),
        town_water_fixed(animaltown_water_setting(8)),town_water_fixed(animaltown_water_setting(9)),
        town_water_fixed(animaltown_water_setting(10)),town_water_fixed(animaltown_water_setting(11)));
    dnd_console_write_line(line);
}
static void town_water_begin(unsigned primitive,unsigned format,unsigned count){
    /* River inspection can also submit the external ocean first. Sampling
     * that draw would consume the frame's cadence before the late surface.
     * Scenes1..3 inspect inland alpha water;4..5 inspect the opaque coast. */
    int optics=town_water_scene>0&&town_water_scene<=3&&animaltown_water_stats(11)==2;
    unsigned expected_blend=town_water_scene<=3?1:0;
    if(optics&&animaltown_water_stats(1)&&(animaltown_water_stats(16)&4))expected_blend=0;
    bool matching_surface=town_water_scene>3||((town_water_selected_flags&3)==1);
    if(town_water_scene>0&&town_water_strength&&matching_surface&&town_water_blend[0]==expected_blend&&town_water_frame>=town_water_next){
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
        if(optics)town_water_report_optics();
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
#undef GX_SetTexCoordGen2
#undef GX_SetIndTexOrder
#undef GX_LoadTexObj
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
#define GX_SetTexCoordGen2 town_water_set_coord2
#define GX_SetIndTexOrder town_water_set_ind_order
#define GX_LoadTexObj town_water_load_texture
#define GX_SetTevKColor town_water_set_kcolor
#define GX_SetBlendMode town_water_set_blend
#define GX_SetZMode town_water_set_depth
#define GX_Begin town_water_begin
