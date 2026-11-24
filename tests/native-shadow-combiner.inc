/* Run the production shadow/rim hooks against a recording GX transport, then
 * interpret their alpha commands with GX's independent signed-D/byte-A/B/C
 * rules. CPU coverage-cache checks alone cannot observe these GPU commands. */
#include <stdbool.h>
typedef unsigned char u8;
typedef unsigned short u16;
typedef float Mtx[3][4];
typedef struct { u8 r,g,b,a; } GXColor;
typedef struct { int unused; } GXTexObj;
typedef struct { u8 a,b,c,d,op,dest,konst,reserved; } DndTevStage;
typedef struct { int count,uniforms;DndTevStage stages[16]; } DndDrawEffect;
typedef struct { const void *data; unsigned bytes; int width,height,format,levels,sampler_set,wrap_s,wrap_t; } DndDrawTexture;
typedef struct { const DndDrawEffect *effect; const void *secondary; } DndDrawMaterial;
typedef struct { float u,v; unsigned color; } DndDrawVertex;
enum {
    GX_FALSE=0,
    GX_TRUE=1,
    GX_TF_I4=0,
    GX_NEAR=0,
    GX_TEXMAP1=1,
    GX_TEXMAP3=3,
    GX_TEXCOORD3=3,
    GX_TEXMTX7=51,
    GX_TF_I8=1,
    GX_TF_IA4=2,
    GX_CLAMP=0,
    GX_LINEAR=1,
    GX_ANISO_1=0,
    GX_TEXMAP0=0,
    GX_TEXCOORD0=0,
    GX_TEXMAP2=2,
    GX_TEXCOORD1=1,
    GX_TEXCOORD2=2,
    GX_TEXCOORD_NULL=255,
    GX_TEXMAP_NULL=255,
    GX_COLOR_NULL=255,
    GX_COLOR0A0=4,
    GX_TG_MTX2x4=1,
    GX_TG_MTX3x4=0,
    GX_TG_TEX0=4,
    GX_TG_POS=0,
    GX_TG_NRM=1,
    GX_IDENTITY=60,
    GX_TEXMTX8=54,
    GX_TEXMTX9=57,
    GX_MTX2x4=1,
    GX_MTX3x4=0,
    GX_DTTMTX0=64,
    GX_CH_RED=0,
    GX_CH_GREEN=1,
    GX_CH_BLUE=2,
    GX_CH_ALPHA=3,
    GX_TEV_SWAP1=1,
    GX_TEV_SWAP0=0,
    GX_TEV_ADD=0,
    GX_TB_ZERO=0,
    GX_CS_SCALE_1=0,
    GX_TEVPREV=0,
    GX_TEVREG0=1,
    GX_TEVREG1=2,
    GX_TEVREG2=3,
    GX_TEV_KASEL_1=0,
    GX_KCOLOR3=3,
    GX_KCOLOR2=2,
    GX_KCOLOR1=1,
    GX_TEV_KASEL_K2_A=30,
    GX_TEV_KCSEL_K0=12,
    GX_TEV_KCSEL_K1=13,
    GX_TEV_KCSEL_K2=14,
    GX_TEV_KCSEL_K3=15,
    GX_CC_CPREV=0,
    GX_CC_C0=2,
    GX_CC_C1=4,
    GX_CC_C2=6,
    GX_CC_A2=7,
    GX_CC_TEXC=8,
    GX_TF_RGB565=4,
    GX_TEXCOORD4=4,
    GX_TEXMTX6=48,
    GX_TEV_SWAP2=2,
    GX_TEV_SWAP3=3,
    GX_TEV_SUB=1,
    GX_CS_DIVIDE_2=3,
    GX_CC_RASC=10,GX_CC_ONE=12,GX_CC_KONST=14,GX_CC_ZERO=15,GX_CA_APREV=0,GX_CA_A2=3,GX_CA_TEXA=4,
    GX_CA_RASA=5,GX_CA_KONST=6,GX_CA_ZERO=7,
    GX_TEV_COMP_A8_GT=14,GX_TEV_COMP_A8_EQ=15,GX_TEV_KASEL_3_4=2,GX_EQUAL=2,GX_ALWAYS=7,GX_AOP_AND=0,
    GX_TEXCOORD5=5,GX_TEXCOORD6=6,GX_TEXMAP4=4,GX_TEXMAP5=5,GX_TEXMAP6=6,GX_TEXMTX4=42,GX_TEXMTX5=45,
    GX_CA_A0=1,GX_CA_A1=2,GX_CC_A0=3,GX_KCOLOR0=0,GX_TEV_KASEL_K0_A=28,GX_TEV_KASEL_K0_G=20,
    GX_TEV_KCSEL_K0_A=28,GX_TEV_COMP_R8_GT=8
};
typedef struct { int input[4],destination,op,konst; } AlphaCommand;
typedef struct { int input[4],destination,konst; } ColorCommand;
typedef struct { int coord,map,channel,raster_swap,texture_swap,direct; } TevOrder;
static AlphaCommand alpha_commands[16];
static ColorCommand color_commands[16];
static int alpha_selection[16];
static TevOrder tev_orders[16];
static int swap_alpha_channel[4]={GX_CH_ALPHA,GX_CH_ALPHA,GX_CH_ALPHA,GX_CH_ALPHA};
static int alpha_command_count,texgens,packed_texgen_source,texture_loads,shadow_loads,tev_order_writes,kalpha_writes;
static int shadow_page_side=SU_SIDE,shadow_page_missing;
static int texture_width,texture_height,texture_min,texture_mag;
static int texture_format,environment_test_rgb=0xffffff,point_test_gain=255,shadow_test_gain=255;
static int alpha_compare;
static int path_test_alpha;
static GXColor konst_colors[4];
static GXColor tev_colors[4];
static GXColor shadow_tint;
static float shadow_uv_scale,shadow_uv_offset,shadow_uv_scale_z,shadow_uv_offset_z;
static float snow_scale_z[3],snow_offset_z[3];
float animaltown_mip_x,animaltown_mip_z,animaltown_mip_radius;
static void DCFlushRange(void *data,size_t bytes){(void)data;(void)bytes;}
static unsigned GX_GetTexBufferSize(int w,int h,int format,int mip,int level){
    assert(format==GX_TF_RGB565&&!mip&&!level);return w*h*2;
}
static void GX_InitTexObj(GXTexObj *t,void *data,u16 w,u16 h,int format,int s,int v,int mip){
    (void)t;(void)data;(void)s;(void)v;(void)mip;texture_width=w;texture_height=h;texture_format=format;
}
static void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float low,float high,float bias,int clamp,int edge,int aniso){
    (void)t;(void)low;(void)high;(void)bias;(void)clamp;(void)edge;(void)aniso;texture_min=min;texture_mag=mag;
}
static void GX_LoadTexObj(GXTexObj *t,int map){
    (void)t;assert(map>=GX_TEXMAP2&&map<=GX_TEXMAP6);
    texture_loads++;if(map==GX_TEXMAP2)shadow_loads++;
}
static void GX_SetNumTevStages(u8 count){assert(count<=16);alpha_command_count=count;}
static void GX_SetNumTexGens(int count){assert(count>=3&&count<=7);texgens=count;}
static void GX_SetTexCoordGen(int coord,int type,int source,int matrix){
    (void)type;(void)matrix;if(coord==GX_TEXCOORD2)packed_texgen_source=source;
}
static void GX_SetTexCoordGen2(int coord,int type,int source,int matrix,int normalize,int post){
    (void)coord;(void)type;(void)source;(void)matrix;(void)normalize;(void)post;
}
static void GX_SetTexCoordScaleManually(int coord,int manual,int s,int t){(void)coord;(void)manual;(void)s;(void)t;}
static void GX_SetTexCoordBias(int coord,int s,int t){(void)coord;(void)s;(void)t;}
static void GX_LoadTexMtxImm(Mtx matrix,int slot,int type){
    (void)type;if(slot==GX_TEXMTX8){shadow_uv_scale=matrix[0][0];shadow_uv_offset=matrix[0][3];shadow_uv_scale_z=matrix[1][1];shadow_uv_offset_z=matrix[1][3];}
    int sample=slot==GX_TEXMTX4?0:slot==GX_TEXMTX6?1:slot==GX_TEXMTX7?2:-1;
    if(sample>=0){snow_scale_z[sample]=matrix[1][1];snow_offset_z[sample]=matrix[1][3];}
}
static void GX_SetTevSwapModeTable(int t,int r,int g,int b,int a){
    assert(t>=0&&t<4&&((r==GX_CH_RED&&g==GX_CH_GREEN&&b==GX_CH_BLUE)||(t==GX_TEV_SWAP3&&r==GX_CH_GREEN&&g==GX_CH_GREEN&&b==GX_CH_GREEN)));swap_alpha_channel[t]=a;
}
static void GX_SetTevDirect(u8 stage){assert(stage<16);tev_orders[stage].direct=1;}
static void GX_SetTevSwapMode(u8 stage,int raster,int tex){
    assert(stage<16);tev_orders[stage].raster_swap=raster;tev_orders[stage].texture_swap=tex;
}
static void GX_SetTevOrder(u8 stage,int coord,int map,int raster){
    assert(stage<16);tev_orders[stage].coord=coord;tev_orders[stage].map=map;
    tev_orders[stage].channel=raster;tev_order_writes++;
}
static void GX_SetTevColorIn(u8 stage,int a,int b,int c,int d){
    assert(stage<16);ColorCommand *out=&color_commands[stage];
    out->input[0]=a;out->input[1]=b;out->input[2]=c;out->input[3]=d;
}
static void GX_SetTevColorOp(u8 stage,int op,int bias,int scale,int clamp,int destination){
    assert(stage<16&&(op==GX_TEV_ADD||op==GX_TEV_SUB||op==GX_TEV_COMP_R8_GT)&&bias==GX_TB_ZERO&&(scale==GX_CS_SCALE_1||scale==GX_CS_DIVIDE_2)&&(clamp==GX_TRUE||clamp==GX_FALSE));
    color_commands[stage].destination=destination;
}
static void GX_SetAlphaCompare(int a,int threshold,int combine,int b,int ref){
    assert(a==GX_EQUAL&&combine==GX_AOP_AND&&b==GX_ALWAYS&&ref==0);
    alpha_compare=threshold;
}
static void GX_SetTevKAlphaSel(u8 stage,int selection){assert(stage<16);alpha_selection[stage]=selection;alpha_commands[stage].konst=selection;kalpha_writes++;}
static void GX_SetTevKColor(int slot,GXColor color){assert(slot>=0&&slot<4);konst_colors[slot]=color;if(slot==GX_KCOLOR3)shadow_tint=color;}
static void GX_SetTevColor(int slot,GXColor color){assert(slot>=0&&slot<4);tev_colors[slot]=color;}
static void GX_SetTevKColorSel(u8 stage,int selection){assert(stage<16);color_commands[stage].konst=selection;}
static void GX_SetTevAlphaIn(u8 stage,int a,int b,int c,int d){
    assert(stage<16);AlphaCommand *out=&alpha_commands[stage];
    out->input[0]=a;out->input[1]=b;out->input[2]=c;out->input[3]=d;
}
static void GX_SetTevAlphaOp(u8 stage,int op,int bias,int scale,int clamp,int destination){
    assert(stage<16&&(op==GX_TEV_ADD||op==GX_TEV_COMP_A8_GT||op==GX_TEV_COMP_A8_EQ)&&bias==GX_TB_ZERO&&scale==GX_CS_SCALE_1&&clamp==GX_TRUE);
    alpha_commands[stage].destination=destination;alpha_commands[stage].op=op;
}
int animaltown_environment_color(void){return environment_test_rgb;}
int animaltown_light_strength(void){return point_test_gain;}
int animaltown_shadow_strength(void){return shadow_test_gain;}
static int snow_test_ready,path_test_ready,shadow_test_mode=3;
static int snow_test_height=SU_SNOW_SIDE,snow_test_first;
const uint8_t *animaltown_snow_mask(void){static const uint8_t image[SU_SNOW_BYTES]={0};return snow_test_ready?image:NULL;}
int animaltown_snow_layout(int *first_z){*first_z=snow_test_first;return snow_test_height;}
const uint8_t *animaltown_path_mask(void){static const uint8_t image[8192]={0};return path_test_ready?image:NULL;}
int animaltown_shadows_mode(void){return shadow_test_mode;}
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

