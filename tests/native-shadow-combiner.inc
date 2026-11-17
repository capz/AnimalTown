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
typedef struct { const DndDrawEffect *effect; const void *secondary; } DndDrawMaterial;
typedef struct { float u,v; unsigned color; } DndDrawVertex;
enum {
    GX_FALSE=0,GX_TRUE=1,GX_TF_I4=0,GX_NEAR=0,GX_TEXMAP1=1,GX_TEXMAP3=3,GX_TEXCOORD3=3,GX_TEXMTX7=51,GX_TF_I8=1,GX_TF_IA4=2,GX_CLAMP=0,GX_LINEAR=1,GX_ANISO_1=0,
    GX_TEXMAP0=0,GX_TEXCOORD0=0,GX_TEXMAP2=2,GX_TEXCOORD1=1,GX_TEXCOORD2=2,GX_TEXCOORD_NULL=255,
    GX_TEXMAP_NULL=255,GX_COLOR_NULL=255,GX_COLOR0A0=4,
    GX_TG_MTX2x4=1,GX_TG_MTX3x4=0,GX_TG_TEX0=4,GX_TG_POS=0,GX_TG_NRM=1,
    GX_IDENTITY=60,GX_TEXMTX8=54,GX_TEXMTX9=57,GX_MTX2x4=1,GX_MTX3x4=0,
    GX_DTTMTX0=64,GX_CH_RED=0,GX_CH_GREEN=1,GX_CH_BLUE=2,GX_TEV_SWAP1=1,GX_TEV_SWAP0=0,GX_TEV_ADD=0,GX_TB_ZERO=0,GX_CS_SCALE_1=0,
    GX_TEVPREV=0,GX_TEVREG0=1,GX_TEVREG1=2,GX_TEVREG2=3,GX_TEV_KASEL_1=0,GX_KCOLOR3=3,GX_KCOLOR2=2,GX_KCOLOR1=1,GX_TEV_KASEL_K2_A=30,GX_TEV_KCSEL_K0=12,GX_TEV_KCSEL_K1=13,GX_TEV_KCSEL_K2=14,
    GX_TEV_KCSEL_K3=15,GX_CC_CPREV=0,GX_CC_C0=2,GX_CC_C1=4,GX_CC_C2=6,GX_CC_A2=7,GX_CC_TEXC=8,
    GX_CC_RASC=10,GX_CC_ONE=12,GX_CC_KONST=14,GX_CC_ZERO=15,GX_CA_APREV=0,GX_CA_A2=3,GX_CA_TEXA=4,
    GX_CA_RASA=5,GX_CA_KONST=6,GX_CA_ZERO=7
};
typedef struct { int input[4],destination; } AlphaCommand;
typedef struct { int input[4],destination,konst; } ColorCommand;
static AlphaCommand alpha_commands[16];
static ColorCommand color_commands[16];
static int alpha_selection[16];
static int raster_orders[16];
static int alpha_command_count;
static int shadow_page_side=SU_SIDE,shadow_page_missing;
static int texture_width,texture_height,texture_min,texture_mag;
static int texture_format,environment_test_rgb=0xffffff,point_test_gain=255;
static GXColor konst_colors[4];
static GXColor tev_colors[4];
static GXColor shadow_tint;
static float shadow_uv_scale,shadow_uv_offset,shadow_uv_scale_z,shadow_uv_offset_z;
static void DCFlushRange(void *data,size_t bytes){(void)data;(void)bytes;}
static void GX_InitTexObj(GXTexObj *t,void *data,u16 w,u16 h,int format,int s,int v,int mip){
    (void)t;(void)data;(void)s;(void)v;(void)mip;texture_width=w;texture_height=h;texture_format=format;
}
static void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float low,float high,float bias,int clamp,int edge,int aniso){
    (void)t;(void)low;(void)high;(void)bias;(void)clamp;(void)edge;(void)aniso;texture_min=min;texture_mag=mag;
}
static void GX_LoadTexObj(GXTexObj *t,int map){(void)t;assert(map==GX_TEXMAP2||map==GX_TEXMAP3);}
static void GX_SetNumTevStages(u8 count){assert(count<=16);alpha_command_count=count;}
static void GX_SetNumTexGens(int count){assert(count==3||count==4);}
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
static void GX_SetTevOrder(u8 stage,int coord,int map,int raster){assert(stage<16);(void)coord;(void)map;raster_orders[stage]=raster;}
static void GX_SetTevColorIn(u8 stage,int a,int b,int c,int d){
    assert(stage<16);ColorCommand *out=&color_commands[stage];
    out->input[0]=a;out->input[1]=b;out->input[2]=c;out->input[3]=d;
}
static void GX_SetTevColorOp(u8 stage,int op,int bias,int scale,int clamp,int destination){
    assert(stage<16&&op==GX_TEV_ADD&&bias==GX_TB_ZERO&&scale==GX_CS_SCALE_1&&clamp==GX_TRUE);
    color_commands[stage].destination=destination;
}
static void GX_SetTevKAlphaSel(u8 stage,int selection){assert(stage<16);alpha_selection[stage]=selection;}
static void GX_SetTevKColor(int slot,GXColor color){assert(slot>=0&&slot<4);konst_colors[slot]=color;if(slot==GX_KCOLOR3)shadow_tint=color;}
static void GX_SetTevColor(int slot,GXColor color){assert(slot>=0&&slot<4);tev_colors[slot]=color;}
static void GX_SetTevKColorSel(u8 stage,int selection){assert(stage<16);color_commands[stage].konst=selection;}
static void GX_SetTevAlphaIn(u8 stage,int a,int b,int c,int d){
    assert(stage<16);AlphaCommand *out=&alpha_commands[stage];
    out->input[0]=a;out->input[1]=b;out->input[2]=c;out->input[3]=d;
}
static void GX_SetTevAlphaOp(u8 stage,int op,int bias,int scale,int clamp,int destination){
    assert(stage<16&&op==GX_TEV_ADD&&bias==GX_TB_ZERO&&scale==GX_CS_SCALE_1&&clamp==GX_TRUE);
    alpha_commands[stage].destination=destination;
}
int animaltown_environment_color(void){return environment_test_rgb;}
int animaltown_light_strength(void){return point_test_gain;}
static int path_test_ready,shadow_test_mode=3;
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
static int water_alpha_operand(int source,const int registers[4][4],int stage,int shadow,int light){
    if(source<=3)return registers[source][3];
    if(source==GX_CA_ZERO)return 0;
    if(source==GX_CA_TEXA){assert(stage==4||stage==6);return stage==4?shadow:light;}
    if(source==GX_CA_KONST&&alpha_selection[stage]==GX_TEV_KASEL_K2_A)return konst_colors[2].a;
    assert(source==GX_CA_RASA||source==GX_CA_KONST);return 255;
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
    for(int stage=4;stage<alpha_command_count;stage++){
        const ColorCommand *rgb=&color_commands[stage];int out[3];
        for(int channel=0;channel<3;channel++){
            int values[4];for(int i=0;i<4;i++)values[i]=water_color_operand(rgb->input[i],channel,registers,stage);
            out[channel]=water_combine(values[0]&255,values[1]&255,values[2]&255,values[3]);
        }
        const AlphaCommand *alpha=&alpha_commands[stage];int values[4];
        for(int i=0;i<4;i++)values[i]=water_alpha_operand(alpha->input[i],registers,stage,shadow,light);
        int opacity=water_combine(values[0]&255,values[1]&255,values[2]&255,values[3]);
        for(int channel=0;channel<3;channel++)registers[rgb->destination][channel]=out[channel];
        registers[alpha->destination][3]=opacity;
    }
    assert(registers[0][3]==173);
    for(int channel=0;channel<3;channel++)result[channel]=registers[0][channel];
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

    for(int night=0;night<2;night++){
        environment_test_rgb=night?0x607aa0:0xffffff;point_test_gain=night?255:0;
        memset(tev_colors,0,sizeof tev_colors);memset(color_commands,0,sizeof color_commands);
        konst_colors[0]=foam0;konst_colors[1]=foam1;
        unsigned bindings=0;town_shadow_ground(triangle,NULL,3,&material,&bindings);
        assert(alpha_command_count==8&&bindings==1);
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
                                         water_combine(0,shadow,66,0),0);
                int expected=water_combine(0,lamp_tone[channel]*point_test_gain/255,light,shaded);
                assert(actual[channel]==expected);
            }
        }
    }
    environment_test_rgb=0xffffff;point_test_gain=255;
    puts("Water shadow GX: both foam colors survive; noon/night RGB blends, cloud masks, lamp masks, alpha and free-register allocation passed");
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
    shadow_page_missing=0;
    path_test_ready=1;shadow_test_mode=1;material.secondary=&effect;bindings=0;
    town_shadow_ground(triangle,NULL,3,&material,&bindings);
    assert(alpha_command_count==9&&bindings==2);verify_alpha_footer(2);
    // Retain the receiver channel throughout the added path passes and the
    // following shadow/light samples, independently of their RGB inputs.
    for(int stage=2;stage<8;stage++)assert(raster_orders[stage]==GX_COLOR0A0);
    path_test_ready=0;town_shadow_ground(triangle,NULL,3,&material,&bindings);
    assert(alpha_command_count==6);verify_alpha_footer(2);
    material.secondary=NULL;shadow_test_mode=3;bindings=1;
    assert(animaltown_rim_enable(100));
    const float normal[]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
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
    puts("Shadow/rim GX: signed-wide alpha, low-byte wrap and exact sea cutout survive appended stages");
    puts("Model lights GX: scope/day gates, nearest two, view transform, diffuse attenuation and alpha preservation passed");
    water_shadow_color_checks();
}
