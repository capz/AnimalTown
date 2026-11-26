using System.Net.WebSockets;
using AnimalTown.Online;

// Both the raw staging listener and WSS run the same authenticated protocol.
// Keep the message budget independent of the caller's short-read boundaries.
sealed class GameplayWebSocketStream(WebSocket socket) : Stream
{
    const int MessageLimit = OnlineWire.HeaderSize + OnlineWire.MaxPayloadSize;
    int messageBytes;
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        for (int empty = 0; empty < 4; empty++)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close) return 0;
            if (received.MessageType != WebSocketMessageType.Binary || received.Count > MessageLimit - messageBytes)
                throw new IOException("Invalid gameplay WebSocket message");
            messageBytes += received.Count;
            if (received.EndOfMessage) messageBytes = 0;
            if (received.Count != 0) return received.Count;
        }
        throw new IOException("Empty gameplay WebSocket message limit");
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0 || buffer.Length > MessageLimit)
            throw new IOException("Gameplay WebSocket message limit");
        return socket.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
