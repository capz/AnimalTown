internal static class CardBlockBridge
{
    // Bound to the pinned SDK's dnd_gc_card_blocks_io by patch-native.ps1.
    internal static int Io(int device,string name,int operation,int block,int identity,byte[] bytes)
        =>throw new System.PlatformNotSupportedException("Native card block bridge required");
}
