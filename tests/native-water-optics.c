/* Record the production GX commands, then evaluate their independent RGB and
 * alpha arithmetic. The SDK copy function is extracted verbatim by the runner.
 * This is a logic/command fixture, not a substitute for a console image test. */
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "dnd_draw.h"
#include "dnd_full_aa.h"
typedef uint8_t u8;typedef uint16_t u16;typedef uint32_t u32;typedef float f32;
typedef float Mtx[3][4];
typedef struct {u8 r,g,b,a;} GXColor;
typedef struct {void *data;int width,height,format,s,t,min,mag;} GXTexObj;
enum {
    GX_FALSE=0,GX_TRUE=1,GX_TF_I4=0,GX_TF_I8=1,GX_TF_IA4=2,GX_TF_IA8=3,GX_TF_RGB565=4,
    GX_NEAR=0,GX_LINEAR=1,GX_CLAMP=0,GX_REPEAT=1,GX_ANISO_1=0,
    GX_TEXMAP0=0,GX_TEXMAP1=1,GX_TEXMAP2=2,GX_TEXMAP3=3,GX_TEXMAP4=4,GX_TEXMAP5=5,GX_TEXMAP6=6,
    GX_TEXCOORD0=0,GX_TEXCOORD1=1,GX_TEXCOORD2=2,GX_TEXCOORD3=3,GX_TEXCOORD4=4,GX_TEXCOORD5=5,GX_TEXCOORD6=6,
    GX_TEXCOORD_NULL=255,GX_TEXCOORDNULL=255,GX_TEXMAP_NULL=255,GX_TEXMAP_DISABLE=256,GX_COLOR_NULL=255,GX_COLOR0A0=4,
    GX_TEXMTX0=30,GX_TEXMTX1=33,GX_TEXMTX2=36,GX_TEXMTX3=39,GX_TEXMTX4=42,GX_TEXMTX5=45,GX_TEXMTX6=48,GX_TEXMTX7=51,GX_TEXMTX8=54,
    GX_TG_MTX2x4=1,GX_TG_MTX3x4=0,GX_MTX2x4=1,GX_MTX3x4=0,GX_TG_TEX0=4,GX_TG_TEX1=5,GX_TG_POS=0,GX_IDENTITY=60,
    GX_CH_RED=0,GX_CH_GREEN=1,GX_CH_BLUE=2,GX_CH_ALPHA=3,GX_TEV_SWAP0=0,GX_TEV_SWAP1=1,GX_TEV_SWAP2=2,GX_TEV_SWAP3=3,
    GX_TEV_ADD=0,GX_TEV_SUB=1,GX_TEV_COMP_R8_GT=8,GX_TEV_COMP_BGR24_EQ=13,GX_TEV_COMP_A8_GT=14,GX_TEV_COMP_A8_EQ=15,
    GX_TB_ZERO=0,GX_CS_SCALE_1=0,GX_CS_SCALE_2=1,GX_CS_SCALE_4=2,GX_CS_DIVIDE_2=3,
    GX_TEVPREV=0,GX_TEVREG0=1,GX_TEVREG1=2,GX_TEVREG2=3,GX_KCOLOR0=0,GX_KCOLOR1=1,GX_KCOLOR2=2,GX_KCOLOR3=3,
    GX_TEV_KCSEL_1=0,GX_TEV_KCSEL_K0=12,GX_TEV_KCSEL_K1=13,GX_TEV_KCSEL_K2=14,GX_TEV_KCSEL_K3=15,
    GX_TEV_KCSEL_K0_A=28,GX_TEV_KASEL_1=0,GX_TEV_KASEL_K0_A=28,GX_TEV_KASEL_K1_A=29,GX_TEV_KASEL_K2_A=30,
    GX_TEV_KASEL_K0_G=20,GX_TEV_KASEL_3_4=2,
    GX_CC_CPREV=0,GX_CC_APREV=1,GX_CC_C0=2,GX_CC_A0=3,GX_CC_C1=4,GX_CC_A1=5,GX_CC_C2=6,GX_CC_A2=7,
    GX_CC_TEXC=8,GX_CC_TEXA=9,GX_CC_RASC=10,GX_CC_RASA=11,GX_CC_ONE=12,GX_CC_HALF=13,GX_CC_KONST=14,GX_CC_ZERO=15,
    GX_CA_APREV=0,GX_CA_A0=1,GX_CA_A1=2,GX_CA_A2=3,GX_CA_TEXA=4,GX_CA_RASA=5,GX_CA_KONST=6,GX_CA_ZERO=7,
    GX_INDTEXSTAGE0=0,GX_INDTEXSTAGE1=1,GX_INDTEXSTAGE2=2,GX_ITS_1=0,GX_ITF_8=0,GX_ITB_ST=3,GX_ITB_TU=6,
    GX_ITM_0=1,GX_ITM_1=2,GX_ITM_2=3,GX_ITW_OFF=0,GX_ITW_0=6,GX_ITBA_OFF=0,
    GX_GREATER=4,GX_EQUAL=2,GX_ALWAYS=7,GX_AOP_AND=0,GX_BM_NONE=0,GX_BM_BLEND=1,GX_BL_ZERO=0,GX_BL_ONE=1,GX_BL_SRCALPHA=4,GX_BL_INVSRCALPHA=5,GX_LO_CLEAR=0,
    GX_CLAMP_TOP=1,GX_CLAMP_BOTTOM=2
};
typedef struct {int input[4],op,bias,scale,clamp,dest,konst;} Command;
typedef struct {Command rgb,alpha;int coord,map,channel,swap,indirect,bias,matrix,wrap_s,wrap_t,add;} Stage;
static Stage stages[16];static GXColor initial[4],konst[4];static int stage_count,indirect_count,texgen_count;
static int swaps[4][4],texgens[8][3],indorders[3][2],blend[4],alpha_compare[5];
static Mtx matrices[10];static float indmatrices[3][2][3];static int indexponents[3];static GXTexObj bound[8];
static void GX_SetNumTevStages(u8 n){assert(n&&n<=16);stage_count=n;}
static void GX_SetNumIndStages(u8 n){assert(n<=4);indirect_count=n;}
static void GX_SetNumTexGens(u8 n){assert(n<=8);texgen_count=n;}
static void GX_SetTevDirect(u8 s){assert(s<16);stages[s].indirect=-1;}
static void GX_SetTevSwapMode(u8 s,int raster,int tex){assert(s<16&&raster==0);stages[s].swap=tex;}
static void GX_SetTevSwapModeTable(int s,int r,int g,int b,int a){swaps[s][0]=r;swaps[s][1]=g;swaps[s][2]=b;swaps[s][3]=a;}
static void GX_SetTevOrder(u8 s,u8 coord,u32 map,int channel){assert(s<16);stages[s].coord=coord;stages[s].map=(int)map;stages[s].channel=channel;}
static void inputs(Command *c,int a,int b,int weight,int d){c->input[0]=a;c->input[1]=b;c->input[2]=weight;c->input[3]=d;}
static void operation(Command *c,int op,int bias,int scale,int clamp,int dest){c->op=op;c->bias=bias;c->scale=scale;c->clamp=clamp;c->dest=dest;}
static void GX_SetTevColorIn(u8 s,int a,int b,int c,int d){inputs(&stages[s].rgb,a,b,c,d);}
static void GX_SetTevAlphaIn(u8 s,int a,int b,int c,int d){inputs(&stages[s].alpha,a,b,c,d);}
static void GX_SetTevColorOp(u8 s,int op,int bias,int scale,int clamp,int dest){operation(&stages[s].rgb,op,bias,scale,clamp,dest);}
static void GX_SetTevAlphaOp(u8 s,int op,int bias,int scale,int clamp,int dest){operation(&stages[s].alpha,op,bias,scale,clamp,dest);}
static void GX_SetTevKColorSel(u8 s,int v){stages[s].rgb.konst=v;}
static void GX_SetTevKAlphaSel(u8 s,int v){stages[s].alpha.konst=v;}
static void GX_SetTevColor(int slot,GXColor value){initial[slot]=value;}
static void GX_SetTevKColor(int slot,GXColor value){konst[slot]=value;}
static void GX_SetTevIndirect(u8 s,int i,int format,int bias,int matrix,int ws,int wt,int add,int lod,int alpha){
    assert(format==GX_ITF_8&&!lod&&alpha==GX_ITBA_OFF);Stage *p=&stages[s];p->indirect=i;p->bias=bias;p->matrix=matrix;p->wrap_s=ws;p->wrap_t=wt;p->add=add;
}
static void GX_SetTevIndWarp(u8 s,int i,int signed_offsets,int replace,int matrix){
    (void)s;(void)i;(void)signed_offsets;(void)replace;(void)matrix;assert(!"Unexpected legacy indirect preset");
}
static void GX_SetIndTexMatrix(int slot,float m[2][3],int exponent){assert(slot>=1&&slot<=3);memcpy(indmatrices[slot-1],m,sizeof indmatrices[0]);indexponents[slot-1]=exponent;}
static void GX_SetIndTexOrder(int i,int coord,int map){indorders[i][0]=coord;indorders[i][1]=map;}
static void GX_SetIndTexCoordScale(int i,int s,int t){assert(i<3&&s==0&&t==0);}
static void GX_LoadTexMtxImm(Mtx m,u32 slot,int kind){assert(slot>=30&&slot<=57&&(kind==0||kind==1));memcpy(matrices[(slot-30)/3],m,sizeof(Mtx));}
static void GX_SetTexCoordGen(int coord,int kind,int source,int matrix){texgens[coord][0]=kind;texgens[coord][1]=source;texgens[coord][2]=matrix;}
static void GX_SetTexCoordScaleManually(int coord,int manual,int s,int t){assert(coord<8&&!manual&&s==1&&t==1);}
static void GX_SetTexCoordBias(int coord,int s,int t){assert(coord<8&&!s&&!t);}
static void GX_SetAlphaCompare(int a,int r,int operation,int b,int t){alpha_compare[0]=a;alpha_compare[1]=r;alpha_compare[2]=operation;alpha_compare[3]=b;alpha_compare[4]=t;}
static void GX_SetBlendMode(int mode,int source,int destination,int logic){blend[0]=mode;blend[1]=source;blend[2]=destination;blend[3]=logic;}
static void GX_InitTexObj(GXTexObj *o,void *data,u16 w,u16 h,int format,int s,int t,int mip){assert(!mip);*o=(GXTexObj){data,w,h,format,s,t,0,0};}
static void GX_InitTexObjLOD(GXTexObj *o,int min,int mag,float low,float high,float bias,int clamp,int edge,int aniso){(void)low;(void)high;(void)bias;(void)clamp;(void)edge;(void)aniso;o->min=min;o->mag=mag;}
static void GX_LoadTexObj(GXTexObj *o,int map){assert(map>=0&&map<8);bound[map]=*o;}
static unsigned GX_GetTexBufferSize(int w,int h,int format,int mip,int level){(void)format;assert(!mip&&!level);return (unsigned)(w*h*2);}
static void DCFlushRange(void *p,unsigned bytes){(void)p;(void)bytes;}

