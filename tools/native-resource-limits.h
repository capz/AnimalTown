#ifndef ANIMALTOWN_NATIVE_RESOURCE_LIMITS_H
#define ANIMALTOWN_NATIVE_RESOURCE_LIMITS_H
/* Terrain prefetch shares these handles with models, textures, UI and masks.
 * Tokens reserve ten index bits, with zero reserved as the invalid handle. */
#define ANIMALTOWN_DRAW_RESOURCE_SLOTS 768
#if ANIMALTOWN_DRAW_RESOURCE_SLOTS < 1 || ANIMALTOWN_DRAW_RESOURCE_SLOTS > 1023
#error AnimalTown resource slots exceed the native token index
#endif
#endif
