#include "dnd_model_internal.h"
#include <assert.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>

/* Immutable package loading and static poses need no graphics or filesystem
 * service. Exercise the production loader with the actual cooked disc parts.
 * Host pointers are wider than GameCube pointers, making this conservative. */
bool dnd_device_active(void) { return false; }
int dnd_fs_close(int handle) { (void)handle; return 0; }

int main(int argc, char **argv) {
    assert(argc > 1 && argc <= 29);
    unsigned resident = 0;
    for (int part = 1; part < argc; part++) {
        FILE *file = fopen(argv[part], "rb");
        assert(file && !fseek(file, 0, SEEK_END));
        long length = ftell(file);
        assert(length > 0 && length <= 256 * 1024 && !fseek(file, 0, SEEK_SET));
        unsigned char *bytes = malloc((size_t)length);
        assert(bytes && fread(bytes, 1, (size_t)length, file) == (size_t)length);
        assert(!fclose(file));
        int model = dnd_model_begin_data(bytes, (unsigned)length, 256 * 1024);
        free(bytes);
        assert(model);
        DndModel *data = dnd_model_find(model, false);
        assert(data && data->ready && !data->skins && !data->clips);
        int instance = dnd_model_instance(model, 16 * 1024);
        assert(instance);
        unsigned used = data->bytes + dnd_instance_find(instance)->bytes;
        resident += used;
        printf("Building part %s: %u budgeted native bytes\n", argv[part], used);
    }
    printf("Building kit: %u / %u budgeted native bytes\n", resident, 768u * 1024);
    fflush(stdout);
    assert(resident <= 768 * 1024);
    return 0;
}
