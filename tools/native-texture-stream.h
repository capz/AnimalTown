#ifndef ANIMALTOWN_TEXTURE_STREAM_H
#define ANIMALTOWN_TEXTURE_STREAM_H
#include "dnd_model_internal.h"
int animaltown_textures_init(DndArray*);
int animaltown_textures_begin(int);
int animaltown_textures_step(int);
int animaltown_textures_read(void);
int animaltown_textures_complete(int);
int animaltown_textures_stats(int);
int animaltown_textures_flags(int);
int animaltown_textures_hasmodel(DndString*);
int animaltown_textures_demand(int,int,int);
int town_texture_acquire(DndTexture*,unsigned);
void town_texture_release(DndTexture*);
int town_texture_view(const DndTexture*,DndTexture*,int*);
void town_texture_model(int,DndModel*);
void town_texture_aram_used(void);
int town_texture_io_busy(void);
int town_texture_minimum(unsigned);
int town_texture_identity(unsigned,unsigned,unsigned,unsigned);
#endif
