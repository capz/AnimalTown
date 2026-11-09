#include <assert.h>
#include <math.h>
#include <stdio.h>
#include "native-face-culling.h"
int main(void){
    for(int flags=0;flags<256;flags++)for(int winding=-1;winding<=1;winding+=2){
        unsigned expected=winding<0?1u:2u;
        assert(town_face_cull(1,flags&4,(float)winding)==expected);
        assert(town_face_cull(0,flags&4,(float)winding)==((flags&4)?expected:0));
    }
    assert(town_face_cull(1,0,0)==2&&town_face_cull(1,0,-0.0f)==2);
    puts("Face culling: all 3D material flags reject backsides, mirrored winding swaps faces, overlay policy retained");
}
