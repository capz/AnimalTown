/* Run the production shadow/rim hooks against a recording GX transport, then
 * interpret their alpha commands with GX's independent signed-D/byte-A/B/C
 * rules. CPU coverage-cache checks alone cannot observe these GPU commands. */
#include <stdbool.h>
typedef unsigned char u8;
typedef unsigned short u16;
typedef float Mtx[3][4];
typedef struct { u8 r,g,b,a; } GXColor;
typedef struct { int unused; } GXTexObj;
typedef struct { int count,uniforms; } DndDrawEffect;
typedef struct { const DndDrawEffect *effect; } DndDrawMaterial;
typedef struct { float u,v; unsigned color; } DndDrawVertex;
enum {
    GX_FALSE=0,GX_TRUE=1,GX_TF_I8=1,GX_TF_IA4=2,GX_CLAMP=0,GX_LINEAR=1,GX_ANISO_1=0,
    GX_TEXMAP2=2,GX_TEXCOORD1=1,GX_TEXCOORD2=2,GX_TEXCOORD_NULL=255,
    GX_TEXMAP_NULL=255,GX_COLOR_NULL=255,GX_COLOR0A0=4,
    GX_TG_MTX2x4=1,GX_TG_MTX3x4=0,GX_TG_TEX0=4,GX_TG_POS=0,GX_TG_NRM=1,
    GX_IDENTITY=60,GX_TEXMTX8=54,GX_TEXMTX9=57,GX_MTX2x4=1,GX_MTX3x4=0,
    GX_DTTMTX0=64,GX_CH_RED=0,GX_CH_GREEN=1,GX_CH_BLUE=2,GX_TEV_SWAP1=1,GX_TEV_SWAP0=0,GX_TEV_ADD=0,GX_TB_ZERO=0,GX_CS_SCALE_1=0,
    GX_TEVPREV=0,GX_TEVREG2=3,GX_TEV_KASEL_1=0,GX_KCOLOR3=3,GX_KCOLOR2=2,GX_KCOLOR1=1,GX_TEV_KASEL_K2_A=30,GX_TEV_KCSEL_K0=12,GX_TEV_KCSEL_K1=13,GX_TEV_KCSEL_K2=14,
    GX_TEV_KCSEL_K3=15,GX_CC_CPREV=0,GX_CC_A2=7,GX_CC_TEXC=8,
    GX_CC_KONST=14,GX_CC_ZERO=15,GX_CA_APREV=0,GX_CA_A2=3,GX_CA_TEXA=4,
    GX_CA_RASA=5,GX_CA_KONST=6,GX_CA_ZERO=7
};
typedef struct { int input[4],destination; } AlphaCommand;
static AlphaCommand alpha_commands[16];
static int alpha_command_count;
static int shadow_page_side=SU_SIDE,shadow_page_missing;
static int texture_width,texture_height,texture_min,texture_mag;
static int texture_format,environment_test_rgb=0xffffff;
static GXColor konst_colors[4];
static GXColor shadow_tint;
static float shadow_uv_scale,shadow_uv_offset,shadow_uv_scale_z,shadow_uv_offset_z;
static void DCFlushRange(void *data,size_t bytes){(void)data;(void)bytes;}
static void GX_InitTexObj(GXTexObj *t,void *data,u16 w,u16 h,int format,int s,int v,int mip){
    (void)t;(void)data;(void)s;(void)v;(void)mip;texture_width=w;texture_height=h;texture_format=format;
}
static void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float low,float high,float bias,int clamp,int edge,int aniso){
    (void)t;(void)low;(void)high;(void)bias;(void)clamp;(void)edge;(void)aniso;texture_min=min;texture_mag=mag;
}
static void GX_LoadTexObj(GXTexObj *t,int map){(void)t;assert(map==GX_TEXMAP2);}
static void GX_SetNumTevStages(u8 count){assert(count<=16);alpha_command_count=count;}
static void GX_SetNumTexGens(int count){assert(count==3);}
static void GX_SetTexCoordGen(int coord,int type,int source,int matrix){(void)coord;(void)type;(void)source;(void)matrix;}
static void GX_SetTexCoordGen2(int coord,int type,int source,int matrix,int normalize,int post){
    (void)coord;(void)type;(void)source;(void)matrix;(void)normalize;(void)post;
}
static void GX_SetTexCoordScaleManually(int coord,int manual,int s,int t){(void)coord;(void)manual;(void)s;(void)t;}
static void GX_SetTexCoordBias(int coord,int s,int t){(void)coord;(void)s;(void)t;}
static void GX_LoadTexMtxImm(Mtx matrix,int slot,int type){
    (void)type;if(slot==GX_TEXMTX8){shadow_uv_scale=matrix[0][0];shadow_uv_offset=matrix[0][3];shadow_uv_scale_z=matrix[1][1];shadow_uv_offset_z=matrix[1][3];}
}
static void GX_SetTevSwapModeTable(int t,int r,int g,int b,int a){(void)t;(void)r;(void)g;(void)b;(void)a;}
static void GX_SetTevDirect(u8 stage){assert(stage<16);}
static void GX_SetTevSwapMode(u8 stage,int raster,int tex){assert(stage<16);(void)raster;(void)tex;}
static void GX_SetTevOrder(u8 stage,int coord,int map,int raster){assert(stage<16);(void)coord;(void)map;(void)raster;}
static void GX_SetTevColorIn(u8 stage,int a,int b,int c,int d){assert(stage<16);(void)a;(void)b;(void)c;(void)d;}
static void GX_SetTevColorOp(u8 stage,int op,int bias,int scale,int clamp,int destination){
    assert(stage<16);(void)op;(void)bias;(void)scale;(void)clamp;(void)destination;
}
static void GX_SetTevKAlphaSel(u8 stage,int selection){assert(stage<16);(void)selection;}
static void GX_SetTevKColor(int slot,GXColor color){assert(slot>=0&&slot<4);konst_colors[slot]=color;if(slot==GX_KCOLOR3)shadow_tint=color;}
static void GX_SetTevKColorSel(u8 stage,int selection){assert(stage<16);(void)selection;}
static void GX_SetTevAlphaIn(u8 stage,int a,int b,int c,int d){
    assert(stage<16);AlphaCommand *out=&alpha_commands[stage];
    out->input[0]=a;out->input[1]=b;out->input[2]=c;out->input[3]=d;
}
static void GX_SetTevAlphaOp(u8 stage,int op,int bias,int scale,int clamp,int destination){
    assert(stage<16&&op==GX_TEV_ADD&&bias==GX_TB_ZERO&&scale==GX_CS_SCALE_1&&clamp==GX_TRUE);
    alpha_commands[stage].destination=destination;
}
int animaltown_environment_color(void){return environment_test_rgb;}
int animaltown_light_strength(void){return 255;}
int animaltown_shadows_mode(void){return 3;}
const uint8_t *animaltown_shadows_page(float x,float z,float *origin){
    static const uint8_t pixels[SU_TERRAIN_BYTES]={0};(void)x;(void)z;
    origin[0]=origin[1]=0;origin[2]=shadow_page_side==SU_SIDE?8:(SU_TERRAIN_WIDTH-2)/SU_TERRAIN_TEXELS_PER_METRE;
    origin[3]=(float)shadow_page_side;origin[4]=shadow_page_side==SU_SIDE?8:(SU_TERRAIN_HEIGHT-2)/SU_TERRAIN_TEXELS_PER_METRE;origin[5]=shadow_page_side==SU_SIDE?SU_SIDE:SU_TERRAIN_HEIGHT;return shadow_page_missing?NULL:pixels;
}
#include "../tools/native-shadow-union-gx.inc"
static void *batch_model,*batch_material;
static bool model_batch_vertex_ready;
#define DND_MODEL_GX_TEST
#include "../tools/native-rim.inc"
#undef DND_MODEL_GX_TEST

