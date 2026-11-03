#include <assert.h>
#include <stdio.h>
#include "../tools/native-texture-pages.c"
static int builds,shutdowns;
int dnd_ipl_font_build(uint8_t *atlas,size_t bytes,int32_t *metadata,size_t count){
    assert(bytes==512*256*2&&count==DND_IPL_METADATA);builds++;
    memset(metadata,0,count*4);metadata[0]=24;metadata[1]=24;
    for(int y=0;y<256;y++)for(int x=0;x<512;x++){
        int at=((y/4)*128+x/4)*32+(y%4*4+x%4)*2;
        atlas[at]=(uint8_t)(y/32*16+x/32);atlas[at+1]=(uint8_t)(x%32+y%32);
    }return 1;
}
void dnd_ipl_font_shutdown(void){shutdowns++;}
int main(void){
    DndArray *pixels=calloc(1,sizeof(DndArray)+32768),*metrics=calloc(1,sizeof(DndArray)+DND_IPL_METADATA*4);
    assert(pixels&&metrics);pixels->element_size=1;pixels->length=32768;metrics->element_size=4;metrics->length=DND_IPL_METADATA;
    assert(!animaltown_ipl_page(6,pixels,metrics)&&!animaltown_ipl_page(0,NULL,metrics));
    for(int page=0;page<6;page++){
        assert(animaltown_ipl_page(page,pixels,metrics));
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){
            int glyph=page*16+y/32*4+x/32,at=((y/4)*32+x/4)*32+(y%4*4+x%4)*2;
            assert(pixels->data[at]==(glyph<95?glyph:0));
            assert(pixels->data[at+1]==(glyph<95?x%32+y%32:0));
        }
    }
    assert(builds==1&&source==NULL&&shutdowns==1);
    assert(animaltown_ipl_page(0,pixels,metrics)&&builds==2);
    assert(animaltown_ipl_page(-1,pixels,metrics)&&source==NULL&&shutdowns==2);
    free(pixels);free(metrics);puts("IPL paging: all 95 glyphs, padding, reset and reload passed");return 0;
}
