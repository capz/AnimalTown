/* SPDX-License-Identifier: MIT. libc uses the console RTC, without a network. */
#include <time.h>
int animaltown_card_debug=0;
int animaltown_calendar(void) {
    time_t now=time(0);
    struct tm calendar;
    localtime_r(&now,&calendar);
    int day=(int)((now-6*3600)/86400);
    int month=(calendar.tm_year+1900)*12+calendar.tm_mon;
    /* 17-bit day + 15-bit month; both remain positive for supported saves. */
    return (month<<17)|(day&0x1ffff);
}
