#ifndef ANIMALTOWN_PATH_MASK_H
#define ANIMALTOWN_PATH_MASK_H
#include <stdint.h>
#include <string.h>
#define PATH_SIDE 128
#define PATH_HEIGHT 120
#define PATH_PACKED (PATH_SIDE*PATH_HEIGHT/4)
#define PATH_VISITS (PATH_SIDE*PATH_HEIGHT)
#define PATH_BYTES (PATH_SIDE*PATH_SIDE/2)
#define PATH_STAMP (PATH_SIDE*PATH_HEIGHT/2)
/* Match TileTraffic's exact visibility milestones. */
#define PATH_MIN_VISITS 35
#define PATH_FULL_VISITS 100
static inline int path_opacity(int visits){return visits<PATH_MIN_VISITS?0:visits>=PATH_FULL_VISITS?15:2+(visits-PATH_MIN_VISITS)*13/(PATH_FULL_VISITS-PATH_MIN_VISITS);}
/* GX I4, one nearest-filtered texel per metre. No RGBA decode, per-cell
 * geometry or duplicate managed image. Padding has no world coverage; its
 * first four bytes carry a publication stamp for sampled texture caches. */
static inline int path_offset(int x,int z){return ((z/8)*(PATH_SIDE/8)+x/8)*32+(z%8)*4+(x%8)/2;}
static inline void path_expand(uint8_t *image,const uint8_t *packed,const uint8_t *visits){
    memset(image,0,PATH_BYTES);
    for(int z=0;z<PATH_HEIGHT;z++)for(int x=0;x<PATH_SIDE;x++){
        int cell=z*PATH_SIDE+x;
        if(((packed[cell/4]>>((cell%4)*2))&3)==1)image[path_offset(x,z)]|=(uint8_t)(path_opacity(visits[cell])<<(x%2?0:4));
    }
}
#endif
