/* Diagnostic geometry is replayed only when enabled. Opaque object draws
 * contribute their submitted triangles; frustum and horizon rejects never
 * reach these hooks. The replay reads the finished scene's depth buffer. */
typedef struct { float x,y,z; } TownDebugPoint;
typedef struct {
    const uint8_t *vertices,*indices;
    Mtx modelview;
    unsigned count,stride,index_size;
    int mirrored;
} TownDebugDraw;
enum { TOWN_DEBUG_DRAWS=768,TOWN_DEBUG_COPIED=8192,TOWN_DEBUG_LINES=384 };
typedef struct {
    TownDebugDraw draw[TOWN_DEBUG_DRAWS];
    TownDebugPoint copied[TOWN_DEBUG_COPIED];
    unsigned draws,points;
} TownDebugEdges;
static TownDebugEdges *town_debug_edges;
static int town_debug_storage,town_debug_collecting,town_debug_rim;
float animaltown_mip_x,animaltown_mip_z,animaltown_mip_radius;

int animaltown_diagnostics_view(int x,int z,int radius,int rim){
    animaltown_mip_x=x/1024.f;animaltown_mip_z=z/1024.f;
    animaltown_mip_radius=radius;town_debug_rim=rim!=0;
    return 1;
}
int animaltown_diagnostics_prepareedges(int enabled){
    if(!enabled){
        town_debug_collecting=0;
        if(town_debug_storage&&dnd_draw_release(town_debug_storage)<0)return 0;
        town_debug_storage=0;town_debug_edges=NULL;return 1;
    }
    if(town_debug_edges)return 1;
    int storage=dnd_buffer_create(sizeof(TownDebugEdges),32);
    if(!storage)return 0;
    town_debug_edges=(TownDebugEdges*)(uintptr_t)dnd_buffer_address(storage,0);
    if(!town_debug_edges){dnd_draw_release(storage);return 0;}
    town_debug_storage=storage;
    return 1;
}
static TownDebugPoint town_debug_position(const TownDebugDraw *draw,unsigned at){
    unsigned index=at;
    if(draw->index_size==2){uint16_t value;memcpy(&value,draw->indices+at*2,2);index=value;}
    if(draw->index_size==4){uint32_t value;memcpy(&value,draw->indices+at*4,4);index=value;}
    TownDebugPoint p;
    memcpy(&p,draw->vertices+index*draw->stride,sizeof(p));
    return p;
}
static TownDebugPoint town_debug_apply(const Mtx m,TownDebugPoint p){
    return (TownDebugPoint){
        m[0][0]*p.x+m[0][1]*p.y+m[0][2]*p.z+m[0][3],
        m[1][0]*p.x+m[1][1]*p.y+m[1][2]*p.z+m[1][3],
        m[2][0]*p.x+m[2][1]*p.y+m[2][2]*p.z+m[2][3]};
}
static float town_debug_dot(TownDebugPoint a,TownDebugPoint b){return a.x*b.x+a.y*b.y+a.z*b.z;}
static TownDebugPoint town_debug_normal(const TownDebugPoint p[3],int mirrored){
    TownDebugPoint a={p[2].x-p[0].x,p[2].y-p[0].y,p[2].z-p[0].z};
    TownDebugPoint b={p[1].x-p[0].x,p[1].y-p[0].y,p[1].z-p[0].z};
    float sign=mirrored?-1.f:1.f;
    return (TownDebugPoint){sign*(a.y*b.z-a.z*b.y),sign*(a.z*b.x-a.x*b.z),sign*(a.x*b.y-a.y*b.x)};
}
static int town_debug_mirrored(const float *w){
    return w[0]*(w[5]*w[10]-w[9]*w[6])-w[4]*(w[1]*w[10]-w[9]*w[2])+w[8]*(w[1]*w[6]-w[5]*w[2])<0;
}
static void town_debug_style(const Mtx matrix,int depth,int blend){
    dnd_draw_backend_restore();
    GX_LoadPosMtxImm((float (*)[4])matrix,GX_PNMTX0);GX_SetCurrentMtx(GX_PNMTX0);
    GX_SetZMode(GX_TRUE,(u8)depth,GX_FALSE);
    GX_SetBlendMode(blend?GX_BM_BLEND:GX_BM_NONE,GX_BL_SRCALPHA,GX_BL_INVSRCALPHA,GX_LO_CLEAR);
    GX_SetCullMode(GX_CULL_NONE);
    GX_SetAlphaCompare(GX_ALWAYS,0,GX_AOP_AND,GX_ALWAYS,0);
    GX_SetNumTexGens(0);GX_SetNumChans(1);GX_SetNumTevStages(1);
    GX_SetChanCtrl(GX_COLOR0A0,GX_DISABLE,GX_SRC_REG,GX_SRC_VTX,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_SetTevOrder(GX_TEVSTAGE0,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR0A0);
    GX_SetTevOp(GX_TEVSTAGE0,GX_PASSCLR);
    GX_ClearVtxDesc();GX_SetVtxDesc(GX_VA_POS,GX_DIRECT);GX_SetVtxDesc(GX_VA_CLR0,GX_DIRECT);
    GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_POS,GX_POS_XYZ,GX_F32,0);
    GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_CLR0,GX_CLR_RGBA,GX_RGBA8,0);
}
/* The ordinary indexed paths own stable vertices until EndFrame. Batched
 * immediate draws reuse staging, so only those copy positions into scratch. */
