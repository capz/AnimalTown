#include "dnd_model_internal.h"
#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Exercise actual cooked villagers against the production loader/animator.
 * Two independent loads share texture ownership, while only one is compacted.
 * Comparing every source attribute and animated output prevents a memory win
 * from concealing missing normals, skin bindings or animation keyframes. */
static bool frame_active;
bool dnd_device_active(void) { return frame_active; }
int dnd_fs_close(int handle) { (void)handle; return 0; }
extern int animaltown_model_retain(int token);
extern int animaltown_building_retain(int token);

static void rejected_range(int token, const DndModel *model,
                           const unsigned char *package, unsigned bytes) {
    assert(animaltown_model_retain(token) == -1);
    assert(model->package == package && model->bytes == bytes && model->refs == 1 && model->ready);
}

static unsigned char *read_package(const char *path, unsigned *length) {
    FILE *file = fopen(path, "rb");
    assert(file && !fseek(file, 0, SEEK_END));
    long bytes = ftell(file);
    assert(bytes >= 160 && bytes <= 3 * 1024 * 1024 && !fseek(file, 0, SEEK_SET));
    unsigned char *data = malloc((size_t)bytes);
    assert(data && fread(data, 1, (size_t)bytes, file) == (size_t)bytes);
    assert(!fclose(file));
    *length = (unsigned)bytes;
    return data;
}

static void same_payload(const DndModel *a, const DndModel *b) {
    assert(a->nodes == b->nodes && a->meshes == b->meshes && a->materials == b->materials);
    assert(a->skins == b->skins && a->clips == b->clips && a->textures == b->textures);
    assert(a->length == b->length && a->hash == b->hash && a->ready && b->ready);
    assert(!memcmp(a->node, b->node, a->nodes * sizeof(*a->node)));
    assert(!memcmp(a->material, b->material, a->materials * sizeof(*a->material)));
    for (unsigned i = 0; i < a->meshes; i++) {
        const DndMesh *x = &a->mesh[i], *y = &b->mesh[i];
        assert(x->node == y->node && x->skin == y->skin && x->material == y->material);
        assert(x->vertices == y->vertices && x->indices == y->indices);
        assert(x->uniform_color == y->uniform_color && x->unit_normals == y->unit_normals);
        assert(!memcmp(x->center, y->center, sizeof(x->center)));
        assert(!memcmp(x->color, y->color, sizeof(x->color)));
        assert(dnd_model_hash(x->data, x->vertices * 80) == dnd_model_hash(y->data, y->vertices * 80));
        assert(dnd_model_hash(x->index, x->indices * 4) == dnd_model_hash(y->index, y->indices * 4));
        assert(!memcmp(x->data, y->data, x->vertices * 80));
        assert(!memcmp(x->index, y->index, x->indices * 4));
    }
    for (unsigned i = 0; i < a->skins; i++) {
        assert(a->skin[i].count == b->skin[i].count);
        assert(!memcmp(a->skin[i].data, b->skin[i].data, (size_t)a->skin[i].count * 68));
    }
    for (unsigned i = 0; i < a->clips; i++) {
        assert(!strcmp(a->clip[i].name, b->clip[i].name));
        assert(a->clip[i].duration == b->clip[i].duration && a->clip[i].size == b->clip[i].size);
        assert(!memcmp(a->clip[i].tracks, b->clip[i].tracks, a->clip[i].size));
    }
    for (unsigned i = 0; i < a->textures; i++) {
        const DndTexture *x = &a->texture[i], *y = &b->texture[i];
        assert(x->width == y->width && x->height == y->height && x->levels == y->levels);
        assert(x->size == y->size && x->cache == y->cache && x->data == y->data);
        assert(x->wrap_s == y->wrap_s && x->wrap_t == y->wrap_t && x->linear == y->linear);
    }
}