static int active,pass=1,raw,width=640,height=480,invalidations,allocations,fail_allocate,allocated_bytes;
static DndDrawTexture resources[8];
bool dnd_device_active(void){return active!=0;}int dnd_device_pass(void){return pass;}int dnd_raw_depth(void){return raw;}
int dnd_device_width(void){return width;}int dnd_device_height(void){return height;}
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
void dnd_draw_backend_flush(void *p,int bytes,int invalidate){assert(p&&bytes>0&&invalidate==2);}
int dnd_texture_allocate_native(int w,int h,int format,int linear){
    assert(!active&&!raw&&linear==1);allocations++;if(fail_allocate)return 0;
    for(int i=1;i<8;i++)if(!resources[i].data){unsigned bytes=(unsigned)(w*h*2);void *p=calloc(1,bytes);assert(p);
        resources[i]=(DndDrawTexture){.data=p,.bytes=bytes,.width=w,.height=h,.format=format,.linear=true,.levels=1};allocated_bytes+=(int)bytes;return i;}
    assert(!"Texture slot exhaustion");return 0;
}
int dnd_texture_update_native(int token,DndArray *a,int offset,int count){assert(!active&&token>0&&token<8&&offset==0&&a->length==(unsigned)count);memcpy((void*)resources[token].data,a->data,(size_t)count);return 0;}
int dnd_texture_resolve(int token,DndDrawTexture *out){if(token<=0||token>=8||!resources[token].data)return -1;*out=resources[token];return 0;}
int dnd_draw_release(int token){if(token<=0||token>=8||!resources[token].data)return -1;allocated_bytes-=(int)resources[token].bytes;free((void*)resources[token].data);memset(&resources[token],0,sizeof resources[token]);return 0;}
static int display_aa_mode,aa_part,copy_source[4],copy_destination[4],copy_clamp,copy_clear,copy_calls;
static void *copy_address;static char copy_order[16];static int copy_events;
static void event(char c){assert(copy_events<15);copy_order[copy_events++]=c;copy_order[copy_events]=0;}
static void GX_SetCopyClamp(int clamp){copy_clamp=clamp;}
static void GX_SetTexCopySrc(u16 x,u16 y,u16 w,u16 h){copy_source[0]=x;copy_source[1]=y;copy_source[2]=w;copy_source[3]=h;event('S');}
static void GX_SetTexCopyDst(u16 w,u16 h,int format,int half){copy_destination[0]=w;copy_destination[1]=h;copy_destination[2]=format;copy_destination[3]=half;event('D');}
static void GX_CopyTex(void *destination,int clear){assert(active&&pass==1);copy_address=destination;copy_clear=clear;copy_calls++;event('C');}
static void GX_PixModeSync(void){event('P');}static void GX_DrawDone(void){event('F');}static void GX_InvalidateTexAll(void){event('I');}
#include "water-copy-fixture.inc"

