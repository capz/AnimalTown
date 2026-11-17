#include "dnd_draw.h"
#ifndef ANIMALTOWN_PATH_MASK_H
#include "animaltown_path_mask.h"
#endif
static int path_storage,path_revision;
static uint32_t path_publication;
static uint8_t *path_pixels;
const uint8_t *animaltown_path_mask(void){return path_pixels;}
int animaltown_paths_prepare(DndArray *packed,int enabled,int revision){
    if(dnd_device_active())return 0;
    if(!enabled){
        if(path_storage&&dnd_draw_release(path_storage)<0)return 0;
        path_storage=0;path_pixels=NULL;return 1;
    }
    if(!packed||packed->element_size!=1||packed->elements_are_references||packed->length!=PATH_PACKED)return 0;
    int fresh=!path_storage;
    if(fresh){
        path_storage=dnd_buffer_create(PATH_BYTES,32);if(!path_storage)return 0;
        path_pixels=(uint8_t*)(uintptr_t)dnd_buffer_address(path_storage,0);
        if(!path_pixels){dnd_draw_release(path_storage);path_storage=0;return 0;}
    }
    if(fresh||path_revision!=revision){
        path_expand(path_pixels,(const uint8_t*)packed->data);path_revision=revision;
        /* Dolphin's default sampled hash can miss changed sparse I4 texels.
         * This sampled word is outside the 120 world rows. Advance even on a
         * fresh allocation with the same revision/address; no second texture. */
        uint32_t stamp=++path_publication;
        memcpy(path_pixels+PATH_STAMP,&stamp,sizeof stamp);
        dnd_draw_backend_flush(path_pixels,PATH_BYTES,0);dnd_draw_backend_publish_texture();
    }
    return 1;
}
