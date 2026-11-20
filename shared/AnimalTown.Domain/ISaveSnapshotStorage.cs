namespace AnimalTown;

// Optional bounded-buffer verification for large card records. Small/in-memory
// stores keep the existing byte-array path. A reference includes its full CRC,
// sequence and length; matching never weakens manifest verification.
public interface ISaveSnapshotStorage
{
    bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence);
}

// Verify the complete referenced record while retaining only a small prefix.
// Useful for card/town identity checks before a large transfer checkpoint.
public interface ISaveVerifiedPrefixStorage
{
    byte[] ReadVerifiedPrefix(string name,int count,int sequence,int length,uint checksum);
}