static int shadow_mode,environment_rgb=0xffffff;float animaltown_mip_x,animaltown_mip_z,animaltown_mip_radius;
int animaltown_shadows_mode(void){return shadow_mode;}int animaltown_environment_color(void){return environment_rgb;}
int animaltown_light_strength(void){return 120;}int animaltown_shadow_strength(void){return 255;}
const uint8_t *animaltown_path_mask(void){return NULL;}const uint8_t *animaltown_snow_mask(void){return NULL;}
const uint8_t *animaltown_snow_mask_at(float x,float z,int whole){(void)x;(void)z;(void)whole;return animaltown_snow_mask();}
int animaltown_snow_layout(int *first){*first=0;return 32;}
const uint8_t *animaltown_shadows_page(float x,float z,float *origin){static const uint8_t pixels[1024];(void)x;(void)z;float page[6]={0,0,8,32,8,32};memcpy(origin,page,sizeof page);return pixels;}
static float view_matrix[16],projection_matrix[16];
static void identity(float *m){memset(m,0,16*sizeof(float));m[0]=m[5]=m[10]=m[15]=1;}
static void multiply(float *out,const float *a,const float *b){float m[16];for(int c=0;c<4;c++)for(int r=0;r<4;r++){m[c*4+r]=0;for(int k=0;k<4;k++)m[c*4+r]+=a[k*4+r]*b[c*4+k];}memcpy(out,m,sizeof m);}
#include "dnd_tev_backend.inc"
#include "../tools/native-shadow-union-gx.inc"
#include "../tools/native-water-optics.inc"

