using System;
using Dolphin.Graphics;

// Keep animated geometry/skins/clips, but release embedded texture bytes after
// the native shared texture cache has acquired its independent GPU storage.
internal static class NativeModels
{
    internal static int Retain(Model model)=>throw new PlatformNotSupportedException("Native animated model retention requires AOT");
}
