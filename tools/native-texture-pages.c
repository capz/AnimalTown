#include "dnd_managed.h"
#include "dnd_ipl_font.h"
#include <stdlib.h>
#include <string.h>
static unsigned char *source;
static int32_t original[DND_IPL_METADATA];
static void reset_source(void){free(source);source=NULL;dnd_ipl_font_shutdown();}
int animaltown_ipl_page(int page,DndArray *pixels,DndArray *metrics){
    if(page<0){reset_source();return 1;}
    if(page>5||!pixels||pixels->element_size!=1||pixels->length!=128*128*2||!metrics||metrics->element_size!=4||metrics->length!=DND_IPL_METADATA)return 0;
    if(!source){source=malloc(DND_IPL_WIDTH*DND_IPL_HEIGHT*2);if(!source)return 0;if(!dnd_ipl_font_build(source,DND_IPL_WIDTH*DND_IPL_HEIGHT*2,original,DND_IPL_METADATA)){reset_source();return 0;}
        for(int g=0;g<DND_IPL_GLYPHS;g++){
            int left=32,top=32,right=-1,bottom=-1,sx=g%16*32,sy=g/16*32;
            for(int y=0;y<original[1];y++)for(int x=0;x<original[0];x++){
                int ax=sx+x,ay=sy+y,at=((ay/4)*128+ax/4)*32+(ay%4*4+ax%4)*2;
                if(!source[at])continue;
                if(x<left)left=x;
                if(x>right)right=x;
                if(y<top)top=y;
                if(y>bottom)bottom=y;
            }
            int *m=original+8+g*5;
            if(right<0){m[0]=m[1]=m[2]=m[3]=0;continue;}
            /* One transparent texel retains the original bilinear coverage. */
            if(left>0)left--;
            if(top>0)top--;
            if(right+1<original[0])right++;
            if(bottom+1<original[1])bottom++;
            m[0]=left;m[1]=top;m[2]=right-left+1;m[3]=bottom-top+1;
        }
    }
    memset(pixels->data,0,128*128*2);memcpy(metrics->data,original,sizeof(original));
    for(int local=0;local<16;local++){
        int glyph=page*16+local;if(glyph>=DND_IPL_GLYPHS)break;
        int sx=glyph%16*8,sy=glyph/16*8,dx=local%4*8,dy=local/4*8;
        for(int row=0;row<8;row++)memcpy((unsigned char*)pixels->data+((dy+row)*32+dx)*32,source+((sy+row)*128+sx)*32,8*32);
    }
    if(page==5)reset_source();
    return 1;
}
