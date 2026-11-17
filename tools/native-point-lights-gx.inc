/* Imported models only. A second raster channel adds textured local diffuse
 * after the ambient/rim stages; ground draws never include this hook.
 * Two hardware lights, no textures, framebuffer copies or expanded meshes. */
#include "native-point-lights.h"
extern int animaltown_object_scope(void);
extern const float *animaltown_point_sources(int*);
static unsigned town_point_mask;
static bool town_points_active(void){return town_point_mask!=0;}
static void town_points_prepare(const float object[16],const float view[16]){
    town_point_mask=0;
    int scope=animaltown_object_scope(),gain=animaltown_light_strength();if(!scope||!gain)return;
    int count,selected[2];const float *points=animaltown_point_sources(&count);
    float local[3]={scope==2?1.5f:0,scope==2?1:.55f,scope==2?1.5f:0},center[3];
    town_point_transform(object,local,center);town_point_select(points,count,center,selected);
    for(int i=0;i<2;i++)if(selected[i]>=0){
        const float *p=points+selected[i]*5;float eye[3];town_point_transform(view,p,eye);
        int strength=(int)(p[4]*gain/255);unsigned id=i?GX_LIGHT7:GX_LIGHT6;
        GXLightObj light={0};GX_InitLightPos(&light,eye[0],eye[1],eye[2]);
        GX_InitLightColor(&light,(GXColor){strength,190*strength/255,100*strength/255,255});
        GX_InitLightAttn(&light,1,0,0,1,0,4/(p[3]*p[3]));GX_LoadLightObj(&light,id);town_point_mask|=id;
    }
}
static void town_point_channels(int source,GXColor tint){
    GX_SetNumChans(town_points_active()?2:1);if(!town_points_active())return;
    GX_SetChanAmbColor(GX_COLOR1A1,(GXColor){0,0,0,0});GX_SetChanMatColor(GX_COLOR1A1,tint);
    GX_SetChanCtrl(GX_COLOR1,GX_ENABLE,GX_SRC_REG,source,town_point_mask,GX_DF_CLAMP,GX_AF_SPOT);
    GX_SetChanCtrl(GX_ALPHA1,GX_DISABLE,GX_SRC_REG,source,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
}
static void town_point_tev(int textured,int stage){
    if(!town_points_active())return;
    GX_SetNumTevStages((u8)(stage+1));GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,textured?GX_TEXCOORD0:GX_TEXCOORD_NULL,textured?GX_TEXMAP0:GX_TEXMAP_NULL,GX_COLOR1A1);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,textured?GX_CC_TEXC:GX_CC_ONE,GX_CC_RASC,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage);
}
