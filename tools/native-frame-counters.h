#ifndef ANIMALTOWN_FRAME_COUNTERS_H
#define ANIMALTOWN_FRAME_COUNTERS_H
#include <stdint.h>
enum { TOWN_ALLOCATIONS,TOWN_FREE_SEARCHES,TOWN_MESH_CREATES,TOWN_MESH_UPDATES,TOWN_MESH_RELEASES,TOWN_TEXTURE_CREATES,TOWN_TEXTURE_UPDATES,TOWN_COUNTERS };
typedef struct { unsigned enabled;uint32_t count[TOWN_COUNTERS]; } TownFrameCounters;
extern TownFrameCounters town_frame_counters;
#define TOWN_COUNT(kind) do {if(town_frame_counters.enabled)town_frame_counters.count[kind]++;} while(0)
#endif
