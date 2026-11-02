/* Regression contract for a libogc adapter that reads sector-aligned data.
 * Uses synthetic card contents; no user card or firmware data is accessed. */
#include <assert.h>
#include <errno.h>
#include <malloc.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
typedef uint32_t u32;
typedef struct {int len;} card_file;
static unsigned char contents[3*8192];
static int reads,closed,read_error=-1,sector_error;
static void identity(int slot){assert(slot==0);}
static int card_errno(int n){return n;}
static int CARD_Open(int slot,const char *name,card_file *file)
{assert(slot==0&&!strcmp(name,"AnimalTown1"));file->len=sizeof(contents);return 0;}
static int CARD_Close(card_file *file){(void)file;closed++;return 0;}
static int CARD_GetSectorSize(int slot,u32 *size){assert(slot==0);*size=8192;return sector_error;}
static int CARD_Read(card_file *file,void *destination,u32 length,u32 offset)
{
    (void)file;assert(length==8192&&offset%8192==0&&offset+length<=sizeof(contents));assert(((uintptr_t)destination&31)==0);
    int index=reads++;if(index==read_error)return -EIO;memcpy(destination,contents+offset,length);return 0;
}
#include "../tools/native-card-read.c.inc"
int main(void)
{
    for(size_t i=0;i<sizeof(contents);i++)contents[i]=(unsigned char)(i*31+i/8192*73);
    unsigned char output[sizeof(contents)+128];memset(output,0xee,sizeof(output));
    assert(dnd_gc_card_read(4,"AnimalTown1",NULL,0)==sizeof(contents)&&reads==0&&closed==1);
    reads=closed=0;assert(dnd_gc_card_read(4,"AnimalTown1",output,32)==32&&reads==1&&closed==1&&!memcmp(output,contents,32)&&output[32]==0xee);
    reads=closed=0;assert(dnd_gc_card_read(4,"AnimalTown1",output,8256)==8256&&reads==2&&closed==1&&!memcmp(output,contents,8256));
    reads=closed=0;assert(dnd_gc_card_read(4,"AnimalTown1",output,sizeof(output))==sizeof(contents)&&reads==3&&closed==1&&!memcmp(output,contents,sizeof(contents)));
    reads=closed=0;read_error=1;assert(dnd_gc_card_read(4,"AnimalTown1",output,sizeof(output))==-EIO&&closed==1);read_error=-1;
    reads=closed=0;sector_error=-ENODEV;assert(dnd_gc_card_read(4,"AnimalTown1",output,32)==-ENODEV&&closed==1&&reads==0);
    puts("Memory-card read: aligned sector reads, prefixes, boundary crossing, clamping and error cleanup passed");return 0;
}
