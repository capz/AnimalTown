"""Forward sparse game acceptance markers without changing the SDK package."""
from pathlib import Path
import shutil
import sys


def patch(root):
    source = root / 'source/console_overlay.c'
    original = 'void dnd_console_write_line(const char *s) { newline(s ? s : ""); }'
    replacement = '''#include "animaltown_residency_trace.inc"
void dnd_console_write_line(const char *s) {
  animaltown_residency_trace(s);
  newline(s ? s : "");
}'''
    text = source.read_text()
    if text.count(original) != 1:
        raise SystemExit('Pinned console trace hook changed: dnd_console_write_line')
    source.write_text(text.replace(original, replacement))
    shutil.copyfile(Path(__file__).with_name('native-residency-trace.inc'),
                    root / 'source/animaltown_residency_trace.inc')
    print('Residency trace: full game markers forwarded; overlay ring preserved')


if __name__ == '__main__':
    patch(Path(sys.argv[1]))
