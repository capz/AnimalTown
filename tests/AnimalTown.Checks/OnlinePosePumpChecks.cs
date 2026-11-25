using AnimalTown.Online;

internal static class OnlinePosePumpChecks
{
    sealed class Stream : IOnlinePoseStream
    {
        readonly byte[] rx = new byte[128];
        public readonly byte[] Tx = new byte[512];
        public int Written;
        int length, offset;
        public int Reads, Writes;

        public void Feed(byte[] bytes, int count)
        {
            Array.Copy(bytes, rx, count);
            offset = 0;
            length = count;
        }

        public int TryRead(byte[] buffer, int at, int count)
        {
            Reads++;
            if (offset == length) return -1;
            int size = Math.Min(3, Math.Min(count, length - offset));
            Array.Copy(rx, offset, buffer, at, size);
            offset += size;
            return size;
        }

        public int TryWrite(byte[] buffer, int at, int count)
        {
            Writes++;
            int size = Math.Min(4, count);
            Array.Copy(buffer, at, Tx, Written, size);
            Written += size;
            return size;
        }
    }

    public static void Run(Action<bool, string> check)
    {
        var stream = new Stream();
        byte[] ticket = new byte[OnlineWire.GameplayTicketSize];
        ticket[0] = 83;
        var pump = new OnlinePosePump(stream, ticket, 0);
        pump.Pump(0);
        check(stream.Written == 16 && stream.Reads <= 4 && stream.Writes <= 4,
            "Live protocol pump limits nonblocking I/O calls per update");
        for (int i = 0; i < 4; i++) pump.Pump(0);
        check(stream.Written == OnlineWire.HeaderSize + OnlineWire.GameplayTicketSize &&
            stream.Tx[OnlineWire.HeaderSize] == 83, "Fragmented socket writes preserve the whole ticket");

        var frame = new byte[128];
        OnlineWire.TryWriteHeader(frame, 0, frame.Length, new OnlineFrameHeader(OnlineFrameType.Pong, 0, 18, 2));
        stream.Feed(frame, OnlineWire.HeaderSize);
        pump.Pump(1);
        check(pump.Epoch == 18 && pump.LocalSlot == 2, "Live protocol joins the assigned epoch and seat");

        var position = new OnlineActorPosition(1, 0, 12 * OnlineWire.PositionScale, 0, 0, 0);
        OnlineWire.TryWritePosition(frame, 0, frame.Length, 18, 1, position);
        stream.Feed(frame, OnlineWire.HeaderSize + OnlineWire.PositionSize);
        for (int i = 0; i < 4; i++) pump.Pump(11);
        check(pump.TryGetActor(1, 11, out var actor) && actor.X == position.X &&
            !pump.TryGetActor(1, 1012, out _), "Partial pose reads publish once and disconnected actors expire");

        OnlineWire.TryWriteHeader(frame, 0, frame.Length, new OnlineFrameHeader(OnlineFrameType.SessionState, 1, 18, 1));
        frame[OnlineWire.HeaderSize] = 1;
        stream.Feed(frame, OnlineWire.HeaderSize + 1);
        for (int i = 0; i < 2; i++) pump.Pump(13);
        int before = stream.Written;
        pump.SetInput(new OnlineMovementInput(100, 0, false));
        long baseline = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) pump.Pump(300);
        long used = GC.GetAllocatedBytesForCurrentThread() - baseline;
        check(pump.Reviewing && before == stream.Written && used == 0,
            "Paused movement stays suppressed without allocating or dropping the transport");

        OnlineWire.TryWriteHeader(frame, 0, frame.Length, new OnlineFrameHeader(OnlineFrameType.Pong, 0, 19, 1));
        stream.Feed(frame, OnlineWire.HeaderSize);
        pump.Pump(301);
        check(pump.Closed, "A frame from a different epoch closes the live protocol");
    }
}
