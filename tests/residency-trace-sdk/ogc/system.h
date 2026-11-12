#ifndef ANIMALTOWN_RESIDENCY_TRACE_SYSTEM_H
#define ANIMALTOWN_RESIDENCY_TRACE_SYSTEM_H
/* Host fixture for the pinned libogc2 printf-compatible reporting ABI. */
void SYS_Report(const char *format, ...) __attribute__((format(printf, 1, 2)));
#endif
