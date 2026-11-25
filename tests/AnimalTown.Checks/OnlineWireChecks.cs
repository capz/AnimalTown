using AnimalTown;
using AnimalTown.Online;

internal static class OnlineWireChecks
{
    public static void Run(Action<bool, string> check)
    {
        const int offset = 5;
        const int inputBytes = OnlineWire.HeaderSize + OnlineWire.InputSize;
        const int positionBytes = OnlineWire.HeaderSize + OnlineWire.PositionSize;
        const uint epoch = 0x12345678;
        const uint sequence = 0xA0B0C0D0;
        var buffer = new byte[OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize + offset];
        var input = new OnlineMovementInput(-127, 126, true);

        Array.Fill(buffer, (byte)0xFF);
        check(OnlineWire.TryWriteInput(buffer, offset, inputBytes, epoch, sequence, input), "Movement frame writes at offset");
        byte[] inputFixture = { 1, 1, 3, 0, 0x78, 0x56, 0x34, 0x12, 0xD0, 0xC0, 0xB0, 0xA0, 0x81, 0x7E, 1 };
        check(Matches(buffer, offset, inputFixture), "Movement frame matches canonical little-endian fixture");
        check(buffer[offset - 1] == 0xFF && buffer[offset + inputBytes] == 0xFF, "Movement writer preserves adjacent bytes");
        check(OnlineWire.TryReadHeader(buffer, offset, OnlineWire.HeaderSize, out var header) &&
            header.Epoch == epoch && header.Sequence == sequence && header.PayloadLength == OnlineWire.InputSize,
            "The complete header decodes before its payload");
        for (int length = 0; length < inputBytes; length++)
            check(OnlineWire.ReadFrame(buffer, offset, length, out _, out var consumed) == OnlineFrameRead.Incomplete && consumed == 0,
                "Partial frame is preserved without consuming bytes");
        check(OnlineWire.TryReadInput(buffer, offset, inputBytes, out header, out var decodedInput) && header.Epoch == epoch &&
            header.Sequence == sequence && decodedInput.X == -127 && decodedInput.Z == 126 && decodedInput.Run,
            "Movement frame reads signed input and acknowledged sequence fields");

        var position = new OnlineActorPosition(2, 0, 0x123456, 0x560708, 0x8192, 0xE1020304);
        int next = offset + inputBytes;
        check(OnlineWire.TryWritePosition(buffer, next, positionBytes, epoch, sequence + 1, position), "Position appends to a complete frame");
        byte[] positionFixture = { 1, 2, 16, 0, 0x78, 0x56, 0x34, 0x12, 0xD1, 0xC0, 0xB0, 0xA0,
            2, 0, 0x56, 0x34, 0x12, 0, 8, 7, 0x56, 0, 0x92, 0x81, 4, 3, 2, 0xE1 };
        check(Matches(buffer, next, positionFixture), "Position frame matches canonical little-endian fixture");
        check(OnlineWire.ReadFrame(buffer, offset, inputBytes + positionBytes, out header, out var firstBytes) ==
            OnlineFrameRead.Complete && firstBytes == inputBytes && header.Type == OnlineFrameType.MovementInput,
            "A coalesced read consumes the first frame only");
        check(OnlineWire.TryReadPosition(buffer, offset + firstBytes, positionBytes, out header, out var decodedPosition) &&
            header.Sequence == sequence + 1 && decodedPosition.Slot == position.Slot && decodedPosition.Room == position.Room &&
            decodedPosition.X == position.X && decodedPosition.Z == position.Z && decodedPosition.Facing == position.Facing &&
            decodedPosition.AcknowledgedInput == position.AcknowledgedInput,
            "Position frame reads exact fixed-point axes, rotation, room and input acknowledgment");
        check(!OnlineWire.TryReadInput(buffer, next, positionBytes, out _, out _), "Typed reader rejects the wrong frame kind");

        int body = offset + OnlineWire.HeaderSize;
        buffer[body] = 128;
        check(!OnlineWire.TryReadInput(buffer, offset, inputBytes, out _, out _), "Movement excludes noncanonical signed axis -128");
        buffer[body] = 0;
        buffer[body + 2] = 2;
        check(!OnlineWire.TryReadInput(buffer, offset, inputBytes, out _, out _), "Movement excludes unrecognized flag bits");
        check(!OnlineWire.TryWriteInput(buffer, offset, inputBytes, epoch, sequence, new OnlineMovementInput(0, -128, false)),
            "Movement writer excludes an invalid Z axis");

        body = next + OnlineWire.HeaderSize;
        buffer[body] = OnlineWire.ActorCount;
        check(!OnlineWire.TryReadPosition(buffer, next, positionBytes, out _, out _), "Position excludes slots outside the three-player roster");
        buffer[body] = 0;
        buffer[body + 1] = 255;
        check(!OnlineWire.TryReadPosition(buffer, next, positionBytes, out _, out _), "Position excludes unknown room IDs");
        buffer[body + 1] = 0;
        buffer[body + 5] = 0x80;
        check(!OnlineWire.TryReadPosition(buffer, next, positionBytes, out _, out _), "Position excludes negative coordinates");
        check(!OnlineWire.TryWritePosition(buffer, next, positionBytes, epoch, sequence,
            new OnlineActorPosition(0, 0, Town.Width * OnlineWire.PositionScale, 0, 0, 0)), "Position excludes the first coordinate past the town");
        check(OnlineWire.TryWritePosition(buffer, next, positionBytes, epoch, sequence,
            new OnlineActorPosition(0, (byte)BuildingWorld.Space(7), OnlineWire.PositionScale, OnlineWire.PositionScale, 0, 0)),
            "Position accepts established indoor space IDs");

        buffer[offset] = OnlineWire.Version + 1;
        check(OnlineWire.ReadFrame(buffer, offset, OnlineWire.HeaderSize, out _, out _) == OnlineFrameRead.Invalid,
            "Unsupported version is rejected before collecting payload");
        buffer[offset] = OnlineWire.Version;
        buffer[offset + 1] = 0xFF;
        check(OnlineWire.ReadFrame(buffer, offset, inputBytes, out _, out _) == OnlineFrameRead.Invalid, "Unknown frame type rejected");
        buffer[offset + 1] = (byte)OnlineFrameType.MovementInput;
        buffer[offset + 2] = 64;
        check(OnlineWire.ReadFrame(buffer, offset, OnlineWire.HeaderSize, out _, out _) == OnlineFrameRead.Invalid,
            "Type-specific size rejected before buffering more bytes");
        check(!OnlineWire.TryWriteHeader(buffer, offset, inputBytes, new OnlineFrameHeader(OnlineFrameType.SnapshotPage,
            OnlineWire.MaxPayloadSize + 1, epoch, sequence)), "Bulk frame writer enforces frame cap");
        check(OnlineWire.TryWriteHeader(buffer, offset, OnlineWire.HeaderSize,
            new OnlineFrameHeader(OnlineFrameType.SnapshotPage, OnlineWire.MaxPayloadSize, epoch, sequence)),
            "Maximum bulk frame header accepted");
        check(OnlineWire.ReadFrame(buffer, offset, buffer.Length - offset, out header, out int bytes) == OnlineFrameRead.Complete &&
            header.PayloadLength == OnlineWire.MaxPayloadSize && bytes == OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize,
            "Maximum bulk frame does not overflow its total");
        buffer[offset + 3] = 255;
        check(OnlineWire.ReadFrame(buffer, offset, OnlineWire.HeaderSize, out _, out _) == OnlineFrameRead.Invalid,
            "Oversized peer frame is classified invalid with only its header");
        check(!OnlineWire.TryWriteHeader(buffer, offset, inputBytes, new OnlineFrameHeader(OnlineFrameType.GameplayTicket, 31, 0, 0)) &&
            OnlineWire.TryWriteHeader(buffer, offset, inputBytes, new OnlineFrameHeader(OnlineFrameType.GameplayTicket, 32, 0, 0)),
            "Gameplay ticket requires a 32-byte opaque payload");
        check(!OnlineWire.TryWriteHeader(buffer, offset, inputBytes, new OnlineFrameHeader(OnlineFrameType.Pong, 1, epoch, sequence)) &&
            OnlineWire.TryWriteHeader(buffer, offset, inputBytes, new OnlineFrameHeader(OnlineFrameType.Heartbeat, 0, epoch, sequence)),
            "Heartbeat and pong are header-only");
        check(!OnlineWire.TryWriteInput(buffer, int.MaxValue, inputBytes, epoch, sequence, input) &&
            !OnlineWire.TryWriteInput(buffer, offset, int.MaxValue, epoch, sequence, input) &&
            !OnlineWire.TryReadHeader(buffer, -1, inputBytes, out _) &&
            OnlineWire.ReadFrame(buffer, 0, -1, out _, out _) == OnlineFrameRead.Invalid,
            "Range validation rejects invalid slices without throwing or overflowing");

        bool ok = Exercise(buffer, input, position, 128);
        long start = GC.GetAllocatedBytesForCurrentThread();
        ok &= Exercise(buffer, input, position, 10_000);
        long allocation = GC.GetAllocatedBytesForCurrentThread() - start;
        check(ok && allocation == 0, "Steady movement encode/decode uses only caller-owned storage");
    }

    private static bool Matches(byte[] actual, int offset, byte[] expected)
    {
        for (int i = 0; i < expected.Length; ++i)
            if (actual[offset + i] != expected[i]) return false;
        return true;
    }

    private static bool Exercise(byte[] buffer, OnlineMovementInput input, OnlineActorPosition position, uint iterations)
    {
        for (uint sequence = 0; sequence < iterations; sequence++)
            if (!OnlineWire.TryWriteInput(buffer, 0, buffer.Length, 1, sequence, input) ||
                !OnlineWire.TryReadInput(buffer, 0, buffer.Length, out var header, out _) ||
                header.Sequence != sequence ||
                !OnlineWire.TryWritePosition(buffer, 0, buffer.Length, 1, sequence, position) ||
                !OnlineWire.TryReadPosition(buffer, 0, buffer.Length, out header, out _) ||
                header.Sequence != sequence)
                return false;
        return true;
    }
}
