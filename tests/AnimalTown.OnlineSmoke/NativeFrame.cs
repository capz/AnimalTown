// The CPU protocol/AOT fixture links the real connection without the console renderer.
// Native frame diagnostics belong to the full console build.
internal static class NativeFrame
{
    internal static void OnlineProbe(int stage) { }
}