static void same_pose(int a_token, int b_token) {
    DndInstance *a = dnd_instance_find(a_token), *b = dnd_instance_find(b_token);
    assert(a && b && a->model->nodes == b->model->nodes);
    assert(!memcmp(a->pose, b->pose, a->model->nodes * sizeof(*a->pose)));
    assert(!memcmp(a->global, b->global, (size_t)a->model->nodes * 64));
    for (unsigned i = 0; i < a->model->meshes; i++) {
        const DndMesh *mesh = &a->model->mesh[i];
        assert((a->vertices[i] != NULL) == (b->vertices[i] != NULL));
        if (a->vertices[i])
            assert(!memcmp(a->vertices[i], b->vertices[i], (size_t)mesh->vertices * 6 * sizeof(float)));
        unsigned probes[] = {0, mesh->vertices / 2, mesh->vertices - 1};
        for (unsigned j = 0; j < sizeof(probes) / sizeof(probes[0]); j++) {
            float x[6], y[6];
            assert(dnd_model_vertex(a_token, (int)i, (int)probes[j], x) == 0);
            assert(dnd_model_vertex(b_token, (int)i, (int)probes[j], y) == 0);
            assert(!memcmp(x, y, sizeof(x)));
        }
    }
}

static void compare_animation(int a, int b, const DndModel *model) {
    same_pose(a, b);
    const float fractions[] = {0, .125f, .5f, 1};
    for (unsigned clip = 0; clip < model->clips; clip++) {
        assert(dnd_model_play(a, (int)clip, 0, 0) == 0);
        assert(dnd_model_play(b, (int)clip, 0, 0) == 0);
        for (unsigned time = 0; time < sizeof(fractions) / sizeof(fractions[0]); time++) {
            float seconds = fractions[time] * model->clip[clip].duration;
            assert(dnd_model_control(a, 4, seconds) == 0);
            assert(dnd_model_control(b, 4, seconds) == 0);
            same_pose(a, b);
        }
    }
}

