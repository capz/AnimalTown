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
    assert(argc > 1 && argc <= 33);
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
        for (unsigned mesh = 0; mesh < data->meshes; mesh++) {
            DndMesh *m = &data->mesh[mesh];
            for (unsigned i = 0; i < m->indices; i += 3) {
                float p[3][3], normal[3] = {0};
                for (unsigned v = 0; v < 3; v++) {
                    const unsigned char *src = m->data + 80 * dnd_model_u32(m->index + (i + v) * 4);
                    for (unsigned c = 0; c < 3; c++) {
                        p[v][c] = dnd_model_f32(src + c * 4);
                        normal[c] += dnd_model_f32(src + 12 + c * 4);
                    }
                }
                float a[3], b[3];
                for (unsigned c = 0; c < 3; c++) { a[c] = p[1][c] - p[0][c]; b[c] = p[2][c] - p[0][c]; }
                float dot = (a[1]*b[2]-a[2]*b[1])*normal[0] + (a[2]*b[0]-a[0]*b[2])*normal[1] + (a[0]*b[1]-a[1]*b[0])*normal[2];
                assert(dot <= 0.00001f); /* Clockwise GX faces, outward source normals. */
            }
        }
#ifdef ANIMALTOWN_RETAIN_BUILDINGS
        extern int animaltown_building_retain(int);
        unsigned geometry_hash=0,texture_hash=0,original_bytes=data->bytes;
        const unsigned char *original_package=data->package;
        for(unsigned i=0;i<data->meshes;i++)geometry_hash^=dnd_model_hash(data->mesh[i].data,data->mesh[i].vertices*80)^dnd_model_hash(data->mesh[i].index,data->mesh[i].indices*4);
        for(unsigned i=0;i<data->textures;i++)texture_hash^=dnd_model_hash(data->texture[i].data,data->texture[i].size);
        unsigned budget=data->budget;data->budget=data->bytes;
        assert(animaltown_building_retain(model)==-2&&data->package==original_package&&data->bytes==original_bytes);
        data->budget=budget;assert(animaltown_building_retain(model)==1&&data->bytes<original_bytes&&data->ready);
        unsigned actual_geometry=0,actual_texture=0;
        for(unsigned i=0;i<data->meshes;i++)actual_geometry^=dnd_model_hash(data->mesh[i].data,data->mesh[i].vertices*80)^dnd_model_hash(data->mesh[i].index,data->mesh[i].indices*4);
        for(unsigned i=0;i<data->textures;i++)actual_texture^=dnd_model_hash(data->texture[i].data,data->texture[i].size);
        assert(actual_geometry==geometry_hash&&actual_texture==texture_hash&&dnd_model_unload(model)==-1);
        DndArray *probe=calloc(1,sizeof(*probe)+1);assert(probe);probe->length=probe->element_size=1;
        assert(dnd_model_copy(model,0,probe,1,0)==-1);free(probe);
        assert(animaltown_building_retain(model)==1);
#endif
        int instance = dnd_model_instance(model, 16 * 1024);
        assert(instance);
        unsigned used = data->bytes + dnd_instance_find(instance)->bytes;
        resident += used;
        printf("Building part %s: %u budgeted native bytes\n", argv[part], used);
#ifdef ANIMALTOWN_RETAIN_BUILDINGS
        assert(dnd_model_dispose(model)==0&&dnd_instance_find(instance)->model->ready);
        assert(dnd_model_instance_dispose(instance)==0);
#endif
    }
    printf("Building kit: %u / %u budgeted native bytes\n", resident, 768u * 1024);
    fflush(stdout);
    assert(resident <= 768 * 1024);
    return 0;
}
