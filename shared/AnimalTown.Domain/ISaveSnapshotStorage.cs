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

// A verified, immutable view of a record while the caller owns the card.
public interface ISaveWordReader
{
    int ReadWord(int byteOffset);
}

public interface ISaveVerifiedRecordStorage
{
    ISaveWordReader OpenVerifiedRecord(string name,int sequence,int length,uint checksum);
}

// Finish verified legacy conversion before the caller retains a large newly
// encoded record. Preparation preserves the existing logical records/revision.
public interface ISaveWritePreparationStorage
{
    void PrepareWrite();
}
