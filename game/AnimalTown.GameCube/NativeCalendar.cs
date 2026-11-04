internal static class NativeCalendar
{
    // Replaced by tools/patch-native.ps1 after AOT. This deliberately fails on
    // the desktop so a missing native bridge cannot silently use a fake clock.
    internal static int Read()=>throw new System.PlatformNotSupportedException("Native RTC bridge required");
    internal static int CardError()=>0;
    internal static int ReadClock()=>throw new System.PlatformNotSupportedException("Native RTC bridge required");
}
