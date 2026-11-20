/* SPDX-License-Identifier: MIT. libc uses the console RTC, without a network. */
#include <time.h>
static int animaltown_clock_second_value;
int animaltown_clock(void) {
    time_t now=time(0);struct tm local;
    if(!localtime_r(&now,&local)||local.tm_year<100||local.tm_year>199) {
        animaltown_clock_second_value=0;
        return (1<<11)|(1<<16)|(6<<20); /* 2000-01-01 00:00, Saturday */
    }
    animaltown_clock_second_value=local.tm_sec>=0&&local.tm_sec<60?local.tm_sec:59;
    return local.tm_min|(local.tm_hour<<6)|(local.tm_mday<<11)|((local.tm_mon+1)<<16)|(local.tm_wday<<20)|((local.tm_year-100)<<23);
}
/* ReadClock and ReadSecond share one RTC snapshot, including at minute rollover. */
int animaltown_clock_second(void) { return animaltown_clock_second_value; }
int animaltown_card_debug=0;
int animaltown_card_mount_error=0;
int animaltown_card_recovery_error=0;
int animaltown_calendar(void) {
    time_t now=time(0);
    struct tm calendar;
    localtime_r(&now,&calendar);
    int day=(int)((now-6*3600)/86400);
    int month=(calendar.tm_year+1900)*12+calendar.tm_mon;
    /* 17-bit day + 15-bit month; both remain positive for supported saves. */
    return (month<<17)|(day&0x1ffff);
}
