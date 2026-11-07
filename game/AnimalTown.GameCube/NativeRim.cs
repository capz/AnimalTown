// Scoped around villager submissions only. The pinned native bridge adds a
// normal-generated lookup and one TEV stage; it never expands indexed models.
internal static class NativeRim
{
    internal static int Enable(int enabled)=>throw new System.PlatformNotSupportedException("Native rim bridge required");
}