static int alpha_operand(int source,const int registers[4],int mask){
    if(source<=3)return registers[source];
    return source==GX_CA_TEXA?mask:source==GX_CA_ZERO?0:255;
}
static int footer_alpha(int first,int previous,int mask){
    int registers[4]={previous,0,0,0};
    for(int stage=first;stage<alpha_command_count;stage++){
        const AlphaCommand *command=&alpha_commands[stage];
        int a=alpha_operand(command->input[0],registers,mask)&255;
        int b=alpha_operand(command->input[1],registers,mask)&255;
        int c=alpha_operand(command->input[2],registers,mask)&255;
        int d=alpha_operand(command->input[3],registers,mask);
        int weight=c+(c>>7);
        int value=d+((a*(256-weight)+b*weight+128)>>8);
        registers[command->destination]=value<0?0:value>255?255:value;
    }
    return registers[0]&255;
}
static int sea_cutout(int alpha){return (alpha>=235)==(alpha>=35);}
static void verify_alpha_footer(int first){
    assert(footer_alpha(first,300,128)==44&&!sea_cutout(footer_alpha(first,300,128)));
    assert(footer_alpha(first,510,128)==254&&sea_cutout(footer_alpha(first,510,128)));
    assert(sea_cutout(34)&&!sea_cutout(35)&&!sea_cutout(234)&&sea_cutout(235));
    for(int wide=-1024;wide<=1023;wide++)for(int mask=0;mask<=255;mask+=51){
        int expected=(unsigned)wide&255,actual=footer_alpha(first,wide,mask);
        assert(actual==expected&&sea_cutout(actual)==sea_cutout(expected));
    }
}
static void native_shadow_combiner_checks(void){
    DndDrawEffect effect={.count=2};DndDrawMaterial material={.effect=&effect};
    const DndDrawVertex triangle[]={{0,0,255},{1,0,255},{0,1,255}};
    unsigned bindings=0;town_shadow_ground(triangle,NULL,3,&material,&bindings);
    assert(alpha_command_count==6&&bindings==1);verify_alpha_footer(2);
    assert(shadow_tint.r==35&&shadow_tint.g==49&&shadow_tint.b==66);
    assert(texture_format==GX_TF_IA4&&konst_colors[GX_KCOLOR2].a==SU_OPACITY);
    assert(konst_colors[GX_KCOLOR1].r==220&&konst_colors[GX_KCOLOR1].g==158&&konst_colors[GX_KCOLOR1].b==74);
    assert(texture_width==32&&texture_height==32&&texture_min==GX_LINEAR&&texture_mag==GX_LINEAR);
    assert(fabsf(shadow_uv_scale-4.f*30/(32*8))<.000001f&&shadow_uv_offset==1.f/32);
    shadow_page_side=SU_TERRAIN_WIDTH;town_shadow_ground(triangle,NULL,3,&material,&bindings);
    assert(texture_width==256&&texture_height==128);
    assert(fabsf(shadow_uv_scale-4.f*SU_TERRAIN_TEXELS_PER_METRE/256)<.000001f&&shadow_uv_offset==1.f/256);
    assert(fabsf(shadow_uv_scale_z-4.f/128)<.000001f&&shadow_uv_offset_z==1.f/128);
    shadow_page_side=SU_SIDE;shadow_page_missing=1;town_shadow_ground(triangle,NULL,3,&material,&bindings);
    assert(texture_width==32&&texture_height==32&&sizeof(town_shadow_empty)==1024);
    shadow_page_missing=0;bindings=1;
    assert(animaltown_rim_enable(100));
    const float normal[]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    town_rim_apply(normal,&effect,false,&bindings);
    assert(alpha_command_count==3&&bindings==2);verify_alpha_footer(2);
    // A water effect keeps its own constants; tint takes the first free slot.
    environment_test_rgb=0xff9860;effect.uniforms=2;
    GXColor original=konst_colors[1];town_environment_apply(&effect,2);
    assert(alpha_command_count==3&&konst_colors[2].r==255&&konst_colors[2].g==152&&konst_colors[2].b==96);
    assert(!memcmp(&original,&konst_colors[1],sizeof(original)));verify_alpha_footer(2);
    environment_test_rgb=0xffffff;
    puts("Shadow/rim GX: signed-wide alpha, low-byte wrap and exact sea cutout survive appended stages");
}
