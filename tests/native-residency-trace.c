#include <assert.h>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>

static unsigned reports;
static char reported[1024];
void SYS_Report(const char *format, ...) {
  /* Message text must always be data, including literal percent sequences. */
  assert(!strcmp(format, "%s\n"));
  va_list args;
  va_start(args, format);
  int bytes = vsnprintf(reported, sizeof(reported), format, args);
  va_end(args);
  assert(bytes >= 0 && (size_t)bytes < sizeof(reported));
  reports++;
}

/* Include the actual patched SDK implementation to verify its ring as well as
 * the bridge. Unused renderer sections are removed by the fixture link. */
#include "console_overlay.c"

static void write_marker(const char *text) {
  unsigned before = reports;
  dnd_console_write_line(text);
#ifdef GEKKO
  assert(reports == before + 1);
  assert(strlen(reported) == strlen(text) + 1);
  assert(!strncmp(reported, text, strlen(text)));
  assert(reported[strlen(text)] == '\n');
#else
  assert(reports == before);
#endif
  size_t columns = strlen(text);
  if (columns > COLS) columns = COLS;
  assert(strlen(lines[head]) == columns);
  assert(!strncmp(lines[head], text, columns));
}

int main(void) {
  dnd_console_init();
  dnd_console_write_line(NULL);
  assert(count == 1 && !lines[head][0] && reports == 0);
  const char *long_marker =
      "ANIMALTOWN MODEL WARM 42 aram bytes 12345678 max advance us 1000 "
      "readiness waits 12345 portal wait ticks 67890 sentinel-after-column-78";
  assert(strlen(long_marker) > COLS);
  write_marker(long_marker);
  assert(strlen(lines[head]) == COLS);
  dnd_console_set_visible(0);
  write_marker("ANIMALTOWN literal %s %n 100% complete");
  assert(!dnd_console_visible());
  write_marker("Terrain archive: 12345678 ARAM bytes / 1048576 MEM1 bytes / 16384 parts");
  unsigned before = reports;
  for (int i = 0; i < ROWS + 3; i++)
    dnd_console_write_line("Frame vertices 6000 ANIMALTOWN suffix");
  dnd_console_write_line("ANIMALTOWN");
  dnd_console_write_line("Terrain archives: ignored");
  assert(reports == before && count == ROWS);
  assert(!strcmp(lines[head], "Terrain archives: ignored"));
  dnd_console_set_visible(1);
  write_marker("ANIMALTOWN READY 0.11.331 native bytes 12345678");
  assert(dnd_console_visible());
  puts("Residency trace: full markers, literal formats, prefix filtering, hidden overlay and ring retention passed");
  return 0;
}
