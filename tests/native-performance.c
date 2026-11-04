/* Isolated accounting fixture; gcc -std=c11 -Wall -Wextra -Werror
 * tests/native-performance.c -o /tmp/animaltown-performance && /tmp/animaltown-performance */
#include <assert.h>
#include <stddef.h>
#define SLOTS 4
#define TEXTURE 2
#define DND_MODEL_SLOTS 3
static struct {int kind,bytes;} slots[SLOTS];
typedef struct {int token;} Model;
static Model *dnd_models[DND_MODEL_SLOTS];
static int reads;
static int dnd_model_stat(int token,int kind) {
    assert(token==7||token==8);assert(kind==6);reads++;return 4096;
}
#include "../tools/native-performance.inc"
int main(void) {
    assert(animaltown_performance_memory(2)==0);
    slots[0].kind=TEXTURE;slots[0].bytes=1024;
    slots[1].kind=3;slots[1].bytes=9999; /* meshes are not textures */
    slots[2].kind=TEXTURE;slots[2].bytes=256;
    assert(animaltown_performance_memory(2)==1280);
    Model first={7},second={8};dnd_models[1]=&first;dnd_models[2]=&second;
    assert(animaltown_performance_memory(2)==5376);assert(reads==1);
    slots[0].kind=0;slots[0].bytes=1024; /* stale freed slot */
    assert(animaltown_performance_memory(2)==4352);
    dnd_models[1]=NULL;dnd_models[2]=NULL;
    assert(animaltown_performance_memory(2)==256);
    assert(animaltown_performance_memory(0)==-1); /* host has no MEM1 */
    assert(animaltown_performance_memory(99)==-1);
    return 0;
}
