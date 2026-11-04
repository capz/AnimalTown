/* Game-owned diagnostic hook for imported villagers/buildings only. Included
 * after the pinned SDK model renderer; the SDK checkout remains unmodified. */
#include "animaltown_uv_topology.h"
typedef struct {int token;unsigned meshes;TownUvTopology *mesh;} TownUvCache;
static TownUvCache town_uv_cache[DND_MODEL_SLOTS];
static int town_uv_flags;
static void town_uv_clear(void){for(unsigned i=0;i<DND_MODEL_SLOTS;i++){TownUvCache *c=&town_uv_cache[i];for(unsigned j=0;j<c->meshes;j++)town_uv_free(&c->mesh[j]);free(c->mesh);memset(c,0,sizeof(*c));}}
int animaltown_uv_mode(int flags){
  if(flags<0||flags>3||dnd_device_active()||dnd_model_frame_active)return -1;
  if(!flags){town_uv_flags=0;town_uv_clear();return 0;}
  if(!town_uv_flags){unsigned bytes=0;
    for(unsigned i=0;i<DND_MODEL_SLOTS;i++){DndModel *m=dnd_models[i];if(!m||!m->ready)continue;TownUvCache *c=&town_uv_cache[i];
      if(m->meshes>4096)goto fail;
      c->mesh=calloc(m->meshes,sizeof(*c->mesh));if(!c->mesh)goto fail;c->meshes=m->meshes;c->token=m->token;
      for(unsigned j=0;j<m->meshes;j++){DndMesh *mesh=&m->mesh[j];if(bytes>1024*1024||mesh->indices>75000)goto fail;
        if(town_uv_build(mesh->data,mesh->vertices,mesh->index,mesh->indices,&c->mesh[j])<0)goto fail;
        bytes+=mesh->indices/3*4+mesh->indices*8;if(bytes>1024*1024)goto fail;
      }
    }
  }
  town_uv_flags=flags;return flags;
fail:town_uv_clear();return -1;
}
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
static void town_uv_position(DndInstance *in,unsigned mi,unsigned index,float offset){
  const DndMesh *m=&in->model->mesh[mi];const unsigned char *v=m->data+index*80;float p[6];
  if(m->skin>=0)memcpy(p,in->vertices[mi]+index*6,24);else for(unsigned k=0;k<6;k++)p[k]=dnd_model_f32(v+k*4);
  GX_Position3f32(p[0]+p[3]*offset,p[1]+p[4]*offset,p[2]+p[5]*offset);
}
static GXColor town_uv_color(unsigned island,unsigned mesh){
  uint32_t h=(island+1)*0x9e3779b9u+mesh*0x85ebca6bu;h^=h>>16;h*=0x7feb352du;h^=h>>15;
  return (GXColor){(u8)(72+(h&127)),(u8)(72+((h>>8)&127)),(u8)(72+((h>>16)&127)),255};
}
static void town_uv_draw(DndInstance *in,const float world[16],int filter,int only,bool lines){
  DndModel *model=in->model;TownUvCache *cache=NULL;for(unsigned i=0;i<DND_MODEL_SLOTS;i++)if(town_uv_cache[i].token==model->token){cache=&town_uv_cache[i];break;}if(!cache)return;
  dnd_draw_backend_restore();dnd_draw_backend_mesh_changed();
  GX_SetNumChans(1);GX_SetNumTexGens(0);GX_SetNumTevStages(1);GX_SetTevOrder(GX_TEVSTAGE0,GX_TEXCOORDNULL,GX_TEXMAP_NULL,GX_COLOR0A0);GX_SetTevOp(GX_TEVSTAGE0,GX_PASSCLR);
  GX_SetChanCtrl(GX_COLOR0A0,GX_DISABLE,GX_SRC_REG,GX_SRC_VTX,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
  GX_SetAlphaCompare(GX_ALWAYS,0,GX_AOP_AND,GX_ALWAYS,0);GX_SetBlendMode(GX_BM_NONE,GX_BL_ONE,GX_BL_ZERO,GX_LO_CLEAR);GX_SetZMode(GX_TRUE,GX_LEQUAL,lines?GX_FALSE:GX_TRUE);
  GX_ClearVtxDesc();GX_SetVtxDesc(GX_VA_POS,GX_DIRECT);GX_SetVtxDesc(GX_VA_CLR0,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_POS,GX_POS_XYZ,GX_F32,0);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_CLR0,GX_CLR_RGBA,GX_RGBA8,0);if(lines)GX_SetLineWidth(6,GX_TO_ZERO);
  for(unsigned mi=0;mi<model->meshes;mi++){
    if(only>=0&&(unsigned)only!=mi)continue;
    DndMesh *mesh=&model->mesh[mi];DndMaterial *mat=&model->material[mesh->material];bool alpha=(mat->mode&4)!=0;
    if((filter==1&&alpha)||(filter==2&&!alpha)||(animated_mesh_visibility&&animated_mesh_visibility[mi]<.5f))continue;
    float object[16],mv[16];if(mesh->skin<0)dnd_model_multiply(object,world,in->global+(unsigned)mesh->node*16);else memcpy(object,world,64);dnd_model_multiply(mv,frame_view,object);
    Mtx matrix;for(int r=0;r<3;r++)for(int c=0;c<4;c++)matrix[r][c]=mv[c*4+r];GX_LoadPosMtxImm(matrix,GX_PNMTX0);GX_SetCurrentMtx(GX_PNMTX0);
    float determinant=object[0]*(object[5]*object[10]-object[9]*object[6])-object[4]*(object[1]*object[10]-object[9]*object[2])+object[8]*(object[1]*object[6]-object[5]*object[2]);GX_SetCullMode(mat->two?GX_CULL_NONE:determinant<0?GX_CULL_FRONT:GX_CULL_BACK);
    TownUvTopology *t=&cache->mesh[mi];unsigned total=lines?t->edges*2:mesh->indices;
    if(!lines)dnd_performance_add_triangles(mesh->indices/3);
    for(unsigned first=0;first<total;){unsigned count=total-first,limit=lines?65534:65535;if(count>limit)count=limit;GX_Begin(lines?GX_LINES:GX_TRIANGLES,GX_VTXFMT0,(u16)count);dnd_performance_add_draw(0);
      for(unsigned j=0;j<count;j++){unsigned n=first+j,index=lines?t->edge[n]:dnd_model_u32(mesh->index+n*4);GXColor c=lines?(GXColor){20,24,32,255}:town_uv_color(t->island[n/3],mi);town_uv_position(in,mi,index,lines?.001f:0);GX_Color4u8(c.r,c.g,c.b,c.a);}GX_End();first+=count;
    }
  }
  batch_model=NULL;batch_material=NULL;model_batch_vertex_ready=false;model_batch_light_ready=false;dnd_draw_backend_restore();dnd_draw_backend_mesh_changed();
}
#endif
static int draw_model_selected(DndInstance *in,const float world[16],const DndDrawEffect *effect,int filter,int only){
  if(!town_uv_flags)return draw_model_selected_base(in,world,effect,filter,only);
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
  int result=0;if(town_uv_flags&1)town_uv_draw(in,world,filter,only,false);else result=draw_model_selected_base(in,world,effect,filter,only);
  if(!result&&(town_uv_flags&2))town_uv_draw(in,world,filter,only,true);
  return result;
#else
  return draw_model_selected_base(in,world,effect,filter,only);
#endif
}
