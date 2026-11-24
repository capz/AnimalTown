#ifndef TOWN_TEST_MALLOC_H
#define TOWN_TEST_MALLOC_H
#include <stddef.h>
struct mallinfo { int fordblks; };
struct mallinfo town_test_mallinfo(void);
void *town_test_memalign(size_t alignment,size_t bytes);
#define mallinfo() town_test_mallinfo()
#define memalign town_test_memalign
#endif
