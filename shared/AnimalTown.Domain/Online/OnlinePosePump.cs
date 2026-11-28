namespace AnimalTown.Online;

// Nonblocking byte transport. -1 means would block, 0 on read means EOF.
public interface IOnlinePoseStream
{
    int TryRead(byte[] buffer, int offset, int count);
    int TryWrite(byte[] buffer, int offset, int count);
}

public interface IOnlineMessageSink
{
    // Called synchronously with borrowed bytes, valid only until the next read.
    bool Apply(OnlineFrameHeader header, byte[] bytes, int offset, int count);
}

public sealed class OnlinePosePump : IDisposable
{
    const int IoSteps = 4, MoveInterval = 50, HeartbeatInterval = 5000;
    const int SilentLimit = 15000, PoseHold = 1000;
    readonly IOnlinePoseStream stream;
    readonly IOnlineMessageSink? messages;
    readonly byte[] pending = new byte[OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize];
    int pendingLength;
    readonly byte[] input = new byte[OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize];
    readonly byte[] output = new byte[OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize];
    readonly OnlineActorPosition[] actors = new OnlineActorPosition[OnlineWire.ActorCount];
    readonly long[] updated = new long[OnlineWire.ActorCount];
    readonly bool[] visible = new bool[OnlineWire.ActorCount];
    int received, needed = OnlineWire.HeaderSize;
    int sent, sendLength;
    OnlineFrameHeader incoming;
    OnlineMovementInput desired;
    long lastReceived, nextMove, nextHeartbeat;
    uint nextSequence;

    public uint Epoch { get; private set; }
    public byte LocalSlot { get; private set; }
    public bool Reviewing { get; private set; }
    public bool Closed { get; private set; }

    public OnlinePosePump(IOnlinePoseStream stream, byte[] ticket, long now, IOnlineMessageSink? messages = null)
    {
        if (ticket.Length != OnlineWire.GameplayTicketSize)
            throw new ArgumentException("The gameplay ticket has an invalid size.");
        this.stream = stream; this.messages = messages;
        lastReceived = now;
        sendLength = OnlineWire.HeaderSize + ticket.Length;
        OnlineWire.TryWriteHeader(output, 0, output.Length,
            new OnlineFrameHeader(OnlineFrameType.GameplayTicket, ticket.Length, 0, 0));
        Array.Copy(ticket, 0, output, OnlineWire.HeaderSize, ticket.Length);
    }

    public void SetInput(OnlineMovementInput value) => desired = value;
    public void Dispose()
    {
        Closed = true; desired = default;
        Array.Clear(pending, 0, pending.Length); Array.Clear(input, 0, input.Length); Array.Clear(output, 0, output.Length);
        Array.Clear(actors, 0, actors.Length); Array.Clear(visible, 0, visible.Length); Array.Clear(updated, 0, updated.Length);
        pendingLength = sendLength = sent = received = 0;
    }
    public bool Queue(OnlineFrameType type, byte[] payload, int offset, int count, out uint sequence)
    {
        sequence = 0;
        if (Closed || Epoch == 0 || pendingLength != 0 || nextSequence == uint.MaxValue ||
            type is not (OnlineFrameType.Command or OnlineFrameType.BulkPage) ||
            payload is null || offset < 0 || count < 0 || offset > payload.Length - count ||
            Reviewing && LocalSlot != 0) return false;
        uint candidate = nextSequence + 1;
        if (!OnlineWire.TryWriteHeader(pending, 0, pending.Length, new OnlineFrameHeader(type, count, Epoch, candidate))) return false;
        Array.Copy(payload, offset, pending, OnlineWire.HeaderSize, count);
        pendingLength = OnlineWire.HeaderSize + count; nextSequence = sequence = candidate;
        return true;
    }

    public bool TryGetActor(byte slot, long now, out OnlineActorPosition position)
    {
        position = default;
        if (slot >= actors.Length || !visible[slot] || now - updated[slot] > PoseHold)
            return false;
        position = actors[slot];
        return true;
    }

    public void Pump(long now)
    {
        if (Closed) return;
        if (now - lastReceived >= SilentLimit)
        {
            Closed = true;
            return;
        }
        Receive(now);
        if (!Closed) Transmit(now);
    }

    void Receive(long now)
    {
        for (int i = 0; i < IoSteps; i++)
        {
            int count = stream.TryRead(input, received, needed - received);
            if (count == -1) return;
            if (count <= 0 || count > needed - received) { Closed = true; return; }
            received += count;
            if (received != needed) continue;
            if (needed == OnlineWire.HeaderSize)
            {
                if (!OnlineWire.TryReadHeader(input, 0, received, out incoming) ||
                    incoming.PayloadLength > input.Length - OnlineWire.HeaderSize)
                { Closed = true; return; }
                needed += incoming.PayloadLength;
                if (received != needed) continue;
            }
            if (!Apply(now)) { Closed = true; return; }
            Array.Clear(input, 0, needed);
            lastReceived = now;
            received = 0;
            needed = OnlineWire.HeaderSize;
        }
    }

    bool Apply(long now)
    {
        if (Epoch == 0)
        {
            if (incoming.Type != OnlineFrameType.Pong || incoming.Epoch == 0 ||
                incoming.Sequence >= OnlineWire.ActorCount)
                return false;
            Epoch = incoming.Epoch;
            LocalSlot = (byte)incoming.Sequence;
            return true;
        }
        if (incoming.Epoch != Epoch) return false;
        switch (incoming.Type)
        {
            case OnlineFrameType.SessionState:
                byte state = input[OnlineWire.HeaderSize];
                if (state > 1) return false;
                Reviewing = state != 0;
                if (Reviewing) desired = default;
                return true;
            case OnlineFrameType.ActorPosition:
                if (!OnlineWire.TryReadPosition(input, 0, received, out _, out var position)) return false;
                actors[position.Slot] = position;
                updated[position.Slot] = now;
                visible[position.Slot] = true;
                return true;
            case OnlineFrameType.ReliableEvent:
            case OnlineFrameType.SnapshotPage:
            case OnlineFrameType.BulkPage:
                return messages != null && messages.Apply(incoming, input, OnlineWire.HeaderSize, incoming.PayloadLength);
            default:
                return false;
        }
    }

    void Transmit(long now)
    {
        for (int i = 0; i < IoSteps; i++)
        {
            if (sent == sendLength)
            {
                Array.Clear(output, 0, sendLength);
                sent = sendLength = 0;
                if (Epoch == 0) return;
                if (now >= nextHeartbeat)
                {
                    OnlineWire.TryWriteHeader(output, 0, output.Length,
                        new OnlineFrameHeader(OnlineFrameType.Heartbeat, 0, Epoch, 0));
                    sendLength = OnlineWire.HeaderSize;
                    nextHeartbeat = now + HeartbeatInterval;
                }
                else if (pendingLength != 0)
                {
                    Array.Copy(pending, output, pendingLength); sendLength = pendingLength;
                    Array.Clear(pending, 0, pendingLength); pendingLength = 0;
                }
                else if (!Reviewing && now >= nextMove)
                {
                    if (nextSequence == uint.MaxValue ||
                        !OnlineWire.TryWriteInput(output, 0, output.Length, Epoch, ++nextSequence, desired))
                    { Closed = true; return; }
                    sendLength = OnlineWire.HeaderSize + OnlineWire.InputSize;
                    nextMove = now + MoveInterval;
                }
                else return;
            }
            int count = stream.TryWrite(output, sent, sendLength - sent);
            if (count == -1) return;
            if (count <= 0 || count > sendLength - sent) { Closed = true; return; }
            sent += count;
        }
    }
}
