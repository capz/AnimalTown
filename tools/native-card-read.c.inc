/* AnimalTown overlay for the pinned SDK's libogc card adapter.
 * CARD_Read starts at a card sector; 512-byte sub-sector reads repeat the
 * first portion of that sector with this libogc2 build. Read complete sectors
 * into aligned staging, then copy only the caller's requested prefix. */
int dnd_gc_card_read(int device, const char *name, void *data, int capacity) {
  int slot=device-4;
  identity(slot);
  card_file file;
  int rc=CARD_Open(slot,name,&file);
  if(rc<0)return card_errno(rc);
  if(!data){int length=file.len;CARD_Close(&file);return length;}
  if(capacity<0){CARD_Close(&file);return -EINVAL;}
  int length=capacity<file.len?capacity:file.len;
  u32 sector=0;
  rc=CARD_GetSectorSize(slot,&sector);
  if(rc<0||sector<512||sector>1024*1024||(sector&511)){
    CARD_Close(&file);return rc<0?card_errno(rc):-EIO;
  }
  void *block=memalign(32,sector);
  if(!block){CARD_Close(&file);return -ENOMEM;}
  int done=0;
  while(done<length){
    rc=CARD_Read(&file,block,sector,(u32)done);
    if(rc<0)break;
    int count=length-done<(int)sector?length-done:(int)sector;
    memcpy((char*)data+done,block,(size_t)count);done+=count;
  }
  int close_rc=CARD_Close(&file);
  free(block);
  return rc<0?card_errno(rc):close_rc<0?card_errno(close_rc):done;
}
