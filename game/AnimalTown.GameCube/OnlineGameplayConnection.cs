#if ANIMALTOWN_EXPERIMENTAL_ONLINE_TLS
using System;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown.Online;
using Dolphin.Network;
using Dolphin.Timing;

// Opens after the control-plane client has been disposed. The owner initializes
// the configured TLS trust and runs ConnectAsync with scene resources unloaded.
internal sealed class OnlineGameplayConnection : IOnlinePoseStream, IDisposable
{
    private const string Protocol = "animaltown/1";
    private const int Port = 443;
    private const int FrameIoBudgetMicroseconds = 250;
    private readonly TlsStream stream;
    private readonly OnlinePosePump pump;
    private bool disposed;
    private long frameStarted;

    private OnlineGameplayConnection(TlsStream stream, byte[] ticket)
    {
        this.stream = stream;
        pump = new OnlinePosePump(this, ticket, Now);
    }

    private static long Now => Clock.Timestamp / 1000;
    internal uint Epoch => pump.Epoch;
    internal byte LocalSlot => pump.LocalSlot;
    internal bool Reviewing => pump.Reviewing;

    // Clear the caller's copy of the ticket even if connection setup fails.
    internal static async Task<OnlineGameplayConnection> ConnectAsync(string gameplayHost, byte[] ticket,
        CancellationToken cancellation)
    {
        if (ticket is null || ticket.Length != OnlineWire.GameplayTicketSize)
            throw new ArgumentException("The gameplay ticket has an invalid size.", nameof(ticket));

        TlsStream? connected = null;
        try
        {
            connected = await TlsStream.ConnectAsync(gameplayHost, Port, Protocol, cancellation);
            return new OnlineGameplayConnection(connected, ticket);
        }
        catch
        {
            connected?.Dispose();
            throw;
        }
        finally
        {
            Array.Clear(ticket, 0, ticket.Length);
        }
    }

    // The session owner decides whether to reconnect or return offline when
    // the transport closes.
    internal bool Tick(OnlineMovementInput input)
    {
        if (disposed) return false;
        frameStarted = Clock.Timestamp;
        pump.SetInput(pump.Reviewing ? default : input);
        try
        {
            pump.Pump(frameStarted / 1000);
            if (!pump.Closed) return true;
        }
        catch (SocketException)
        {
            // The player-facing state is connection lost. No peer or credentials
            // are written to diagnostics.
        }
        Dispose();
        return false;
    }

    internal bool TryGetActor(byte slot, out OnlineActorPosition position)
    {
        position = default;
        return !disposed && pump.TryGetActor(slot, Now, out position);
    }

    // A native TLS step cannot be preempted. Stop starting further steps for
    // this frame once network work reaches its share of the live-play budget.
    private bool FrameBudgetSpent => Clock.Timestamp - frameStarted >= FrameIoBudgetMicroseconds;

    int IOnlinePoseStream.TryRead(byte[] buffer, int offset, int count)
        => FrameBudgetSpent ? -1 : stream.TryRead(buffer, offset, count);

    int IOnlinePoseStream.TryWrite(byte[] buffer, int offset, int count)
        => FrameBudgetSpent ? -1 : stream.TryWrite(buffer, offset, count);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stream.Dispose();
    }
}
#endif
