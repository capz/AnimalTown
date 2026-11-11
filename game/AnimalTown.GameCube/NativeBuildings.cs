using System;
using Dolphin.Graphics;

// Opt in only after validation/loading and before creating any instances.
// These static parts remain resident; their source-package streaming is disabled.
internal static class NativeBuildings
{
    internal static int Retain(Model model)=>throw new PlatformNotSupportedException("Native static building retention requires AOT");
}
