#ifndef ANIMALTOWN_RIM_POLICY_H
#define ANIMALTOWN_RIM_POLICY_H
static inline int town_rim_percent(int percent){return percent<0?0:percent>200?200:percent;}
static inline unsigned char town_rim_channel(int base,int percent){return (unsigned char)(base*town_rim_percent(percent)/100);}
#endif