void animaltown_diagnostics_capture(const void *positions,unsigned stride,const void *indices,unsigned index_size,
        unsigned count,const float *view,int mirrored,int transient){
    if(!town_debug_collecting||count<3)return;
    TownDebugEdges *edges=town_debug_edges;
    if(edges->draws==TOWN_DEBUG_DRAWS)return;
    if(transient){
        if(count>TOWN_DEBUG_COPIED-edges->points)return;
        TownDebugPoint *next=edges->copied+edges->points;
        for(unsigned i=0;i<count;i++)memcpy(next+i,(const uint8_t*)positions+i*stride,sizeof(*next));
        edges->points+=count;positions=next;stride=sizeof(*next);
    }
    TownDebugDraw *draw=edges->draw+edges->draws++;
    *draw=(TownDebugDraw){positions,indices,{{0}},count,stride,index_size,mirrored};
    memcpy(draw->modelview,view,sizeof(Mtx));
}
static void town_debug_lines(const TownDebugPoint *points,unsigned count){
    GX_Begin(GX_LINES,GX_VTXFMT0,(u16)count);
    for(unsigned i=0;i<count;i++){
        TownDebugPoint p=points[i];
        /* Bring the edge slightly toward the eye without changing its
         * projected position. This suppresses depth rounding on its own face. */
        float bias=p.z<-.12f?1+.025f/p.z:1;
        GX_Position3f32(p.x*bias,p.y*bias,p.z*bias);GX_Color4u8(82,238,255,255);
    }
    GX_End();dnd_performance_add_draw(0);
}
int animaltown_diagnostics_edgepass(int begin){
    TownDebugEdges *edges=town_debug_edges;
    if(!edges)return 0;
    town_debug_collecting=begin!=0;
    if(begin){edges->draws=edges->points=0;return 1;}
    if(!edges->draws)return 1;
    const Mtx unit={{1,0,0,0},{0,1,0,0},{0,0,1,0}};
    town_debug_style(unit,GX_GREATER,0);
    GX_SetLineWidth(12,GX_TO_ZERO);
    TownDebugPoint points[TOWN_DEBUG_LINES];
    unsigned used=0;
    for(unsigned m=0;m<edges->draws;m++){
        const TownDebugDraw *draw=edges->draw+m;
        for(unsigned i=0;i+2<draw->count;i+=3){
            TownDebugPoint p[3];
            for(int j=0;j<3;j++)p[j]=town_debug_apply(draw->modelview,town_debug_position(draw,i+j));
            if(town_debug_dot(town_debug_normal(p,draw->mirrored),p[0])>=0)continue;
            for(int j=0;j<3;j++){points[used++]=p[j];points[used++]=p[(j+1)%3];}
            if(used==TOWN_DEBUG_LINES){town_debug_lines(points,used);used=0;}
        }
    }
    if(used)town_debug_lines(points,used);
    GX_SetLineWidth(6,GX_TO_ZERO);
    animaltown_shadows_model_reset();
    return 1;
}
static void town_debug_overlay(const DndDrawVertex *vertices,const uint16_t *indices,int count,const Mtx modelview){
    if(!town_debug_rim||count<3)return;
    int scope=animaltown_shadows_mode();
    int snow=scope==4||scope==5;
    if(scope!=1&&scope!=6&&!snow)return;
    town_debug_style(modelview,GX_LEQUAL,1);
    GX_SetCullMode(GX_CULL_BACK);
    unsigned bindings=0;
    if(snow){
        const uint8_t *depth=animaltown_snow_mask(),*paths=animaltown_path_mask();
        int stage=1;
        GX_SetNumTexGens(depth?7:paths?6:0);
        if(depth)town_snow_bind(depth,&bindings);
        GX_SetVtxDesc(GX_VA_TEX0,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_TEX0,GX_TEX_ST,GX_F32,0);
        GX_SetTevSwapModeTable(GX_TEV_SWAP1,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_RED);
        GX_SetTevSwapMode(GX_TEVSTAGE0,GX_TEV_SWAP1,GX_TEV_SWAP0);
        GX_SetTevColorIn(GX_TEVSTAGE0,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ONE);
        GX_SetTevColorOp(GX_TEVSTAGE0,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        GX_SetTevAlphaIn(GX_TEVSTAGE0,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO,GX_CA_RASA);
        GX_SetTevAlphaOp(GX_TEVSTAGE0,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG0);
        town_snow_cutout(stage++,depth!=NULL);
        if(paths)town_snow_paths(stage++,paths,&bindings);
        GX_SetNumTevStages((u8)(stage+1));GX_SetTevDirect((u8)stage);
        GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
        GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
        GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
        GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_A0,GX_CA_APREV,GX_CA_ZERO);
        GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        GX_SetAlphaCompare(GX_GREATER,0,GX_AOP_AND,GX_ALWAYS,0);
    }
    for(int start=0;start<count;){
        int n=count-start;if(n>65535)n=65535;
        GX_Begin(GX_TRIANGLES,GX_VTXFMT0,(u16)n);
        for(int i=start;i<start+n;i+=3){
            TownDebugPoint local[3],view[3];
            for(int j=0;j<3;j++){
                const DndDrawVertex *v=&vertices[indices?indices[i+j]:i+j];
                local[j]=(TownDebugPoint){v->x,v->y,v->z};
                view[j]=town_debug_apply(modelview,local[j]);
            }
            TownDebugPoint normal=town_debug_normal(view,0);
            float square=town_debug_dot(normal,normal);
            for(int j=0;j<3;j++){
                float cosine=square>0?-town_debug_dot(normal,view[j])/sqrtf(square*town_debug_dot(view[j],view[j])):1;
                /* Camera-facing is transparent. Grazing faces reach 70%. */
                u8 alpha=cosine<=0?179:cosine>=1?0:(u8)(179*(1-cosine)+.5f);
                GX_Position3f32(local[j].x,local[j].y,local[j].z);
                if(snow){
                    const DndDrawVertex *v=&vertices[indices?indices[i+j]:i+j];
                    GX_Color4u8(alpha,alpha,alpha,(u8)v->color);GX_TexCoord2f32(v->u,v->v);
                }else GX_Color4u8(255,255,255,alpha);
            }
        }
        GX_End();dnd_performance_add_draw(start==0?bindings:0);start+=n;
    }
}