enum { GX_LIGHT6=64,GX_LIGHT7=128,GX_COLOR1A1=5,GX_COLOR1=1,GX_ALPHA1=3,
       GX_ENABLE=1,GX_DISABLE=0,GX_SRC_REG=0,GX_SRC_VTX=1,GX_LIGHTNULL=0,GX_DF_CLAMP=2,GX_DF_NONE=0,GX_AF_SPOT=2,GX_AF_NONE=0 };
typedef struct {float position[3],quadratic;GXColor color;} GXLightObj;
static GXLightObj recorded_points[2];
static int point_test_scope,point_test_count,point_channels,point_material_source;
static float point_test_data[20];
int animaltown_object_scope(void){return point_test_scope;}
const float *animaltown_point_sources(int *count){*count=point_test_count;return point_test_data;}
static void GX_InitLightPos(GXLightObj *l,float x,float y,float z){l->position[0]=x;l->position[1]=y;l->position[2]=z;}
static void GX_InitLightColor(GXLightObj *l,GXColor color){l->color=color;}
static void GX_InitLightAttn(GXLightObj *l,float a,float b,float c,float d,float e,float f){assert(a==1&&b==0&&c==0&&d==1&&e==0);l->quadratic=f;}
static void GX_LoadLightObj(GXLightObj *l,unsigned id){assert(id==GX_LIGHT6||id==GX_LIGHT7);recorded_points[id==GX_LIGHT7]=*l;}
static void GX_SetNumChans(int count){point_channels=count;}
static void GX_SetChanAmbColor(int channel,GXColor color){assert(channel==GX_COLOR1A1&&color.r==0&&color.g==0&&color.b==0);}
static void GX_SetChanMatColor(int channel,GXColor color){assert(channel==GX_COLOR1A1);(void)color;}
static void GX_SetChanCtrl(int channel,int enabled,int ambient,int source,unsigned mask,int diffuse,int attenuation){
    assert(ambient==GX_SRC_REG);point_material_source=source;
    if(channel==GX_COLOR1)assert(enabled==GX_ENABLE&&mask&&diffuse==GX_DF_CLAMP&&attenuation==GX_AF_SPOT);
    else assert(channel==GX_ALPHA1&&enabled==GX_DISABLE&&mask==GX_LIGHTNULL);
}
#include "../tools/native-point-lights-gx.inc"

