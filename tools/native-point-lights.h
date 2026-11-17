/* Bounded nearest-source selection, shared with the host fixture. */
#ifndef ANIMALTOWN_POINT_LIGHTS_H
#define ANIMALTOWN_POINT_LIGHTS_H
#include <math.h>
static inline void town_point_select(const float *points,int count,const float center[3],int selected[2]){
    float best[2]={16,16};selected[0]=selected[1]=-1;
    for(int i=0;i<count;i++){
        const float *p=points+i*5;if(p[4]<=0)continue;
        float x=p[0]-center[0],y=p[1]-center[1],z=p[2]-center[2];
        float score=(x*x+y*y+z*z)/(p[3]*p[3]);
        if(score<best[0]){best[1]=best[0];selected[1]=selected[0];best[0]=score;selected[0]=i;}
        else if(score<best[1]){best[1]=score;selected[1]=i;}
    }
}
static inline void town_point_transform(const float matrix[16],const float p[3],float out[3]){
    for(int r=0;r<3;r++)out[r]=matrix[r]*p[0]+matrix[4+r]*p[1]+matrix[8+r]*p[2]+matrix[12+r];
}
#endif