static DndArray *array(int count,int size){DndArray *a=calloc(1,sizeof(DndArray)+(size_t)count*size);assert(a);a->length=(unsigned)count;a->element_size=(unsigned)size;return a;}
static DndEffectAlpha effect_alpha;static DndDrawEffect effect;static DndDrawMaterial material;static DndDrawTexture distortion={.width=64,.height=64,.format=4};
static float world[16];static DndArray *settings;
static void make_effect(void){
    memset(&effect,0,sizeof effect);memset(&effect_alpha,0,sizeof effect_alpha);
    effect.count=4;effect.uniforms=2;effect.texture=3;effect.indirect_strength=24;effect.colors[0]=0x367f91ff;effect.colors[1]=0xd3f2ecff;effect.alpha=&effect_alpha;
    effect.stages[0]=(DndTevStage){15,15,15,10,0,0,12,0};effect.stages[1]=(DndTevStage){15,8,13,15,0,1,12,0};
    effect.stages[2]=(DndTevStage){0,14,2,15,0,0,12,0};effect.stages[3]=(DndTevStage){0,14,8,15,0,0,13,2};
    for(int i=0;i<4;i++)effect_alpha.stages[i]=(DndTevAlphaStage){7,7,7,5,0,192,0,0};
    effect_alpha.indirect_enabled=1;effect_alpha.indirect_bias=GX_ITB_TU;effect_alpha.indirect_exponent=-1;effect_alpha.indirect_matrix[1]=effect_alpha.indirect_matrix[5]=.035f;
    material=(DndDrawMaterial){.color=0xffffffff,.flags=1|8,.effect=&effect,.secondary=&distortion};
}
static void record_draw(int optics){
    unsigned bindings=0;DndDrawVertex triangle[3]={{.u=0,.v=0,.color=144},{.u=1,.v=0,.color=144},{.u=0,.v=1,.color=144}};
    dnd_draw_backend_tev(&effect,true);GX_SetNumTexGens(2);GX_SetBlendMode(GX_BM_BLEND,GX_BL_SRCALPHA,GX_BL_INVSRCALPHA,0);
    town_shadow_ground(triangle,NULL,3,NULL,&material,&bindings);if(optics)town_water_optics_apply(&material,world,&bindings);
}
static int channel(GXColor c,int n){return n==0?c.r:n==1?c.g:n==2?c.b:c.a;}
static int clamp_byte(int v){return v<0?0:v>255?255:v;}
static int mix(int a,int b,int c,int d){int w=c+(c>>7);return clamp_byte(d+((a*(256-w)+b*w+128)>>8));}
typedef struct {int raster[4],ripple[2],lookup[4],warped[4],unwarped[4],guard[4],shadow,light;} Pixel;
static void texel(const Stage *s,const Pixel *p,int tex[4]){
    memset(tex,0,4*sizeof(int));
    if(s->map==0)for(int i=0;i<4;i++)tex[i]=p->ripple[s->coord==1];
    if(s->map==2){tex[0]=tex[1]=tex[2]=p->shadow;tex[3]=p->light;}
    if(s->map==4)memcpy(tex,p->lookup,4*sizeof(int));
    if(s->map==5)memcpy(tex,s->indirect==0?p->warped:p->unwarped,4*sizeof(int));
    if(s->map==6)memcpy(tex,p->guard,4*sizeof(int));
    int old[4];memcpy(old,tex,sizeof old);for(int i=0;i<4;i++)tex[i]=old[swaps[s->swap][i]];
}
static int constant(int selector,int c,int alpha){
    if(selector==0)return 255;
    if(selector>=28&&selector<=31)return channel(konst[selector-28],3);
    if(!alpha&&selector>=12&&selector<=15)return channel(konst[selector-12],c);
    assert(!"Unsupported fixture constant");return 0;
}
static int operand(int source,int c,int alpha,int registers[4][4],const int tex[4],const Pixel *p,int selector){
    if(alpha){if(source<4)return registers[source][3];if(source==4)return tex[3];if(source==5)return p->raster[3];if(source==6)return constant(selector,3,1);return 0;}
    if(source<8)return registers[source/2][source&1?3:c];
    if(source==8)return tex[c];
    if(source==9)return tex[3];
    if(source==10)return p->raster[c];
    if(source==11)return p->raster[3];
    if(source==12)return 255;
    if(source==13)return 128;
    if(source==14)return constant(selector,c,0);
    return 0;
}
static int evaluate(const Command *command,int c,int alpha,int registers[4][4],const int tex[4],const Pixel *p){
    int v[4];for(int i=0;i<4;i++)v[i]=operand(command->input[i],c,alpha,registers,tex,p,command->konst);
    int a=v[0]&255,b=v[1]&255,w=(v[2]&255)+((v[2]&255)>>7),value;
    if(command->op==GX_TEV_COMP_BGR24_EQ){
        int equal=1;for(int n=0;n<3;n++)if((operand(command->input[0],n,0,registers,tex,p,command->konst)&255)!=(operand(command->input[1],n,0,registers,tex,p,command->konst)&255))equal=0;
        value=v[3]+(equal?(v[2]&255):0);
    }else{assert(command->op==GX_TEV_ADD||command->op==GX_TEV_SUB);int product=(a*(256-w)+b*w+128)>>8;value=v[3]+(command->op==GX_TEV_SUB?-product:product);}
    assert(command->bias==0);value=command->scale==3?value/2:value*(1<<command->scale);
    return command->clamp?clamp_byte(value):value;
}
static void run_stages(const Pixel *p,int first,int end,int registers[4][4]){
    for(int s=first;s<end;s++){int tex[4],out[4];texel(&stages[s],p,tex);for(int c=0;c<3;c++)out[c]=evaluate(&stages[s].rgb,c,0,registers,tex,p);out[3]=evaluate(&stages[s].alpha,3,1,registers,tex,p);
        for(int c=0;c<3;c++)registers[stages[s].rgb.dest][c]=out[c];
        registers[stages[s].alpha.dest][3]=out[3];}
}
static void registers_init(int registers[4][4]){for(int r=0;r<4;r++)for(int c=0;c<4;c++)registers[r][c]=channel(initial[r],c);}
static void composition_checks(void){
    const int opacity[]={0,64,96,97,120,144,255};
    for(int legacy=0;legacy<2;legacy++)for(int night=0;night<2;night++){
        shadow_mode=legacy?3:0;environment_rgb=night?0x52708e:0xffffff;record_draw(1);
        int base=legacy?8:night?5:4;assert(stage_count==base+8&&indirect_count==3&&texgen_count==7);
        assert(stages[base].map==256&&texgens[2][2]==(legacy?54:60));
        assert(konst[0].r==54&&konst[0].g==127&&konst[0].b==145&&konst[1].r==211&&konst[1].g==242&&konst[1].b==236);
        assert(indmatrices[0][0][1]==.035f&&indexponents[0]==-1);if(legacy)assert(initial[2].r==220*120/255);
        for(unsigned a=0;a<sizeof opacity/sizeof opacity[0];a++)for(int valid=0;valid<2;valid++){
            Pixel p={{75,154,187,opacity[a]},{52,83},{180,180,180,107},{29,74,102,255},{61,92,116,255},{29,74,102,255},113,89};
            GXColor clear=town_optics_capture_clear();if(!valid)for(int c=0;c<3;c++)p.guard[c]=channel(clear,c);
            int registers[4][4];registers_init(registers);run_stages(&p,0,base,registers);assert(registers[1][3]==96);
            int expected[3];for(int c=0;c<3;c++){
                int lit=mix(0,p.lookup[c],konst[0].a,registers[0][c]);lit=mix(lit,channel(clear,c),mix(0,p.lookup[3],konst[1].a,0),0);
                int weight=valid?clamp_byte(4*(p.raster[3]-96)):0;int background=mix(p.unwarped[c],p.warped[c],weight,0);
                expected[c]=mix(background,lit,p.raster[3],0);
            }
            run_stages(&p,base,stage_count,registers);for(int c=0;c<3;c++)assert(registers[0][c]==expected[c]);assert(registers[0][3]==255);
        }
    }
    shadow_mode=0;environment_rgb=0xffffff;
}
static void projection_checks(void){
    identity(view_matrix);view_matrix[14]=-6;identity(world);world[12]=.7f;world[14]=.4f;
    memset(projection_matrix,0,sizeof projection_matrix);projection_matrix[0]=2;projection_matrix[5]=3;projection_matrix[10]=-1;projection_matrix[11]=-1;projection_matrix[14]=-.2f;
    record_draw(1);Mtx *screen=&matrices[5];float x=1,y=.5f,z=0,w=5.6f;
    float q=(*screen)[2][0]*x+(*screen)[2][1]*y+(*screen)[2][2]*z+(*screen)[2][3];
    float s=((*screen)[0][0]*x+(*screen)[0][1]*y+(*screen)[0][2]*z+(*screen)[0][3])/q;
    float t=((*screen)[1][0]*x+(*screen)[1][1]*y+(*screen)[1][2]*z+(*screen)[1][3])/q;
    assert(fabsf(q-w)<1e-6f&&fabsf(s-(.5f+1.7f/w))<1e-6f&&fabsf(t-(.5f-.75f/w))<1e-6f);
    projection_matrix[0]=1.5f;record_draw(1);s=((*screen)[0][0]*x+(*screen)[0][3])/q;assert(fabsf(s-(.5f+1.275f/w))<1e-6f);
    assert(texgens[6][0]==GX_TG_MTX3x4&&texgens[6][1]==GX_TG_POS&&texgens[6][2]==GX_TEXMTX5);
    assert(fabsf(matrices[4][1][3]-(.5f+(.4f-2.f)/106.f))<1e-6f);
    identity(world);identity(view_matrix);identity(projection_matrix);
}
static void controls_checks(void){
    float *p=(float*)settings->data;
    active=0;p[8]=0;p[9]=0;p[10]=0;p[11]=0;assert(animaltown_water_prepare(settings,1,255,0xffffff)==3);
    active=1;animaltown_water_begin();copy_events=0;animaltown_water_capture();animaltown_water_scope(2);record_draw(1);
    assert(indmatrices[1][0][0]==0&&indmatrices[2][0][1]==0&&konst[0].a==0&&konst[1].a==0);
    p[8]=.14f;p[9]=1.5f;p[10]=.18f;p[11]=.1f;
    for(int sun=0;sun<=255;sun+=255){active=0;animaltown_water_prepare(settings,1,sun,0x123456);active=1;animaltown_water_begin();copy_events=0;animaltown_water_capture();animaltown_water_scope(2);record_draw(1);
        assert(konst[0].a==(sun?46:0)&&konst[1].a==26);assert(animaltown_water_stats(19)==sun&&animaltown_water_stats(20)==0x123456);
        /* Signed128 is exact zero; both independent slope samples add linearly.
         * A one-byte change stays bounded and uses the same matrix, never the
         * earlier surface matrix or independent refraction matrix. */
        for(int sample=0;sample<2;sample++)for(int byte=0;byte<256;byte++){
            float value=(byte-128)*indmatrices[1][sample][sample];assert(fabsf(value-(byte-128)*.035f)<1e-6f);
        }
        assert(indorders[1][0]==3&&indorders[2][0]==4&&indorders[1][1]==3&&indorders[2][1]==3);
        assert(stages[4].wrap_s==GX_ITW_0&&!stages[4].add&&stages[5].add);
        assert(fabsf(indmatrices[2][0][1]*128*2-1.5f)<1e-6f&&indmatrices[2][0][0]==0);
    }
}
static void lifetime_checks(void){
    active=0;animaltown_water_prepare(settings,0,0,0);assert(allocated_bytes==16384);int before=allocations;
    fail_allocate=1;assert(animaltown_water_prepare(settings,1,255,0xffffff)==1);assert(allocations==before+1);
    for(int i=0;i<20;i++)assert(animaltown_water_prepare(settings,1,255,0xffffff)==1);
    assert(allocations==before+1);
    active=1;animaltown_water_begin();animaltown_water_scope(2);record_draw(1);assert(stage_count==7&&indirect_count==3&&texgen_count==6&&blend[0]==GX_BM_BLEND&&animaltown_water_stats(16)==3);
    Pixel p={{75,154,187,120},{52,83},{180,180,180,107},{0},{0},{0},0,0};int registers[4][4];registers_init(registers);run_stages(&p,0,stage_count,registers);assert(registers[0][3]==120);
    int cache=invalidations;animaltown_water_scope(0);assert(invalidations>cache);dnd_draw_backend_tev(NULL,true);assert(stage_count==1&&indirect_count==0&&stages[0].indirect==-1);
    active=0;animaltown_water_prepare(settings,0,0,0);fail_allocate=0;assert(animaltown_water_prepare(settings,1,255,0xffffff)==3);assert(allocations==before+2);
    width=640;height=528;assert(animaltown_water_prepare(settings,1,255,0xffffff)==3);assert(animaltown_water_stats(9)==264&&allocated_bytes==185344);
    width=641;assert(animaltown_water_prepare(settings,1,255,0xffffff)==1);assert(allocated_bytes==16384);
    width=640;height=480;assert(animaltown_water_prepare(settings,1,255,0xffffff)==3);
}
static void capture_checks(void){
    active=1;animaltown_water_begin();copy_events=0;assert(animaltown_water_capture()==1);assert(!strcmp(copy_order,"SDCPFI")&&!copy_clear);
    assert(copy_source[2]==640&&copy_source[3]==480&&copy_destination[0]==320&&copy_destination[1]==240&&copy_destination[2]==4&&copy_destination[3]==1);
    int copies=copy_calls;assert(animaltown_water_capture()==1&&copy_calls==copies);
    assert(animaltown_water_stats(1)&&animaltown_water_stats(14)==animaltown_water_stats(15));
    animaltown_water_begin();assert(!animaltown_water_stats(1));animaltown_water_scope(2);record_draw(1);assert(stage_count==7&&blend[0]==1);
    display_aa_mode=2;void *base=(void*)resources[town_optics_capture].data;
    for(aa_part=0;aa_part<2;aa_part++){
        animaltown_water_begin();copy_events=0;assert(animaltown_water_capture()==1&&!strcmp(copy_order,"SDCPFI"));
        assert(copy_source[3]==248&&copy_destination[1]==124&&copy_clamp==(aa_part?2:1));
        assert(copy_address==(unsigned char*)base+(aa_part?116*320*2:0));
        /* The displayed half is separated from the stale band by8 source
         * rows; worst four-pixel warp plus two-pixel filtering fits. */
        int shown_edge=aa_part?240:239,resident_edge=aa_part?232:247;assert(abs(shown_edge-resident_edge)>=8);
    }
    display_aa_mode=0;pass=2;assert(!animaltown_water_capture()&&!animaltown_water_stats(1));pass=1;
}
int main(void){
    identity(world);identity(view_matrix);identity(projection_matrix);make_effect();
    settings=array(12,4);float values[12]={2,53,.2f,.3f,2.2f,.4f,.5f,3.1f,.14f,1.5f,.18f,.1f};memcpy(settings->data,values,sizeof values);
    DndArray *slopes=array(8192,1);memset(slopes->data,128,8192);
    active=1;assert(!animaltown_water_initialize(slopes));active=0;assert(animaltown_water_initialize(slopes)==1&&allocated_bytes==16384);
    assert(animaltown_water_prepare(settings,1,255,0xffffff)==3&&allocated_bytes==169984);
    active=1;animaltown_water_begin();animaltown_water_scope(1);record_draw(1);assert(alpha_compare[0]==GX_GREATER&&alpha_compare[1]==0&&stages[3].alpha.input[3]==GX_CA_RASA);
    copy_events=0;assert(animaltown_water_capture()==1);animaltown_water_scope(2);town_water_optics_clear(0x95c6baffu);record_draw(1);
    assert(bound[3].format==3&&bound[4].format==3&&bound[5].format==4&&bound[6].data==bound[5].data&&bound[5].min==GX_LINEAR&&bound[6].min==GX_NEAR);
    assert(bound[3].s==GX_REPEAT&&bound[4].s==GX_CLAMP&&blend[0]==GX_BM_NONE);
    composition_checks();projection_checks();controls_checks();lifetime_checks();capture_checks();
    active=0;assert(animaltown_water_shutdown()==1&&allocated_bytes==0&&animaltown_water_stats(0)==0);free(slopes);free(settings);
    puts("Water optics: actual base/footer TEV evaluation, exact alpha composition, shoreline/clear guard, neutral/gain matrices, projection/aspect, wet-rock mask and state invalidation passed");
    puts("Water capture: pinned no-clear GX copy/sync/cache order, split-AA bands/freshness, accounted allocations, refusal retry policy, resize and disposal passed");
    return 0;
}
