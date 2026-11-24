#ifndef ANIMALTOWN_MEMORY_PRESSURE_H
#define ANIMALTOWN_MEMORY_PRESSURE_H
#include <stddef.h>

void *animaltown_memory_malloc(size_t bytes);
void *animaltown_memory_calloc(size_t count,size_t bytes);
void *animaltown_memory_realloc(void *previous,size_t bytes);
void *animaltown_memory_memalign(size_t alignment,size_t bytes);

/* The implementation calls the real libc functions. Other bundled native
 * translation units use aliases, including allocations in SDK include files.
 * Existing free(), ownership and accounting stay with the original owner. */
#if defined(DND_GAMECUBE_STORAGE) && !defined(ANIMALTOWN_MEMORY_IMPLEMENTATION)
#define malloc animaltown_memory_malloc
#define calloc animaltown_memory_calloc
#define realloc animaltown_memory_realloc
#define memalign animaltown_memory_memalign
#endif
#endif
