namespace AnimalTown.Online;

// Masks must come from a cryptographic source seeded with fresh platform entropy.
public interface IOnlineMaskSource { uint NextMask(); }

// RFC 6455 binary messages over an already authenticated TLS byte stream.
// Caller owns the TLS stream. All retained framing buffers are fixed-size.
public sealed class OnlineWebSocketStream : IOnlinePoseStream, IDisposable
{
    public const int MessageLimit = OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize;
    readonly IOnlinePoseStream transport;
    readonly IOnlineMaskSource masks;
    readonly byte[] header = new byte[4];
    readonly byte[] output = new byte[MessageLimit + 8];
    readonly byte[] control = new byte[125];
    readonly byte[] reply = new byte[133];
    int headerUsed, headerNeeded = 2, remaining, messageBytes, opcode, controlUsed;
    int outputUsed, outputSent, replyUsed, replySent;
    bool fragmented, final, closing;
    public bool Closed { get; private set; }
    public bool ProtocolError { get; private set; }

    public OnlineWebSocketStream(IOnlinePoseStream transport, IOnlineMaskSource masks)
    { this.transport = transport; this.masks = masks; }

    public int TryRead(byte[] buffer, int offset, int count)
    {
        Range(buffer, offset, count);
        if (count == 0) return 0;
        Flush();
        if (Closed) return 0;
        if (closing) return -1;
        for (int work = 0; work < 4; work++)
        {
            if (headerUsed < headerNeeded)
            {
                int read = transport.TryRead(header, headerUsed, headerNeeded - headerUsed);
                if (read == -1) return -1;
                if (read <= 0 || read > headerNeeded - headerUsed) { Closed = true; return 0; }
                headerUsed += read;
                if (headerUsed < headerNeeded) continue;
                if (headerNeeded == 2 && (header[1] & 127) == 126) { headerNeeded = 4; continue; }
                if (!Header()) return Fail();
            }
            if (opcode <= 2)
            {
                if (remaining == 0) { NextFrame(); continue; }
                int wanted = Math.Min(count, remaining);
                int read = transport.TryRead(buffer, offset, wanted);
                if (read == -1) return -1;
                if (read <= 0 || read > wanted) { Closed = true; return 0; }
                remaining -= read;
                if (remaining == 0) NextFrame();
                return read;
            }
            if (remaining > 0)
            {
                int read = transport.TryRead(control, controlUsed, remaining);
                if (read == -1) return -1;
                if (read <= 0 || read > remaining) { Closed = true; return 0; }
                controlUsed += read; remaining -= read;
                if (remaining != 0) continue;
            }
            if (opcode == 8)
            {
                if (controlUsed == 1 || controlUsed >= 2 && !ValidClose()) return Fail();
                // Discard unsent application data; finish a frame already on the wire.
                if (outputSent == 0) { Array.Clear(output, 0, outputUsed); outputUsed = 0; }
                QueueControl(8); closing = true; Flush();
                return Closed ? 0 : -1;
            }
            if (opcode == 9)
            {
                // Never replace an unsent pong; backpressure the incoming stream.
                if (replySent != replyUsed) return -1;
                QueueControl(10); Flush();
            }
            NextFrame();
        }
        return -1;
    }

    public int TryWrite(byte[] buffer, int offset, int count)
    {
        Range(buffer, offset, count);
        if (count == 0) return 0;
        if (count > MessageLimit) throw new ArgumentOutOfRangeException(nameof(count));
        Flush();
        if (Closed) return 0;
        if (closing || outputSent != outputUsed || replySent != replyUsed) return -1;
        outputUsed = Frame(output, 2, buffer, offset, count);
        outputSent = 0;
        Flush();
        // Accepted into owned staging even when the TLS write would block.
        return Closed ? 0 : count;
    }

    bool Header()
    {
        int marker = header[1] & 127;
        if ((header[0] & 112) != 0 || (header[1] & 128) != 0 || marker == 127) return false;
        final = (header[0] & 128) != 0; opcode = header[0] & 15;
        remaining = marker == 126 ? header[2] * 256 + header[3] : marker;
        if (marker == 126 && remaining < 126) return false;
        controlUsed = 0;
        if (opcode >= 8) return opcode <= 10 && final && remaining <= 125;
        if (opcode != 0 && opcode != 2 || opcode == 0 != fragmented) return false;
        if (opcode == 2) messageBytes = 0;
        if (remaining > MessageLimit - messageBytes) return false;
        messageBytes += remaining; fragmented = !final;
        return true;
    }

    void NextFrame()
    {
        if (opcode <= 2 && final) messageBytes = 0;
        headerUsed = 0; headerNeeded = 2; controlUsed = 0;
    }

    int Fail() { ProtocolError = true; Dispose(); return 0; }

    public void Dispose()
    {
        Closed = true;
        Array.Clear(header, 0, header.Length); Array.Clear(output, 0, output.Length);
        Array.Clear(control, 0, control.Length); Array.Clear(reply, 0, reply.Length);
        outputUsed = outputSent = replyUsed = replySent = 0;
    }

    void QueueControl(int kind)
    { replyUsed = Frame(reply, kind, control, 0, controlUsed); replySent = 0; }

    int Frame(byte[] destination, int kind, byte[] source, int offset, int count)
    {
        destination[0] = (byte)(128 | kind);
        int maskAt = 2;
        if (count < 126) destination[1] = (byte)(128 | count);
        else { destination[1] = 254; destination[2] = (byte)(count >> 8); destination[3] = (byte)count; maskAt = 4; }
        uint mask = masks.NextMask();
        for (int i = 0; i < 4; i++) destination[maskAt + i] = (byte)(mask >> (i * 8));
        for (int i = 0; i < count; i++) destination[maskAt + 4 + i] = (byte)(source[offset + i] ^ destination[maskAt + (i & 3)]);
        return maskAt + 4 + count;
    }

    void Flush()
    {
        if (Closed) return;
        for (int step = 0; step < 4; step++)
        {
            bool data = outputSent != outputUsed;
            if (!data && replySent == replyUsed) { if (closing) Closed = true; return; }
            byte[] bytes = data ? output : reply;
            int at = data ? outputSent : replySent, needed = (data ? outputUsed : replyUsed) - at;
            int sent = transport.TryWrite(bytes, at, needed);
            if (sent == -1) return;
            if (sent <= 0 || sent > needed) { Closed = true; return; }
            if (data)
            {
                outputSent += sent;
                if (outputSent == outputUsed) { Array.Clear(output, 0, outputUsed); outputSent = outputUsed = 0; }
            }
            else
            {
                replySent += sent;
                if (replySent == replyUsed) { Array.Clear(reply, 0, replyUsed); replySent = replyUsed = 0; }
            }
        }
    }

    bool ValidClose()
    {
        int code = control[0] * 256 + control[1];
        if (code < 1000 || code >= 5000 || code is 1004 or 1005 or 1006 or 1015 || code > 1014 && code < 3000) return false;
        for (int at = 2; at < controlUsed;)
        {
            int first = control[at++];
            if (first < 128) continue;
            int length = first >= 194 && first <= 223 ? 1 : first >= 224 && first <= 239 ? 2 : first >= 240 && first <= 244 ? 3 : -1;
            if (length < 0 || at + length > controlUsed) return false;
            int second = control[at];
            if (first == 224 && second < 160 || first == 237 && second >= 160 || first == 240 && second < 144 || first == 244 && second >= 144) return false;
            for (int i = 0; i < length; i++) if ((control[at++] & 192) != 128) return false;
        }
        return true;
    }

    static void Range(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
    }
}
