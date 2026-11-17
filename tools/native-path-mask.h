#ifndef ANIMALTOWN_PATH_MASK_H
#define ANIMALTOWN_PATH_MASK_H
#include <stdint.h>
#include <string.h>
#define PATH_SIDE 128
#define PATH_HEIGHT 120
#define PATH_PACKED (PATH_SIDE*PATH_HEIGHT/4)
#define PATH_BYTES (PATH_SIDE*PATH_SIDE/2)
/* GX I4, one nearest-filtered texel per metre. No RGBA decode, per-cell
 * geometry or duplicate managed image. The final eight rows stay empty. */
static inline int path_offset(int x,int z){return ((z/8)*(PATH_SIDE/8)+x/8)*32+(z%8)*4+(x%8)/2;}
static inline void path_expand(uint8_t *image,const uint8_t *packed){
    memset(image,0,PATH_BYTES);
    for(int z=0;z<PATH_HEIGHT;z++)for(int x=0;x<PATH_SIDE;x++){
        int cell=z*PATH_SIDE+x;
        if(((packed[cell/4]>>((cell%4)*2))&3)==1)image[path_offset(x,z)]|=(uint8_t)(x%2?15:240);
    }
}
#endif
