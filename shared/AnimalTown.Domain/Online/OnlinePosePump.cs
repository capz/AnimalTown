namespace AnimalTown.Online;

// Nonblocking byte transport. -1 means would block, 0 on read means EOF.
public interface IOnlinePoseStream
{
    int TryRead(byte[] buffer, int offset, int count);
    int TryWrite(byte[] buffer, int offset, int count);
}

public sealed class OnlinePosePump
{
    const int IoSteps = 4, MoveInterval = 50, HeartbeatInterval = 5000;
    const int SilentLimit = 15000, PoseHold = 1000;
    readonly IOnlinePoseStream stream;
    readonly byte[] input = new byte[OnlineWire.HeaderSize + 512];
    readonly byte[] output = new byte[OnlineWire.HeaderSize + OnlineWire.GameplayTicketSize];
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

    public OnlinePosePump(IOnlinePoseStream stream, byte[] ticket, long now)
    {
        if (ticket.Length != OnlineWire.GameplayTicketSize)
            throw new ArgumentException("The gameplay ticket has an invalid size.");
        this.stream = stream;
        lastReceived = now;
        sendLength = OnlineWire.HeaderSize + ticket.Length;
        OnlineWire.TryWriteHeader(output, 0, output.Length,
            new OnlineFrameHeader(OnlineFrameType.GameplayTicket, ticket.Length, 0, 0));
        Array.Copy(ticket, 0, output, OnlineWire.HeaderSize, ticket.Length);
    }

    public void SetInput(OnlineMovementInput value) => desired = value;

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
