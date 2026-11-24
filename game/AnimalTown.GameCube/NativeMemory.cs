// Reads actual system MEM1 headroom, including allocations outside graphics.
// A total-free count cannot promise a contiguous block; allocation failures
// remain an independent pressure signal. Call between native render frames.
internal static class NativeMemory
{
    internal const int SnapshotLength=5;
    // Total free, libc free, unclaimed arena, failure sequence, last failed bytes.
    internal static int Read(int[] snapshot)=>throw new System.PlatformNotSupportedException();
}
