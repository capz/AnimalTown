/* Keep libogc's exact mount result separate from transaction errno values.
 * A failed directory/FAT check may leave libogc attached to its workarea;
 * detach before freeing it so the next attempt starts with valid state. */
extern int animaltown_card_mount_error;
extern int animaltown_card_recovery_error;
bool dnd_gc_card_mount(int device, const char *game, const char *company) {
  animaltown_card_debug=0;
  animaltown_card_mount_error=0;
  animaltown_card_recovery_error=0;
  int slot=device-4;
  if(slot<0||slot>1){animaltown_card_mount_error=CARD_ERROR_FATAL_ERROR;return false;}
  if(dnd_cmgr_active(slot)){animaltown_card_mount_error=CARD_ERROR_BUSY;return false;}
  if(!card_initialized){CARD_Init(game,company);card_initialized=true;}
  void *area=memalign(32,CARD_WORKAREA);
  if(!area){animaltown_card_mount_error=-2001;return false;}
  int rc=CARD_Mount(slot,area,NULL);
  if(rc<0){
    animaltown_card_mount_error=rc;
    CARD_Unmount(slot);
    free(area);
    return false;
  }
  workareas[slot]=area;
  strcpy(games[slot],game);strcpy(companies[slot],company);
  rc=dnd_gc_card_recover(device);
  if(rc<0){
    animaltown_card_recovery_error=rc;
    CARD_Unmount(slot);
    free(area);workareas[slot]=NULL;
    return false;
  }
  return true;
}