int main(int argc, char **argv) {
    assert(argc == 7); /* fox, bunny, giraffe, cat, hamster, spaniel */
    size_t saved_total = 0, texture_total = 0;
    assert(dnd_model_live_handles() == 0 && animaltown_model_retain(-1) == -1);
    for (int file = 1; file < argc; file++) {
        unsigned length;
        unsigned char *source = read_package(argv[file], &length);
        unsigned original_hash = dnd_model_hash(source, length);
        int baseline = dnd_model_begin_data(source, length, 3 * 1024 * 1024);
        assert(baseline);
        int cached_bytes = dnd_model_stat(baseline, 6);
        int retained = dnd_model_begin_data(source, length, 3 * 1024 * 1024);
        assert(retained && dnd_model_stat(retained, 6) == cached_bytes);
        memset(source, 0xa5, length);free(source);source = NULL;
        DndModel *a = dnd_model_find(baseline, false), *b = dnd_model_find(retained, false);
        assert(a && b && dnd_model_hash(a->package, a->length) == original_hash);
        assert(dnd_model_hash(b->package, b->length) == original_hash);
        same_payload(a, b);
        unsigned original_bytes = b->bytes, original_budget = b->budget;
        unsigned char *original_package = b->package;
        b->budget = original_bytes;
        assert(animaltown_model_retain(retained) == -2);
        assert(b->package == original_package && b->bytes == original_bytes && b->refs == 1);
        same_payload(a, b);b->budget = original_budget;
        if (b->skins || b->clips) {
            assert(animaltown_building_retain(retained) == -1);
            same_payload(a, b);
        }
        /* Addresses remain inside the live source allocation, but outside
         * their permitted section. Every failed preflight must be atomic. */
        const unsigned char *data = b->mesh[0].data, *index = b->mesh[0].index;
        b->mesh[0].data = b->package;
        rejected_range(retained, b, original_package, original_bytes);
        b->mesh[0].data = data;same_payload(a, b);
        b->mesh[0].index = b->package;
        rejected_range(retained, b, original_package, original_bytes);
        b->mesh[0].index = index;same_payload(a, b);
        if (b->skins) {
            const unsigned char *skin = b->skin[0].data;
            b->skin[0].data = b->package;
            rejected_range(retained, b, original_package, original_bytes);
            b->skin[0].data = skin;same_payload(a, b);
        }
        if (b->clips) {
            const unsigned char *tracks = b->clip[0].tracks;
            unsigned size = b->clip[0].size;
            b->clip[0].tracks = b->package;
            rejected_range(retained, b, original_package, original_bytes);
            b->clip[0].tracks = tracks;same_payload(a, b);
            b->clip[0].size = length;
            rejected_range(retained, b, original_package, original_bytes);
            b->clip[0].size = size;same_payload(a, b);
        }
        frame_active = true;
        assert(animaltown_model_retain(retained) == -1);
        frame_active = false;
        assert(b->package == original_package && b->bytes == original_bytes);
        int live = dnd_model_instance(retained, 512 * 1024);assert(live);
        assert(animaltown_model_retain(retained) == -1);
        assert(b->package == original_package && b->bytes == original_bytes);
        assert(dnd_model_instance_dispose(live) == 0 && b->refs == 1);
        assert(animaltown_model_retain(retained) == 1);
        assert(b->package != original_package && b->bytes < original_bytes);
        assert(dnd_model_stat(retained, 2) == (int)length);
        assert(dnd_model_stat(retained, 6) == cached_bytes);
        same_payload(a, b);
        unsigned texture_bytes = 0;
        for (unsigned i = 0; i < b->textures; i++)texture_bytes += b->texture[i].size;
        unsigned saved = original_bytes - b->bytes;
        assert(texture_bytes && saved >= texture_bytes);
        saved_total += saved;texture_total += texture_bytes;
        unsigned compact_bytes = b->bytes;
        assert(animaltown_model_retain(retained) == 1 && b->bytes == compact_bytes);
        DndArray *probe = calloc(1, sizeof(*probe) + 1);assert(probe);
        probe->length = probe->element_size = 1;
        assert(dnd_model_copy(retained, 0, probe, 1, 0) == -1);
        assert(dnd_model_copy(retained, 0, probe, 1, 1) == -1);
        assert(dnd_model_unload(retained) == -1 && dnd_model_restore_begin(retained) == -1);
        free(probe);
        /* Reuse freed allocation size classes before evaluating compact data. */
        size_t churn_bytes = ((size_t)length + 63) & ~(size_t)31;
        unsigned char *churn = aligned_alloc(32, churn_bytes);assert(churn);
        memset(churn, 0x5a, churn_bytes);
        int first = dnd_model_instance(baseline, 512 * 1024);
        int second = dnd_model_instance(retained, 512 * 1024);
        assert(first && second);
        compare_animation(first, second, a);same_payload(a, b);
        unsigned texture_hash = 0;
        for (unsigned i = 0; i < b->textures; i++)texture_hash ^= dnd_model_hash(b->texture[i].data, b->texture[i].size);
        /* An instance keeps its compact model alive after the public owner is
         * closed. Dropping the other model must retain shared texture bytes. */
        assert(dnd_model_dispose(retained) == 0 && b->ready);
        assert(dnd_model_instance_dispose(first) == 0 && dnd_model_dispose(baseline) == 0);
        assert(dnd_model_evaluate(dnd_instance_find(second)) == 0);
        unsigned remaining_hash = 0;
        for (unsigned i = 0; i < b->textures; i++)remaining_hash ^= dnd_model_hash(b->texture[i].data, b->texture[i].size);
        assert(remaining_hash == texture_hash);
        assert(dnd_model_instance_dispose(second) == 0);
        free(churn);
        assert(dnd_model_live_handles() == 0);
        printf("Resident model %s: %u bytes recovered; %u original texture bytes retained once\n", argv[file], saved, texture_bytes);
    }
    assert(texture_total == 2621440 && saved_total >= texture_total);
    printf("Resident villagers: %zu MEM1 bytes recovered; geometry, skins, clips, evaluated poses, shared textures and disposal unchanged\n", saved_total);
    return 0;
}
