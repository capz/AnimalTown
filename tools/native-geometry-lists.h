#ifndef ANIMALTOWN_GEOMETRY_LISTS_H
#define ANIMALTOWN_GEOMETRY_LISTS_H
#include <stdint.h>
typedef struct {const unsigned char *data;unsigned bytes,primitives;} TownGeometryList;
int town_geometry_scope(int);
void town_geometry_mesh_owner(int);
void town_geometry_model_owner(int,unsigned,unsigned,int);
int town_geometry_mesh_list(unsigned,unsigned,TownGeometryList*);
int town_geometry_model_list(unsigned,unsigned,TownGeometryList*);
int town_geometry_pump(void);
void town_geometry_begin(unsigned);
void town_geometry_clear(void);
void town_geometry_forget(int,int);
int town_geometry_stats(int);
#endif