static int alpha_operand(int source,const int registers[4],int mask,int raster,int konst){
    if(source<=3)return registers[source];
    if(source==GX_CA_TEXA)return mask;
    if(source==GX_CA_ZERO)return 0;
    if(source==GX_CA_RASA)return raster;
    return konst==GX_TEV_KASEL_3_4?191:255;
}
static int footer_alpha_for_raster(int first,int previous,int mask,int raster){
    int registers[4]={previous,0,0,0};
    for(int stage=first;stage<alpha_command_count;stage++){
        const AlphaCommand *command=&alpha_commands[stage];
        int sample=tev_orders[stage].map==GX_TEXMAP3?path_test_alpha:mask;
        int a=alpha_operand(command->input[0],registers,sample,raster,command->konst)&255;
        int b=alpha_operand(command->input[1],registers,sample,raster,command->konst)&255;
        int c=alpha_operand(command->input[2],registers,sample,raster,command->konst)&255;
        int d=alpha_operand(command->input[3],registers,sample,raster,command->konst);
        int weight=c+(c>>7);
        int value=d+(command->op==GX_TEV_COMP_A8_GT?(a>b?c:0):command->op==GX_TEV_COMP_A8_EQ?(a==b?c:0):((a*(256-weight)+b*weight+128)>>8));
        registers[command->destination]=value<0?0:value>255?255:value;
    }
    return registers[0]&255;
}
static int footer_alpha(int first,int previous,int mask){return footer_alpha_for_raster(first,previous,mask,255);}
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
static void reset_shadow_recording(void){
    texgens=packed_texgen_source=texture_loads=shadow_loads=tev_order_writes=kalpha_writes=0;
    memset(tev_orders,0,sizeof(tev_orders));
    /* A preceding material can leave selectors in stages that use raster alpha. */
    for(int stage=0;stage<16;stage++)alpha_selection[stage]=GX_TEV_KASEL_K2_A;
}
static void verify_shadow_orders(int first,int mode,int paths,unsigned bindings){
    assert(alpha_command_count==first+4&&texgens==3+paths&&texture_loads==1+paths&&shadow_loads==1);
    assert(bindings==(unsigned)(1+paths)&&tev_order_writes==4+3*paths);
    assert(kalpha_writes==(mode==2?3:1)&&packed_texgen_source==(mode==2?GX_TG_POS:GX_TG_TEX0));
    assert(swap_alpha_channel[GX_TEV_SWAP1]==GX_CH_RED);
    for(int i=0;i<4;i++){
        const TevOrder *order=&tev_orders[first+i];
        assert(order->direct&&order->raster_swap==GX_TEV_SWAP0&&order->texture_swap==(i==0?GX_TEV_SWAP1:GX_TEV_SWAP0));
        assert(order->coord==(i%2==0?GX_TEXCOORD2:GX_TEXCOORD_NULL));
        assert(order->map==(i%2==0?GX_TEXMAP2:GX_TEXMAP_NULL));
        assert(order->channel==(i<3?GX_COLOR0A0:GX_COLOR_NULL));
        assert(color_commands[first+i].destination==GX_TEVPREV);
        assert(alpha_commands[first+i].destination==(i==3?GX_TEVPREV:GX_TEVREG2));
    }
    int expected_kalpha=mode==2?GX_TEV_KASEL_1:GX_TEV_KASEL_K2_A;
    assert(alpha_selection[first]==expected_kalpha&&alpha_selection[first+2]==expected_kalpha);
    assert(alpha_selection[first+1]==GX_TEV_KASEL_K2_A);
}
static int water_combine(int a,int b,int c,int d){
    int weight=c+(c>>7),value=d+((a*(256-weight)+b*weight+128)>>8);
    return value<0?0:value>255?255:value;
}
static int color_channel(GXColor color,int channel){return channel==0?color.r:channel==1?color.g:channel==2?color.b:color.a;}
static int water_color_operand(int source,int channel,const int registers[4][4],int stage){
    if(source<=7)return registers[source/2][source&1?3:channel];
    if(source==GX_CC_ZERO)return 0;
    assert(source==GX_CC_KONST);
    int slot=color_commands[stage].konst-GX_TEV_KCSEL_K0;assert(slot>=0&&slot<4);
    return color_channel(konst_colors[slot],channel);
}
static int shadow_alpha_operand(int source,const int registers[4][4],int stage,int shadow,int light,int receiver){
    if(source<=3)return registers[source][3];
    if(source==GX_CA_ZERO)return 0;
    if(source==GX_CA_TEXA){
        assert(tev_orders[stage].map==GX_TEXMAP2);
        return swap_alpha_channel[tev_orders[stage].texture_swap]==GX_CH_RED?shadow:light;
    }
    if(source==GX_CA_RASA)return receiver;
    if(source==GX_CA_KONST&&alpha_selection[stage]==GX_TEV_KASEL_K2_A)return konst_colors[2].a;
    assert(source==GX_CA_KONST&&alpha_selection[stage]==GX_TEV_KASEL_1);return 255;
}
static void run_shadow_stages(int first,int registers[4][4],int shadow,int light,int receiver){
    for(int stage=first;stage<alpha_command_count;stage++){
        const ColorCommand *rgb=&color_commands[stage];int out[3];
        for(int channel=0;channel<3;channel++){
            int values[4];for(int i=0;i<4;i++)values[i]=water_color_operand(rgb->input[i],channel,registers,stage);
            out[channel]=water_combine(values[0]&255,values[1]&255,values[2]&255,values[3]);
        }
        const AlphaCommand *alpha=&alpha_commands[stage];int values[4];
        for(int i=0;i<4;i++)values[i]=shadow_alpha_operand(alpha->input[i],registers,stage,shadow,light,receiver);
        int opacity=water_combine(values[0]&255,values[1]&255,values[2]&255,values[3]);
        for(int channel=0;channel<3;channel++)registers[rgb->destination][channel]=out[channel];
        registers[alpha->destination][3]=opacity;
    }
}
static void recorded_water_pixel(int foam0,int foam1,int shadow,int light,int result[3]){
    const int tint[3]={75,154,187};int registers[4][4];
    for(int reg=0;reg<4;reg++)for(int channel=0;channel<4;channel++)registers[reg][channel]=color_channel(tev_colors[reg],channel);
    /* The authored WaterWarp program blends both sampled foam weights before
     * this production footer. Use the final recorded constants so any footer
     * collision changes its result, exactly as it does at primitive execution. */
    for(int channel=0;channel<3;channel++){
        int first=water_combine(tint[channel],color_channel(konst_colors[0],channel),foam0,0);
        registers[0][channel]=water_combine(first,color_channel(konst_colors[1],channel),foam1,0);
    }
    registers[0][3]=173;
    run_shadow_stages(4,registers,shadow,light,255);
    assert(registers[0][3]==173);
    for(int channel=0;channel<3;channel++)result[channel]=registers[0][channel];
}
static void verify_shadow_colors(int first,int mode,int gain,int rgb){
    const int dark[]={35,49,66},warm[]={220,158,74},previous[]={224,103,47};
    for(int receiver=0;receiver<=255;receiver+=85)for(int texel=0;texel<=255;texel++){
        int flag=mode==2?255:receiver,shadow=(texel&15)*17,light=(texel>>4)*17;
        int opacity=water_combine(0,water_combine(0,shadow,flag,0),SU_OPACITY,0);
        int illumination=water_combine(0,light,flag,0);
        int registers[4][4]={{previous[0],previous[1],previous[2],300}};
        run_shadow_stages(first,registers,shadow,light,receiver);
        for(int c=0;c<3;c++){
            int tint=(rgb>>(16-8*c))&255;
            int color=water_combine(0,previous[c],tint,0);
            color=water_combine(color,dark[c]*tint/255,opacity,0);
            assert(registers[0][c]==water_combine(0,warm[c]*gain/255,illumination,color));
        }
        assert(registers[0][3]==44);
    }
}
static void water_shadow_color_checks(void){
    DndDrawEffect water={.count=4,.uniforms=2};DndDrawMaterial material={.effect=&water};
    const DndDrawVertex triangle[]={{0,0,255},{1,0,255},{0,1,255}};
    const GXColor foam0={196,228,222,255},foam1={229,245,237,255};
    const int tint[3]={75,154,187},shadow_tone[3]={35,49,66},lamp_tone[3]={220,158,74};
    assert(town_shadow_lamp_register(&water)==GX_TEVREG0);
    /* Packed destinations and read-only, initially-zero RGB inputs must both
     * reserve their registers. Alpha writes remain a separate component. */
    water.stages[0].dest=128|64|GX_TEVREG0;
    assert(town_shadow_lamp_register(&water)==GX_TEVREG1);
    water.stages[1].a=GX_CC_C1;assert(town_shadow_lamp_register(&water)==GX_TEVREG2);
    water.stages[2].c=GX_CC_C2;assert(town_shadow_lamp_register(&water)==0);
    memset(water.stages,0,sizeof water.stages);
    water.uniforms=1;assert(town_shadow_lamp_register(&water)==0);
    water.uniforms=3;assert(town_shadow_lamp_register(&water)==0);water.uniforms=2;

    for(int night=0;night<2;night++)for(int gain=0;gain<=255;gain+=51){
        environment_test_rgb=night?0x607aa0:0xffffff;shadow_test_gain=gain;point_test_gain=255-gain;
        memset(tev_colors,0,sizeof tev_colors);memset(color_commands,0,sizeof color_commands);
        konst_colors[0]=foam0;konst_colors[1]=foam1;
        unsigned bindings=0;reset_shadow_recording();town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
        verify_shadow_orders(4,3,0,bindings);
        assert(!memcmp(&konst_colors[0],&foam0,sizeof foam0)&&!memcmp(&konst_colors[1],&foam1,sizeof foam1));
        assert(tev_colors[GX_TEVREG0].r==220*point_test_gain/255&&tev_colors[GX_TEVREG0].a==0);
        assert(color_commands[7].input[1]==GX_CC_C0);
        for(int first=0;first<=255;first+=85)for(int second=0;second<=255;second+=85)
        for(int shadow=0;shadow<=255;shadow+=85)for(int light=0;light<=255;light+=85){
            int actual[3];recorded_water_pixel(first,second,shadow,light,actual);
            for(int channel=0;channel<3;channel++){
                int env=(environment_test_rgb>>(16-channel*8))&255;
                int base=water_combine(tint[channel],color_channel(foam0,channel),first,0);
                base=water_combine(base,color_channel(foam1,channel),second,0);
                int shaded=water_combine(water_combine(0,base,env,0),shadow_tone[channel]*env/255,
                                         water_combine(0,shadow,(SU_OPACITY*gain+127)/255,0),0);
                int expected=water_combine(0,lamp_tone[channel]*point_test_gain/255,light,shaded);
                assert(actual[channel]==expected);
            }
        }
        if(!gain){
            int clear[3],covered[3],lit[3];
            recorded_water_pixel(85,170,0,0,clear);recorded_water_pixel(85,170,255,0,covered);recorded_water_pixel(85,170,255,255,lit);
            assert(!memcmp(clear,covered,sizeof clear));
            assert(lit[0]>covered[0]&&lit[1]>covered[1]&&lit[2]>covered[2]);
        }
    }
    environment_test_rgb=0xffffff;point_test_gain=shadow_test_gain=255;
    puts("Water shadow GX: day/dusk/night fades, invisible night shadows, surviving lamps/foam, alpha and free-register allocation passed");
}
static void shallow_water_shadow_checks(void){
    DndDrawEffect water={.count=4,.uniforms=2};DndDrawMaterial material={.effect=&water};
    const DndDrawVertex triangle[]={{0,0,72},{1,0,108},{0,1,144}};
    const GXColor lower={54,127,145,255},glint={211,242,236,255};
    const int tint[]={99,181,196},dark[]={35,49,66},warm[]={220,158,74};
    /* The inland shader now uses RGB REG0 for its half-strength dark ripple.
     * Its alpha is explicit raster opacity, independent of that I8 sample. */
    water.stages[1].dest=128|64|GX_TEVREG0;water.stages[2].c=GX_CC_C0;
    assert(town_shadow_lamp_register(&water)==GX_TEVREG1);
    shadow_test_mode=3;point_test_gain=173;shadow_test_gain=191;
    for(int night=0;night<2;night++){
        environment_test_rgb=night?0x607aa0:0xffffff;
        memset(tev_colors,0,sizeof tev_colors);memset(color_commands,0,sizeof color_commands);
        konst_colors[0]=lower;konst_colors[1]=glint;
        unsigned bindings=0;reset_shadow_recording();town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
        verify_shadow_orders(4,3,0,bindings);
        assert(!memcmp(&konst_colors[0],&lower,sizeof lower)&&!memcmp(&konst_colors[1],&glint,sizeof glint));
        assert(color_commands[7].input[1]==GX_CC_C1&&tev_colors[GX_TEVREG1].a==0);
        for(int alpha=72;alpha<=144;alpha+=36)for(int ripple=0;ripple<=255;ripple+=85)
        for(int shadow=0;shadow<=255;shadow+=85)for(int light=0;light<=255;light+=85){
            int registers[4][4],base[3];
            for(int reg=0;reg<4;reg++)for(int c=0;c<4;c++)registers[reg][c]=color_channel(tev_colors[reg],c);
            for(int c=0;c<3;c++){
                int weight=water_combine(0,ripple,128,0);
                registers[GX_TEVREG0][c]=weight;
                base[c]=water_combine(tint[c],color_channel(lower,c),weight,0);
                base[c]=water_combine(base[c],color_channel(glint,c),255-ripple,0);
                registers[GX_TEVPREV][c]=base[c];
            }
            registers[GX_TEVPREV][3]=alpha;run_shadow_stages(4,registers,shadow,light,alpha);
            assert(registers[GX_TEVPREV][3]==alpha);
            for(int c=0;c<3;c++){
                int env=(environment_test_rgb>>(16-c*8))&255;
                int opacity=water_combine(0,water_combine(0,shadow,alpha,0),(SU_OPACITY*shadow_test_gain+127)/255,0);
                int shaded=water_combine(water_combine(0,base[c],env,0),dark[c]*env/255,opacity,0);
                int expected=water_combine(0,warm[c]*point_test_gain/255,water_combine(0,light,alpha,0),shaded);
                assert(registers[GX_TEVPREV][c]==expected);
            }
        }
    }
    environment_test_rgb=0xffffff;point_test_gain=shadow_test_gain=255;
    puts("Shallow water GX: depth opacity survives shadow/light footer, RGB scratch and lamp registers coexist");
}
static void path_blend_color_checks(void){
    int page=1;DndDrawMaterial material={.secondary=&page};unsigned bindings=0;
    const int ground[3]={94,135,57},dirt[3]={154,128,93};
    path_test_ready=1;assert(town_paths_apply(&material,0,0,&bindings)==3&&bindings==1);
    for(int level=0;level<16;level++)for(int receiver=0;receiver<=255;receiver+=255){
        int registers[4][4]={{0}};
        for(int channel=0;channel<3;channel++)registers[0][channel]=ground[channel];
        registers[0][3]=173;
        for(int stage=0;stage<3;stage++){
            const ColorCommand *rgb=&color_commands[stage];int out[3];
            for(int channel=0;channel<3;channel++){
                int values[4];for(int i=0;i<4;i++)values[i]=rgb->input[i]==GX_CC_TEXC?dirt[channel]:water_color_operand(rgb->input[i],channel,registers,stage);
                out[channel]=water_combine(values[0]&255,values[1]&255,values[2]&255,values[3]);
            }
            const AlphaCommand *alpha=&alpha_commands[stage];int values[4];
            for(int i=0;i<4;i++){
                int input=alpha->input[i];
                if(input<=3)values[i]=registers[input][3];
                else if(input==GX_CA_ZERO)values[i]=0;
                else if(input==GX_CA_TEXA)values[i]=level*17;
                else {assert(input==GX_CA_RASA);values[i]=receiver;}
            }
            int opacity=water_combine(values[0]&255,values[1]&255,values[2]&255,values[3]);
            for(int channel=0;channel<3;channel++)registers[rgb->destination][channel]=out[channel];
            registers[alpha->destination][3]=opacity;
        }
        int weight=water_combine(0,level*17,receiver,0);
        for(int channel=0;channel<3;channel++)assert(registers[0][channel]==water_combine(ground[channel],dirt[channel],weight,0));
        assert(registers[0][3]==173);
    }
    path_test_ready=0;
    puts("Path blend GX: all 16 opacity levels interpolate ground/dirt, retain receiver exclusion and preserve ground alpha");
}
static void native_shadow_combiner_checks(void){
    path_blend_color_checks();
    DndDrawEffect effect={.count=2};DndDrawMaterial material={.effect=&effect};
    const DndDrawVertex triangle[]={{0,0,255},{1,0,255},{0,1,255}};
    const uint16_t indices[]={1,2,0};
    const float normal[]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    unsigned bindings=0;reset_shadow_recording();town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    verify_shadow_orders(2,3,0,bindings);verify_alpha_footer(2);verify_shadow_colors(2,3,255,0xffffff);
    assert(shadow_tint.r==35&&shadow_tint.g==49&&shadow_tint.b==66);
    assert(texture_format==GX_TF_IA4&&konst_colors[GX_KCOLOR2].a==SU_OPACITY);
    assert(konst_colors[GX_KCOLOR1].r==220&&konst_colors[GX_KCOLOR1].g==158&&konst_colors[GX_KCOLOR1].b==74);
    assert(texture_width==32&&texture_height==32&&texture_min==GX_LINEAR&&texture_mag==GX_LINEAR);
    assert(fabsf(shadow_uv_scale-4.f*30/(32*8))<.000001f&&shadow_uv_offset==1.f/32);
    shadow_page_side=SU_TERRAIN_WIDTH;town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(texture_width==256&&texture_height==128);
    assert(fabsf(shadow_uv_scale-4.f*SU_TERRAIN_TEXELS_PER_METRE/256)<.000001f&&shadow_uv_offset==1.f/256);
    assert(fabsf(shadow_uv_scale_z-4.f/128)<.000001f&&shadow_uv_offset_z==1.f/128);
    shadow_page_side=SU_SIDE;shadow_page_missing=1;town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(texture_width==32&&texture_height==32&&sizeof(town_shadow_empty)==1024);
    shadow_page_missing=0;
    path_test_ready=1;shadow_test_mode=1;material.secondary=&effect;bindings=0;
    reset_shadow_recording();town_shadow_ground(triangle,indices,3,NULL,&material,&bindings);
    verify_shadow_orders(5,1,1,bindings);verify_alpha_footer(2);
    // Retain the receiver channel throughout the added path passes and the
    // following shadow/light samples, independently of their RGB inputs.
    for(int stage=2;stage<8;stage++)assert(tev_orders[stage].channel==GX_COLOR0A0);
    path_test_ready=0;town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(alpha_command_count==6);verify_alpha_footer(2);
    material.secondary=NULL;
    for(int gain=0;gain<=255;gain++){
        point_test_gain=gain;environment_test_rgb=0x596c91;bindings=0;
        reset_shadow_recording();town_shadow_ground(triangle,gain%2?indices:NULL,3,NULL,&material,&bindings);
        verify_shadow_orders(2,1,0,bindings);
        assert(konst_colors[GX_KCOLOR1].r==220*gain/255&&konst_colors[GX_KCOLOR1].g==158*gain/255&&konst_colors[GX_KCOLOR1].b==74*gain/255);
        if(gain==0||gain==128||gain==255)verify_shadow_colors(2,1,gain,environment_test_rgb);
    }
    /* Indoor position projection retains both always-on selectors and fits
     * in the final four hardware stages after a 12-stage base effect. */
    bindings=0;shadow_test_mode=2;environment_test_rgb=0xffffff;point_test_gain=255;effect.count=12;
    reset_shadow_recording();town_shadow_apply(0,0,normal,&effect,&bindings);
    verify_shadow_orders(12,2,0,bindings);verify_shadow_colors(12,2,255,environment_test_rgb);verify_alpha_footer(12);
    effect.count=2;shadow_test_mode=3;bindings=1;
    shadow_page_missing=1;
    shadow_test_mode=4;
    town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(texture_format==GX_TF_IA4&&texgens==3&&alpha_command_count==5);
    assert(footer_alpha_for_raster(0,0,0,254)==alpha_compare);
    shadow_page_missing=0;snow_test_ready=1;
    town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(texture_format==GX_TF_I8&&texgens==7&&alpha_command_count==8);
    assert(tev_orders[7].map==GX_TEXMAP4&&tev_orders[7].coord==GX_TEXCOORD6);
    assert(fabsf(shadow_uv_scale-2.f*30/(32*8))<.000001f);
    assert(alpha_compare==254);
    assert(footer_alpha_for_raster(0,0,190,254)==alpha_compare);
    assert(footer_alpha_for_raster(0,0,191,254)==0);
    assert(footer_alpha_for_raster(0,0,255,254)==0);
    assert(footer_alpha_for_raster(0,0,0,255)!=alpha_compare);
    assert(footer_alpha_for_raster(0,0,0,0)!=alpha_compare);
    const float relief_z[]={0,-.3f,.3f};
    for(int strip=0;strip<=1;strip++){
        snow_test_height=strip?SU_SNOW_STRIP_TEXELS:SU_SNOW_SIDE;
        snow_test_first=strip?19:0;
        bindings=0;town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
        assert(texture_format==GX_TF_I8&&texture_width==SU_SNOW_SIDE&&texture_height==snow_test_height);
        assert(texture_min==GX_LINEAR&&texture_mag==GX_LINEAR&&bindings==2);
        for(int sample=0;sample<3;sample++){
            const float logical_z=47.25f;
            float actual=(logical_z*.5f*snow_scale_z[sample]+snow_offset_z[sample])*snow_test_height;
            float expected=(logical_z-snow_test_first+1)*4+relief_z[sample];
            assert(snow_scale_z[sample]*snow_test_height==8&&fabsf(actual-expected)<.0001f);
        }
    }
    snow_test_height=SU_SNOW_SIDE;snow_test_first=0;
    path_test_ready=1;bindings=0;
    reset_shadow_recording();town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(alpha_command_count==9&&texgens==7&&bindings==3);
    assert(tev_orders[8].map==GX_TEXMAP3&&tev_orders[8].coord==GX_TEXCOORD5);
    for(int gain=0;gain<=255;gain+=51){
        shadow_test_gain=gain;town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
        assert(konst_colors[GX_KCOLOR2].a==(66*gain+127)/255);
        for(int level=0;level<16;level++){
            path_test_alpha=level*17;
            assert(footer_alpha_for_raster(0,0,0,254)==(level?0:254));
            assert(footer_alpha_for_raster(0,0,255,254)==0);
        }
    }
    snow_test_ready=path_test_ready=path_test_alpha=0;shadow_test_gain=255;
    shadow_test_mode=6;bindings=0;
    reset_shadow_recording();town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);
    assert(alpha_command_count==6&&texgens==3&&bindings==1);
    static const uint8_t ground_pixels[2560]={0};
    const DndDrawTexture ground={.data=ground_pixels,.bytes=sizeof ground_pixels,.width=32,.height=32,.format=GX_TF_RGB565,.levels=2};
    animaltown_mip_x=12.5f;animaltown_mip_z=40.25f;animaltown_mip_radius=4;
    material.effect=NULL;bindings=0;
    reset_shadow_recording();town_shadow_ground(triangle,NULL,3,&ground,&material,&bindings);
    assert(alpha_command_count==11&&texgens==5&&bindings==3);
    assert(tev_orders[1].map==GX_TEXMAP4&&tev_orders[2].map==GX_TEXMAP5&&tev_orders[2].coord==GX_TEXCOORD4);
    assert(tev_orders[4].map==GX_TEXMAP4&&tev_orders[5].map==GX_TEXMAP4);
    assert(alpha_commands[4].input[1]==GX_CA_A1&&alpha_commands[5].konst==GX_TEV_KASEL_K0_G);
    assert(tev_colors[GX_TEVREG1].a==148&&konst_colors[GX_KCOLOR0].a==160&&konst_colors[GX_KCOLOR0].g==172);
    material.secondary=&ground;path_test_ready=1;shadow_test_mode=1;bindings=0;
    reset_shadow_recording();town_shadow_ground(triangle,NULL,3,&ground,&material,&bindings);
    assert(alpha_command_count==12&&texgens==7&&bindings==5);
    assert(tev_orders[5].map==GX_TEXMAP6&&tev_orders[5].coord==GX_TEXCOORD6);
    animaltown_mip_radius=0;path_test_ready=0;material.secondary=NULL;material.effect=&effect;
    shadow_test_mode=3;bindings=1;
    assert(animaltown_rim_enable(100));
    town_rim_apply(normal,&effect,false,&bindings);
    assert(alpha_command_count==3&&bindings==2);verify_alpha_footer(2);
    // A water effect keeps its own constants; tint takes the first free slot.
    environment_test_rgb=0xff9860;effect.uniforms=2;
    GXColor original=konst_colors[1];town_environment_apply(&effect,2);
    assert(alpha_command_count==3&&konst_colors[2].r==255&&konst_colors[2].g==152&&konst_colors[2].b==96);
    assert(!memcmp(&original,&konst_colors[1],sizeof(original)));verify_alpha_footer(2);
    effect.stages[1].dest=128;town_environment_apply(&effect,2);
    assert(alpha_command_count==4);verify_alpha_footer(2);
    environment_test_rgb=0xffffff;
    /* Scope/day gates, nearest-source ranking, eye-space transforms and
     * hardware attenuation are independent of static ground coverage. */
    float points[]={0,.55f,2,4,255, 0,.55f,4,4,210, 100,0,0,4,255};
    memcpy(point_test_data,points,sizeof(points));point_test_count=3;
    float view[16];memcpy(view,normal,sizeof(view));view[12]=3;view[13]=1;view[14]=-8;
    town_points_prepare(normal,view);assert(!town_points_active());
    point_test_scope=1;point_test_gain=0;town_points_prepare(normal,view);assert(!town_points_active());
    point_test_gain=255;town_points_prepare(normal,view);assert(town_point_mask==(GX_LIGHT6|GX_LIGHT7));
    assert(recorded_points[0].position[0]==3&&recorded_points[0].position[2]==-6);
    assert(recorded_points[0].color.r==255&&recorded_points[0].color.g==190&&recorded_points[0].color.b==100);
    assert(recorded_points[1].color.r==210&&fabsf(recorded_points[0].quadratic-.25f)<.000001f);
    /* With the recorded distance attenuation, a front-facing point 2m away
     * receives half the light; 4m receives a fifth. A backface receives none. */
    float near=1/(1+recorded_points[0].quadratic*4),far=1/(1+recorded_points[0].quadratic*16);
    assert(fabsf(near-.5f)<.000001f&&fabsf(far-.2f)<.000001f&&fmaxf(0,-1)*near==0);
    town_point_channels(GX_SRC_REG,(GXColor){255,255,255,255});assert(point_channels==2&&point_material_source==GX_SRC_REG);
    town_point_channels(GX_SRC_VTX,(GXColor){255,255,255,255});assert(point_channels==2&&point_material_source==GX_SRC_VTX);
    town_point_tev(1,4);assert(alpha_command_count==5);verify_alpha_footer(4);
    town_point_tev(0,4);verify_alpha_footer(4);
    /* A point can move through the field without changing any light texels. */
    point_test_data[2]=6;town_points_prepare(normal,view);assert(recorded_points[0].color.r==210);
    point_test_scope=0;town_points_prepare(normal,view);town_point_channels(GX_SRC_REG,(GXColor){0,0,0,0});assert(point_channels==1);
    int previous=alpha_command_count;town_point_tev(1,7);assert(alpha_command_count==previous);
    puts("Shadow GX: four stages, one IA4 binding, two packed sample orders and one outdoor konst-alpha write");
    puts("Shadow/rim GX: signed-wide alpha, low-byte wrap and exact sea cutout survive appended stages");
    puts("Model lights GX: scope/day gates, nearest two, view transform, diffuse attenuation and alpha preservation passed");
    water_shadow_color_checks();
    shallow_water_shadow_checks();
}
