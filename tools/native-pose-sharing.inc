/* Included in the copied model_animation.c. Native instances retain their own
 * clocks, local poses, node matrices and workspaces; only skin output is shared. */
#define TOWN_POSE_CAP 8
static int town_pose_evaluate_base(DndInstance*);
extern int town_texture_feature(int);
typedef struct {DndInstance *instance;unsigned hash;} TownPoseEntry;
static TownPoseEntry town_pose_entries[TOWN_POSE_CAP];
static DndInstance *town_pose_actors[7];
static int town_pose_deferred,town_pose_flushing,town_pose_dirty_count;
static int town_pose_dirty[TOWN_POSE_CAP],town_pose_strength[TOWN_POSE_CAP];
static unsigned town_pose_evaluations,town_pose_avoided,town_pose_shares,town_pose_splits;
static unsigned town_pose_word(float f){unsigned u;memcpy(&u,&f,4);return u;}
static unsigned town_pose_override_hash(const DndInstance *in){
 unsigned hash=2166136261u;int any=0;
 if(in->offset&&in->offset_set)for(unsigned i=0;i<in->model->nodes;i++)if(in->offset_set[i]){any=1;hash=(hash^i)*16777619u;const unsigned char *p=(const void*)(in->offset+i);for(unsigned n=0;n<sizeof(DndPose);n++)hash=(hash^p[n])*16777619u;}
 return any?hash:0;
}
static unsigned town_pose_hash(const DndInstance *in){
 unsigned key[8]={(unsigned)in->model->token,in->model->hash,(unsigned)in->clip,(unsigned)in->next,in->clip>=0?town_pose_word(in->time):0,in->next>=0?town_pose_word(in->next_time):0,in->next>=0?town_pose_word(in->fade>0?fminf(1,in->elapsed/in->fade):1):0,town_pose_override_hash(in)};
 return dnd_model_hash(key,sizeof key);
}
static int town_pose_equal(const DndInstance *a,const DndInstance *b){
 if(a->model!=b->model||a->clip!=b->clip||a->next!=b->next)return 0;
 if(a->clip>=0&&town_pose_word(a->time)!=town_pose_word(b->time))return 0;
 if(a->next>=0&&(town_pose_word(a->next_time)!=town_pose_word(b->next_time)||town_pose_word(a->fade>0?fminf(1,a->elapsed/a->fade):1)!=town_pose_word(b->fade>0?fminf(1,b->elapsed/b->fade):1)))return 0;
 for(unsigned i=0;i<a->model->nodes;i++){
  int left=a->offset&&a->offset_set&&a->offset_set[i],right=b->offset&&b->offset_set&&b->offset_set[i];
  if(left!=right||(left&&memcmp(a->offset+i,b->offset+i,sizeof(DndPose))))return 0;
 }
 return 1;
}
static int town_pose_dirty_index(int token){for(int i=0;i<town_pose_dirty_count;i++)if(town_pose_dirty[i]==token)return i;return -1;}
static int town_pose_complete(const DndInstance *in){
 if(!in||!in->model->ready||!in->vertices)return 0;
 for(unsigned i=0;i<in->model->meshes;i++)if(in->model->mesh[i].skin>=0&&!in->vertices[i])return 0;
 return 1;
}
static unsigned town_pose_charge(const DndInstance *in,unsigned mesh){return (in->model->mesh[mesh].vertices*24+63u)&~31u;}
static int town_pose_other_owner(const DndInstance *in,unsigned mesh,int allow_group){
 float *p=in->vertices[mesh];if(!p)return 0;
 for(int i=0;i<DND_INSTANCE_SLOTS;i++){
  DndInstance *other=dnd_instances[i];if(!other||other==in||other->model!=in->model||!other->vertices||other->vertices[mesh]!=p)continue;
  int dirty=town_pose_dirty_index(other->token);
  if(allow_group&&town_pose_flushing&&dirty>=0&&town_pose_strength[dirty]&&town_pose_equal(in,other))continue;
  return 1;
 }
 return 0;
}
static void town_pose_drop(DndInstance *in,unsigned mesh){
 if(!in->vertices[mesh])return;
 if(town_pose_other_owner(in,mesh,0))in->bytes-=town_pose_charge(in,mesh);else dnd_model_free(in->vertices[mesh],&in->bytes);
 in->vertices[mesh]=NULL;
}
static void town_pose_detach(DndInstance *in){
 for(unsigned i=0;i<in->model->meshes;i++)if(in->vertices[i]&&town_pose_other_owner(in,i,1)){in->bytes-=town_pose_charge(in,i);in->vertices[i]=NULL;town_pose_splits++;}
}
static void town_pose_register_live(DndInstance *in,unsigned hash){
 if(!in->model->skins)return;
 int free_slot=-1;
 for(int i=0;i<TOWN_POSE_CAP;i++){if(town_pose_entries[i].instance==in){town_pose_entries[i].hash=hash;return;}if(!town_pose_entries[i].instance&&free_slot<0)free_slot=i;}
 if(free_slot>=0)town_pose_entries[free_slot]=(TownPoseEntry){in,hash};
}
static int town_pose_share(DndInstance *in,int force){
 if(!town_texture_feature(64)||!in->model->skins)return 0;
 unsigned hash=town_pose_hash(in);
 for(int c=0;c<TOWN_POSE_CAP;c++){
  DndInstance *other=town_pose_entries[c].instance;
  if(!other||other==in||town_pose_entries[c].hash!=hash||!town_pose_complete(other))continue;
  int dirty=town_pose_dirty_index(other->token);if(dirty>=0&&town_pose_strength[dirty])continue;
  if(!town_pose_equal(in,other))continue;
  uint64_t current=0,needed=0;int changed=0;
  for(unsigned i=0;i<in->model->meshes;i++)if(in->model->mesh[i].skin>=0){if(in->vertices[i])current+=town_pose_charge(in,i);needed+=town_pose_charge(in,i);changed|=in->vertices[i]!=other->vertices[i];}
  if((uint64_t)in->bytes-current+needed>in->budget)return 0;
  if(changed)for(unsigned i=0;i<in->model->meshes;i++)if(in->model->mesh[i].skin>=0&&in->vertices[i]!=other->vertices[i]){town_pose_drop(in,i);in->vertices[i]=other->vertices[i];in->bytes+=town_pose_charge(in,i);}
  if(changed||force){memcpy(in->pose,other->pose,in->model->nodes*sizeof(DndPose));memcpy(in->other,other->other,in->model->nodes*sizeof(DndPose));memcpy(in->global,other->global,in->model->nodes*64);memcpy(in->done,other->done,in->model->nodes);}
  if(changed)town_pose_shares++;
  if(force)town_pose_avoided++;
  town_pose_register_live(in,hash);return 1;
 }
 return 0;
}
static int town_pose_queue(DndInstance *in,int strength){
 int index=town_pose_dirty_index(in->token);if(index>=0){if(strength>town_pose_strength[index])town_pose_strength[index]=strength;return 1;}
 if(town_pose_dirty_count==TOWN_POSE_CAP)return 0;
 town_pose_dirty[town_pose_dirty_count]=in->token;town_pose_strength[town_pose_dirty_count++]=strength;return 1;
}
int dnd_model_evaluate(DndInstance *in){
 if(!in||!in->model->ready||dnd_device_active())return -1;
 if(town_pose_deferred){if(town_pose_queue(in,2))return 0;}
 if(town_pose_share(in,1))return 0;
 town_pose_detach(in);int result=town_pose_evaluate_base(in);
 if(result==0){town_pose_evaluations++;town_pose_register_live(in,town_pose_hash(in));}return result;
}
static void town_pose_forget(DndInstance *in){
 for(int i=0;i<TOWN_POSE_CAP;i++)if(town_pose_entries[i].instance==in)town_pose_entries[i]=(TownPoseEntry){0};
 for(int i=0;i<7;i++)if(town_pose_actors[i]==in)town_pose_actors[i]=NULL;
}
int town_pose_register_actor(int actor,DndModel *model,unsigned bytes){
 if(dnd_device_active()||actor<0||actor>=7||!model)return 0;
 DndInstance *found=NULL;
 for(int i=0;i<DND_INSTANCE_SLOTS;i++)if(dnd_instances[i]&&dnd_instances[i]->model==model&&dnd_instances[i]->bytes==bytes){
  int claimed=0;for(int a=0;a<7;a++)claimed|=town_pose_actors[a]==dnd_instances[i];if(claimed)continue;
  if(found)return 0;
  found=dnd_instances[i];
 }
 if(!found)return 0;
 town_pose_actors[actor]=found;return 1;
}
int town_pose_actor(int actor){if(actor<0||actor>=7||!town_pose_deferred||!town_pose_actors[actor])return 0;return town_pose_queue(town_pose_actors[actor],1);}
int town_pose_batch(int begin){
 if(dnd_device_active()||begin<0||begin>1)return -1;
 if(begin){if(town_pose_deferred)return -1;town_pose_dirty_count=0;town_pose_deferred=1;return 1;}
 town_pose_deferred=0;town_pose_flushing=1;int result=1;
 for(int i=0;i<town_pose_dirty_count;i++){
  DndInstance *in=dnd_instance_find(town_pose_dirty[i]);int strength=town_pose_strength[i];if(!in||!in->model->ready){result=-1;break;}
  if(strength==2){if(dnd_model_evaluate(in)<0){result=-1;break;}}
  else if(town_texture_feature(64)){if(!town_pose_share(in,0)&&!town_pose_complete(in)&&dnd_model_evaluate(in)<0){result=-1;break;}}
  else{int shared=0;for(unsigned m=0;m<in->model->meshes;m++)shared|=town_pose_other_owner(in,m,0);if(shared&&dnd_model_evaluate(in)<0){result=-1;break;}}
  town_pose_strength[i]=0;
 }
 town_pose_flushing=0;town_pose_dirty_count=0;return result;
}
int town_pose_stats(int kind){
 if(kind==0)return (int)town_pose_evaluations;
 if(kind==1)return (int)town_pose_avoided;
 if(kind==2)return (int)town_pose_shares;
 if(kind==3)return (int)town_pose_splits;
 unsigned saved=0;
 for(int i=0;i<DND_INSTANCE_SLOTS;i++){
  DndInstance *in=dnd_instances[i];if(!in||!in->vertices)continue;
  for(unsigned m=0;m<in->model->meshes;m++)if(in->vertices[m])for(int j=0;j<i;j++){
   DndInstance *other=dnd_instances[j];if(other&&other->model==in->model&&other->vertices&&other->vertices[m]==in->vertices[m]){saved+=in->model->mesh[m].vertices*24;break;}
  }
 }
 return kind==4?(int)saved:-1;
}
