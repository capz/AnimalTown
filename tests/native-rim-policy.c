#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include "native-rim-policy.h"
int main(void){
    assert(town_rim_percent(INT_MIN)==0&&town_rim_percent(INT_MAX)==200);
    for(int p=-10;p<=210;p++){
        int bounded=p<0?0:p>200?200:p;
        assert(town_rim_channel(92,p)==92*bounded/100);
        assert(town_rim_channel(108,p)==108*bounded/100);
        assert(town_rim_channel(124,p)==124*bounded/100);
    }
    assert(town_rim_channel(124,0)==0&&town_rim_channel(124,100)==124&&town_rim_channel(124,200)==248);
    puts("Rim intensity: bounded 0..200%, original 100% color, zero/maximum and overflow-safe inputs passed");
}
