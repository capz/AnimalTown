#if ANIMALTOWN_ONLINE_PLAY
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Network;
using Dolphin.Diagnostics;
using Dolphin.Timing;

// Connection setup owns no town renderer. TLS configuration and device ticket
// issuance happen before this call, with fresh trust/entropy/time checks.
internal sealed class OnlineGameplayConnection : IDisposable
{
    private readonly RawStream raw;
    private readonly OnlineWebSocketStream websocket;
    private readonly OnlinePosePump pump;
    private bool disposed;
    private OnlineGameplayConnection(RawStream raw, OnlineWebSocketStream websocket, byte[] ticket, IOnlineMessageSink? messages)
    { this.raw = raw; this.websocket = websocket; pump = new OnlinePosePump(websocket, ticket, Now, messages); }
    private static long Now => Clock.Timestamp / 1000;
    internal uint Epoch => pump.Epoch;
    internal byte LocalSlot => pump.LocalSlot;
    internal bool Reviewing => pump.Reviewing;
    internal OnlineId NewCommandId()=>new((ulong)raw.NextMask()|((ulong)raw.NextMask()<<32),(ulong)raw.NextMask()|((ulong)raw.NextMask()<<32));
    internal bool Queue(OnlineFrameType type, byte[] payload, int offset, int count, out uint sequence)
        => pump.Queue(type, payload, offset, count, out sequence);

    internal static async Task<OnlineGameplayConnection> ConnectAsync(string origin, byte[] ticket,
        IOnlineMessageSink? messages, CancellationToken cancellation)
    {
        if (!OnlineCardCredential.ValidOrigin(origin) || ticket == null || ticket.Length != OnlineWire.GameplayTicketSize)
            throw new ArgumentException("Invalid authenticated gameplay endpoint.");
        TlsStream? connected = null;
        try
        {
            connected = await TlsStream.ConnectAsync(origin.Substring(8), 443, cancellation);
            var raw = new RawStream(connected);
            var upgrade = new OnlineWebSocketUpgrade(origin, raw);
            long deadline = Clock.Timestamp + 10000000;
            int sent = 0;
            while (sent < upgrade.Request.Length)
            {
                CheckSetup(cancellation, deadline);
                int count = raw.TryWrite(upgrade.Request, sent, upgrade.Request.Length - sent);
                if (count == 0) throw new IOException("Gameplay upgrade connection closed.");
                if (count > 0) sent += count; else await Task.Delay(1, cancellation);
            }
            var headers = new byte[OnlineWebSocketUpgrade.HeaderLimit];
            int received = 0, consumed;
            while (true)
            {
                CheckSetup(cancellation, deadline);
                int count = raw.TryRead(headers, received, headers.Length - received);
                if (count == 0) throw new IOException("Gameplay upgrade response was truncated.");
                if (count < 0) { await Task.Delay(1, cancellation); continue; }
                received += count;
                var result = upgrade.ReadResponse(headers, received, out consumed);
                if (result == OnlineFrameRead.Invalid) throw new IOException("Gameplay upgrade was rejected.");
                if (result == OnlineFrameRead.Complete) break;
            }
            var prefixed = new Prefixed(raw, headers, consumed, received - consumed);
            var socket = new OnlineWebSocketStream(prefixed, raw);
            var resultConnection = new OnlineGameplayConnection(raw, socket, ticket, messages);
            connected = null; // ownership transferred only after validated upgrade
            return resultConnection;
        }
        finally
        {
            connected?.Dispose();
            Array.Clear(ticket, 0, ticket.Length);
        }
    }
    static void CheckSetup(CancellationToken cancellation, long deadline)
    { cancellation.ThrowIfCancellationRequested(); if (Clock.Timestamp >= deadline) throw new TimeoutException("Gameplay upgrade timed out."); }

    internal bool Tick(OnlineMovementInput input)
    {
        if (disposed) return false;
        NativeFrame.OnlineProbe(30);raw.BeginFrame(); pump.SetInput(pump.Reviewing ? default : input);
        try { pump.Pump(Now); NativeFrame.OnlineProbe(31); if (!pump.Closed && !websocket.Closed) return true; }
        catch (SocketException) { }
        catch (IOException) { }
        Dispose(); return false;
    }
    internal bool TryGetActor(byte slot, out OnlineActorPosition position)
    { position = default; return !disposed && pump.TryGetActor(slot, Now, out position); }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        NativeFrame.OnlineProbe(40);DebugOverlay.WriteLine("ANIMALTOWN ONLINE CLOSE BEGIN");
        pump.Dispose();NativeFrame.OnlineProbe(41);websocket.Dispose();NativeFrame.OnlineProbe(42);raw.Dispose();NativeFrame.OnlineProbe(43);
        DebugOverlay.WriteLine("ANIMALTOWN ONLINE CLOSE READY");
    }

    sealed class RawStream : IOnlinePoseStream, IOnlineMaskSource, IDisposable
    {
        const int FrameBudgetMicroseconds = 250;
        readonly TlsStream tls;
        readonly byte[] random = new byte[256];
        int randomAt = 256;
        long frameStart;
        bool budgeted;
        internal RawStream(TlsStream tls) { this.tls = tls; }
        internal void BeginFrame() { frameStart = Clock.Timestamp; budgeted = true; }
        bool Spent => budgeted && Clock.Timestamp - frameStart >= FrameBudgetMicroseconds;
        public int TryRead(byte[] bytes, int offset, int count)
        { if(Spent)return -1;NativeFrame.OnlineProbe(10);int result=tls.TryRead(bytes,offset,count);NativeFrame.OnlineProbe(11);return result; }
        public int TryWrite(byte[] bytes, int offset, int count)
        { if(Spent)return -1;NativeFrame.OnlineProbe(12);int result=tls.TryWrite(bytes,offset,count);NativeFrame.OnlineProbe(13);return result; }
        public uint NextMask()
        {
            if (randomAt == random.Length) { NativeFrame.OnlineProbe(14);tls.FillRandom(random, 0, random.Length);NativeFrame.OnlineProbe(15);randomAt = 0; }
            uint value = 0;
            for (int i = 0; i < 4; i++) { value |= (uint)random[randomAt] << (i * 8); random[randomAt++] = 0; }
            return value;
        }
        public void Dispose() { Array.Clear(random, 0, random.Length); tls.Dispose(); }
    }
    sealed class Prefixed : IOnlinePoseStream
    {
        readonly IOnlinePoseStream stream;
        byte[]? prefix;
        int at, left;
        internal Prefixed(IOnlinePoseStream stream, byte[] bytes, int offset, int count)
        { this.stream = stream; prefix = count == 0 ? null : bytes; at = offset; left = count; }
        public int TryRead(byte[] bytes, int offset, int count)
        {
            if (left == 0) return stream.TryRead(bytes, offset, count);
            int take = Math.Min(left, count); Array.Copy(prefix!, at, bytes, offset, take); at += take; left -= take;
            if (left == 0) { Array.Clear(prefix!, 0, prefix!.Length); prefix = null; }
            return take;
        }
        public int TryWrite(byte[] bytes, int offset, int count) => stream.TryWrite(bytes, offset, count);
    }
}
#endif
