/* Sparse acceptance events need their full text even while the overlay is
 * hidden. Keep the original ring's size/rendering policy in console_overlay.c. */
#ifdef GEKKO
#include <ogc/system.h>
#endif

static void animaltown_residency_trace(const char *text) {
#ifdef GEKKO
  if (text && (!strncmp(text, "ANIMALTOWN ", sizeof("ANIMALTOWN ") - 1) ||
               !strncmp(text, "Terrain archive:", sizeof("Terrain archive:") - 1)))
    SYS_Report("%s\n", text);
#else
  (void)text;
#endif
}
