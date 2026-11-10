/* 32 verified detail-bank blocks; one completed ARAM quantum between frames.
 * Stage 1 is independent of the DVD reader's stage 0. */
typedef struct {unsigned offset,count,crc,stamp;int bank,valid;} TownTextureCacheBlock;
static TownTextureCacheBlock texture_cache_blocks[32];
static int texture_cache_store,texture_cache_phase,texture_cache_slot=-1;
static unsigned texture_cache_position,texture_cache_crc,texture_cache_retry;
static unsigned texture_cache_hits,texture_cache_misses,texture_cache_bytes,texture_cache_stores,texture_cache_errors;
static TownTextureCacheBlock texture_cache_pending;
void town_texture_reclaim_aram(void){
 if(texture_cache_store){(void)dnd_aram_release(texture_cache_store);(void)dnd_aram_dispose(texture_cache_store);texture_cache_store=0;}
 memset(texture_cache_blocks,0,sizeof(texture_cache_blocks));texture_cache_phase=0;texture_cache_slot=-1;texture_cache_retry=texture_frame+120;
}
static int tex_cache_allocate(void){
 if(!(texture_features&8)||texture_frame<texture_cache_retry)return 0;
 if(texture_cache_store)return 1;
 texture_cache_store=dnd_aram_allocate(1024*1024);
 if(!texture_cache_store)return 0;
 if(dnd_aram_retain(texture_cache_store)){(void)dnd_aram_dispose(texture_cache_store);texture_cache_store=0;return 0;}
 return 1;
}
static int tex_cache_dma(int slot,int buffer,unsigned count,int write){
 if(texture_aram_used||!texture_cache_store)return 0;
 unsigned rounded=(count+31u)&~31u;
 if(dnd_aram_begin_native(texture_cache_store,slot*(int)TEX_QUANTUM,buffer,0,(int)rounded,write))return -1;
 texture_aram_used=1;int result;do{result=dnd_aram_poll_native();}while(!result);
 if(result!=1){texture_cache_errors++;return -1;}
 texture_cache_bytes+=rounded;return 1;
}
static int tex_cache_pump(void){
 if(!texture_cache_phase)return 0;
 if(!(texture_features&8)){town_texture_reclaim_aram();return 1;}
 if(texture_cache_phase==1||texture_cache_phase==2){
  if(texture_aram_used)return 0;
  int result=tex_cache_dma(texture_cache_slot,texture_stage[1],texture_cache_pending.count,texture_cache_phase==1);
  if(result<0){texture_cache_phase=0;texture_cache_slot=-1;return 1;}
  if(!result)return 0;
  texture_cache_phase++;if(texture_cache_phase==3){texture_cache_position=0;texture_cache_crc=~0u;}return 1;
 }
 unsigned count=TEX_MIN(4096u,texture_cache_pending.count-texture_cache_position);
 unsigned char *p=(void*)(uintptr_t)dnd_buffer_address(texture_stage[1],(int)texture_cache_position);if(!p){texture_cache_phase=0;return 1;}
 texture_cache_crc=tex_crc(texture_cache_crc,p,count);texture_cache_position+=count;
 if(texture_cache_position==texture_cache_pending.count){
  if(~texture_cache_crc==texture_cache_pending.crc){texture_cache_blocks[texture_cache_slot]=texture_cache_pending;texture_cache_blocks[texture_cache_slot].valid=1;texture_cache_blocks[texture_cache_slot].stamp=texture_frame;texture_cache_stores++;}
  else texture_cache_errors++;
  texture_cache_phase=0;texture_cache_slot=-1;
 }
 return 1;
}
static int tex_cache_find(int bank,unsigned offset,unsigned count,unsigned crc){
 if(!(texture_features&8)||!texture_cache_store)return -1;
 for(int i=0;i<32;i++){TownTextureCacheBlock *b=texture_cache_blocks+i;if(b->valid&&b->bank==bank&&b->offset==offset&&b->count==count&&b->crc==crc)return i;}
 return -1;
}
static int tex_cache_read(int bank,unsigned offset,unsigned count,unsigned crc){
 int slot=tex_cache_find(bank,offset,count,crc);if(slot<0){texture_cache_misses++;return -1;}
 if(texture_aram_used)return -2;
 int result=tex_cache_dma(slot,texture_stage[0],count,0);
 if(result<0){texture_cache_blocks[slot].valid=0;return -1;}
 if(!result)return -2;
 texture_cache_blocks[slot].stamp=texture_frame;texture_cache_hits++;return slot;
}
static int tex_cache_begin_store(int bank,unsigned offset,unsigned count,unsigned crc){
 if(bank!=1||texture_cache_phase||!tex_cache_allocate())return 0;
 if(tex_cache_find(bank,offset,count,crc)>=0)return 0;
 texture_cache_pending=(TownTextureCacheBlock){.bank=bank,.offset=offset,.count=count,.crc=crc,.stamp=texture_frame};return 1;
}
static void tex_cache_finish_store(void){
 int slot=-1;for(int i=0;i<32;i++)if(!texture_cache_blocks[i].valid){slot=i;break;}
 if(slot<0){slot=0;for(int i=1;i<32;i++)if(texture_cache_blocks[i].stamp<texture_cache_blocks[slot].stamp)slot=i;}
 texture_cache_blocks[slot].valid=0;texture_cache_slot=slot;texture_cache_phase=1;
}
