#include <assert.h>
#include <time.h>
static struct tm fixture;
static time_t fake_now(time_t *result) { if(result)*result=0;return 0; }
static struct tm *fake_local(const time_t *now,struct tm *target) { (void)now;*target=fixture;return target; }
#define time fake_now
#define localtime_r fake_local
#include "../tools/native-calendar.c"
#undef time
#undef localtime_r
int main(void) {
    fixture.tm_year=126;fixture.tm_mon=10;fixture.tm_mday=20;fixture.tm_hour=23;fixture.tm_min=59;fixture.tm_sec=59;fixture.tm_wday=5;
    int clock=animaltown_clock();
    assert((clock&63)==59&&((clock>>6)&31)==23&&animaltown_clock_second()==59);
    fixture.tm_min=0;fixture.tm_hour=0;fixture.tm_sec=0;fixture.tm_mday=21;
    assert(animaltown_clock_second()==59); /* No second independent RTC read. */
    clock=animaltown_clock();assert((clock&63)==0&&((clock>>11)&31)==21&&animaltown_clock_second()==0);
    fixture.tm_year=99;animaltown_clock();assert(animaltown_clock_second()==0);
    return 0;
}
