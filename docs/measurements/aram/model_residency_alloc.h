#ifndef TOWN_MODEL_ALLOCATION_AUDIT_H
#define TOWN_MODEL_ALLOCATION_AUDIT_H
#include <stddef.h>
void *town_audit_calloc(size_t count,size_t size);
void *town_audit_aligned_alloc(size_t alignment,size_t size);
void town_audit_free(void *pointer);
#endif
