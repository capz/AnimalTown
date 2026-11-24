"""Exercise the production water shader and the pinned SDK's actual copy helper."""
from pathlib import Path
import subprocess
import sys
import tempfile


def main(native):
    root = Path(__file__).resolve().parents[1]
    source = (native / 'source/graphics_gx.c').read_text()
    signature = 'void dnd_draw_backend_copy(const DndDrawTexture *texture,int x,int y,int w,int h) {'
    if source.count(signature) != 1:
        raise SystemExit('Pinned water copy fixture boundary changed')
    start = source.index(signature)
    end = start + len(signature)
    depth = 1
    while depth:
        if end >= len(source):
            raise SystemExit('Unterminated pinned water copy helper')
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    with tempfile.TemporaryDirectory(prefix='animaltown-water-') as temporary:
        temporary = Path(temporary)
        (temporary / 'water-copy-fixture.inc').write_text(source[start:end] + '\n')
        executable = temporary / 'native-water-optics-check'
        subprocess.run(['cc', '-std=c11', '-O2', '-Wall', '-Wextra', '-Werror',
                        '-I' + str(native / 'include'), '-I' + str(temporary),
                        str(root / 'tests/native-water-optics.c'), '-lm', '-o', str(executable)], check=True)
        subprocess.run([str(executable)], check=True)


if __name__ == '__main__':
    main(Path(sys.argv[1]).resolve())
