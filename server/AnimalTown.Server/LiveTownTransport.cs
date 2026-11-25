using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AnimalTown;
using AnimalTown.Online;

record LiveTicketRequest(Guid Host, Guid Visit);
readonly record struct LiveGuestPose(int Room, float X, float Z);

sealed class LiveTownTransport(GameDatabase database, IConfiguration configuration, ILogger<LiveTownTransport> logger)
    : BackgroundService
{
    static readonly SslApplicationProtocol GameplayProtocol = new("animaltown/1");
    static readonly TimeSpan HandshakeLimit = TimeSpan.FromSeconds(8);
    static readonly TimeSpan InputLimit = TimeSpan.FromSeconds(15);
    const int Capacity = 32, CapacityPerAddress = 8;
    const int MaxTickets = 128;
    static readonly JsonSerializerOptions CommandJson = new(JsonSerializerDefaults.Web);
    readonly object gate = new();
    readonly Dictionary<string, Ticket> tickets = new(StringComparer.Ordinal);
    readonly Dictionary<Guid, TownSession> towns = new();
    readonly Dictionary<IPAddress, int> connections = new();
    readonly SemaphoreSlim slots = new(Capacity);
    int listening;
    uint nextEpoch;

    sealed record Ticket(DevicePrincipal Device, Guid Host, Guid Visit, Guid Gate, long ExpiresAt);

    sealed class Actor(Ticket ticket, byte slot, float x, float z)
    {
        public readonly Ticket Ticket = ticket;
        public readonly byte Slot = slot;
        public readonly Guid ConnectionId = Guid.NewGuid();
        public float X = x, Z = z;
        public ushort Facing;
        public uint LastSeen, Applied;
        public OnlineMovementInput Input;
        public long LastInputAt;
        public byte[]? PendingReply;
        public uint PendingReplySequence;
    }

    sealed class TownSession(GameState world, Guid gateId, uint epoch)
    {
        public readonly GameState World = world;
        public readonly uint Epoch = epoch;
        public readonly Guid GateId = gateId;
        public readonly Actor?[] Actors = new Actor?[3];
        public bool Reviewing;
        public long LastStep;
    }

    public async Task<object> Issue(DevicePrincipal device, LiveTicketRequest request)
    {
        if (Volatile.Read(ref listening) == 0)
            throw new GameError(503, "gameplay-transport-unavailable");
        var access = await database.ValidateLiveAccess(device, request.Host, request.Visit, false);
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        long now = Environment.TickCount64;
        lock (gate)
        {
            foreach (string expired in tickets.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                tickets.Remove(expired);
            if (tickets.Count >= MaxTickets || tickets.Values.Count(ticket => ticket.Device.Id == device.Id) >= 2 ||
                tickets.Values.Count(ticket => ticket.Device.Player == device.Player) >= 4)
                throw new GameError(429, "too-many-gameplay-tickets");
            tickets.Add(TicketKey(secret), new Ticket(device, request.Host, request.Visit, access.Gate, now + 15_000));
        }
        return new { ticket = secret, expiresIn = 15 };
    }

    static string TicketKey(string secret) => Convert.ToHexString(SHA256.HashData(Convert.FromHexString(secret)));

    public void CloseTown(Guid host, Guid gateId)
    {
        lock (gate)
        {
            if (towns.TryGetValue(host, out var current) && current.GateId == gateId)
            {
                towns.Remove(host);
                Array.Clear(current.Actors);
            }
            foreach (string key in tickets.Where(pair => pair.Value.Host == host && pair.Value.Gate == gateId).Select(pair => pair.Key).ToArray())
                tickets.Remove(key);
        }
    }

    public bool TryGetGuestPose(Guid host, Guid guest, Guid visit, DevicePrincipal device, Guid connectionId, out LiveGuestPose pose)
    {
        lock (gate)
        {
            if (towns.TryGetValue(host, out TownSession? town) && !town.Reviewing)
            {
                foreach (var actor in town.Actors)
                {
                    if (actor is null || actor.Ticket.Device.Player != guest || actor.Ticket.Visit != visit || actor.Ticket.Device != device || actor.ConnectionId != connectionId)
                        continue;
                    pose = new LiveGuestPose(0, actor.X, actor.Z);
                    return true;
                }
            }
        }
        pose = default;
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int port = configuration.GetValue<int>("OnlineGameplay:Port");
        if (port == 0) return;
        string certificate = configuration["OnlineGameplay:Certificate"] ?? throw new InvalidOperationException("Set OnlineGameplay:Certificate");
        string key = configuration["OnlineGameplay:CertificateKey"] ?? throw new InvalidOperationException("Set OnlineGameplay:CertificateKey");
        using var identity = X509Certificate2.CreateFromPemFile(certificate, key);
        var listener = new TcpListener(IPAddress.Any, port);
        var active = new HashSet<Task>();
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        listener.Start(Capacity);
        Volatile.Write(ref listening, 1);
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(shutdown.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint endpoint || !TryReserveConnection(endpoint.Address))
                {
                    client.Dispose();
                    continue;
                }
                active.RemoveWhere(task => task.IsCompleted);
                active.Add(Serve(client, endpoint.Address, identity, shutdown.Token));
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            Volatile.Write(ref listening, 0);
            listener.Stop();
            await shutdown.CancelAsync();
            await Task.WhenAll(active);
        }
    }

    bool TryReserveConnection(IPAddress address)
    {
        lock (gate)
        {
            connections.TryGetValue(address, out int count);
            if (count >= CapacityPerAddress || !slots.Wait(0)) return false;
            connections[address] = count + 1;
            return true;
        }
    }

    async Task Serve(TcpClient client, IPAddress address, X509Certificate2 identity, CancellationToken stop)
    {
        Actor? actor = null;
        TownSession? town = null;
        try
        {
            using (client)
            {
                client.NoDelay = true;
                using var stream = new SslStream(client.GetStream());
                using var opening = CancellationTokenSource.CreateLinkedTokenSource(stop);
                opening.CancelAfter(HandshakeLimit);
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = identity,
                    EnabledSslProtocols = SslProtocols.Tls12,
                    ApplicationProtocols = [GameplayProtocol],
                }, opening.Token);
                if (stream.NegotiatedApplicationProtocol != GameplayProtocol) return;

                byte[] receive = new byte[OnlineWire.HeaderSize + 512];
                if (!await ReadHeader(stream, receive, opening.Token) ||
                    !OnlineWire.TryReadHeader(receive, 0, OnlineWire.HeaderSize, out var hello) ||
                    hello.Type != OnlineFrameType.GameplayTicket || hello.Epoch != 0 || hello.Sequence != 0)
                    return;
                await stream.ReadExactlyAsync(receive.AsMemory(OnlineWire.HeaderSize, 32), opening.Token);

                Ticket? ticket;
                lock (gate)
                    tickets.Remove(TicketKey(Convert.ToHexString(receive, OnlineWire.HeaderSize, 32)), out ticket);
                if (ticket is null || ticket.ExpiresAt <= Environment.TickCount64) return;
                var access = await database.ValidateLiveAccess(ticket.Device, ticket.Host, ticket.Visit, true);
                if (access.Gate != ticket.Gate || access.World is not GameState world) return;
                opening.Token.ThrowIfCancellationRequested();
                (town, actor) = Join(ticket, world);
                await RunVisit(stream, receive, town, actor, stop);
            }
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException or SocketException or OperationCanceledException or GameError)
        {
            // The browser and gameplay client see a closed connection. Keep
            // credentials, payloads and exception text out of diagnostics.
            logger.LogDebug("Gameplay connection ended with {FailureType}", ex.GetType().Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Gameplay connection failed with {FailureType}", ex.GetType().Name);
        }
        finally
        {
            if (actor is not null && town is not null)
            {
                lock (gate)
                {
                    if (town.Actors[actor.Slot] == actor) town.Actors[actor.Slot] = null;
                    if (town.Actors.All(member => member is null) &&
                        towns.TryGetValue(actor.Ticket.Host, out var current) && current == town)
                        towns.Remove(actor.Ticket.Host);
                }
            }
            lock (gate)
            {
                int count = connections[address];
                if (count == 1) connections.Remove(address);
                else connections[address] = count - 1;
                slots.Release();
            }
        }
    }

    (TownSession, Actor) Join(Ticket ticket, GameState world)
    {
        lock (gate)
        {
            bool newTown = !towns.TryGetValue(ticket.Host, out var town) || town.GateId != ticket.Gate;
            if (newTown) town = new TownSession(world, ticket.Gate, checked(++nextEpoch));
            if (town is null) throw new InvalidOperationException("No gameplay town");
            int slot = ticket.Visit == Guid.Empty ? 0 : -1;
            Actor? prior = null;
            for (int i = 0; i < town.Actors.Length; i++)
            {
                var candidate = town.Actors[i];
                if (candidate?.Ticket.Device.Player == ticket.Device.Player)
                {
                    slot = i;
                    prior = candidate;
                    break;
                }
                if (slot < 0 && i > 0 && candidate is null) slot = i;
            }
            if (slot < 0) throw new GameError(409, "town-full");
            float x, z;
            if (prior is not null) { x = prior.X; z = prior.Z; }
            else if (slot == 0 && world.Space == 0 && world.CanWalk(world.X, world.Z, 0))
            { x = world.X; z = world.Z; }
            else if (!HelipadSite.TryArrival(town.World, out x, out z))
                throw new GameError(409, "arrival-blocked");
            var actor = new Actor(ticket, (byte)slot, x, z);
            town.Actors[slot] = actor;
            if (newTown) towns[ticket.Host] = town;
            return (town, actor);
        }
    }

    async Task RunVisit(SslStream stream, byte[] receive, TownSession town, Actor actor, CancellationToken stop)
    {
        using var visit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var receiveWork = Receive(stream, receive, town, actor, visit.Token);
        // Each sender owns only three pose slots and one frame buffer.
        byte[] send = new byte[OnlineWire.HeaderSize + 512];
        var poses = new OnlineActorPosition[3];
        uint update = 0;
        bool? lastReviewState = null;
        long nextAuthorization = 0;
        try
        {
            if (OnlineWire.TryWriteHeader(send, 0, send.Length, new OnlineFrameHeader(OnlineFrameType.Pong, 0, town.Epoch, actor.Slot)))
                await WriteFrame(stream, send, OnlineWire.HeaderSize, visit.Token);

            using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (!receiveWork.IsCompleted && await ticks.WaitForNextTickAsync(visit.Token))
            {
                long now = Environment.TickCount64;
                if (now >= nextAuthorization)
                {
                    var access = await database.ValidateLiveAccess(actor.Ticket.Device, actor.Ticket.Host, actor.Ticket.Visit, false);
                    if (access.Gate != actor.Ticket.Gate) break;
                    bool reviewing = await database.IsReviewActive(actor.Ticket.Host);
                    lock (gate) town.Reviewing = reviewing;
                    nextAuthorization = now + 1000;
                }
                int count = 0;
                bool reviewingNow;
                byte[]? reply;
                uint replySequence;
                lock (gate)
                {
                    if (town.Actors[actor.Slot] != actor) break;
                    StepTown(town, now);
                    reviewingNow = town.Reviewing;
                    reply = actor.PendingReply;
                    replySequence = actor.PendingReplySequence;
                    actor.PendingReply = null;
                    foreach (var member in town.Actors)
                    {
                        if (member is null) continue;
                        poses[count++] = new OnlineActorPosition(member.Slot, 0,
                            (int)(member.X * 65536), (int)(member.Z * 65536), member.Facing, member.Applied);
                    }
                }
                if (lastReviewState != reviewingNow &&
                    OnlineWire.TryWriteHeader(send, 0, send.Length, new OnlineFrameHeader(OnlineFrameType.SessionState, 1, town.Epoch, ++update)))
                {
                    send[OnlineWire.HeaderSize] = reviewingNow ? (byte)1 : (byte)0;
                    await WriteFrame(stream, send, OnlineWire.HeaderSize + 1, visit.Token);
                    lastReviewState = reviewingNow;
                }
                if (reply is not null &&
                    OnlineWire.TryWriteHeader(send, 0, send.Length, new OnlineFrameHeader(OnlineFrameType.ReliableEvent, reply.Length, town.Epoch, replySequence)))
                {
                    reply.CopyTo(send.AsSpan(OnlineWire.HeaderSize));
                    await WriteFrame(stream, send, OnlineWire.HeaderSize + reply.Length, visit.Token);
                }
                for (int i = 0; i < count; i++)
                    if (OnlineWire.TryWritePosition(send, 0, send.Length, town.Epoch, ++update, poses[i]))
                        await WriteFrame(stream, send, OnlineWire.HeaderSize + 16, visit.Token);
            }
        }
        finally
        {
            await visit.CancelAsync();
            try { await receiveWork; }
            catch (OperationCanceledException) when (visit.IsCancellationRequested) { }
        }
    }

    static async Task WriteFrame(SslStream stream, byte[] frame, int length, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await stream.WriteAsync(frame.AsMemory(0, length), deadline.Token);
    }

    static async Task<bool> ReadHeader(SslStream stream, byte[] buffer, CancellationToken token)
    {
        int first = await stream.ReadAsync(buffer.AsMemory(0, OnlineWire.HeaderSize), token);
        if (first == 0) return false;
        await stream.ReadExactlyAsync(buffer.AsMemory(first, OnlineWire.HeaderSize - first), token);
        return true;
    }

    async Task Receive(SslStream stream, byte[] buffer, TownSession town, Actor actor, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        long burstStart = 0;
        int burst = 0, commands = 0;
        while (!token.IsCancellationRequested)
        {
            deadline.CancelAfter(InputLimit);
            if (!await ReadHeader(stream, buffer, deadline.Token) ||
                !OnlineWire.TryReadHeader(buffer, 0, OnlineWire.HeaderSize, out var header) ||
                header.Epoch != town.Epoch)
                return;
            long now = Environment.TickCount64;
            if (now - burstStart >= 1000) { burstStart = now; burst = 0; commands = 0; }
            if (++burst > 40) return;
            if (header.Type == OnlineFrameType.Heartbeat)
            {
                if (actor.Ticket.Visit != Guid.Empty)
                    await database.HeartbeatVisit(actor.Ticket.Device.Player, actor.Ticket.Visit, actor.Ticket.Device);
                continue;
            }
            if (header.Type == OnlineFrameType.Command)
            {
                if (++commands > 4 || header.PayloadLength > 512 || header.Sequence == 0) return;
                await stream.ReadExactlyAsync(buffer.AsMemory(OnlineWire.HeaderSize, header.PayloadLength), deadline.Token);
                lock (gate)
                    if (town.Actors[actor.Slot] != actor || actor.PendingReply is not null) return;
                var reply = await GuestCommand(actor, buffer, header.PayloadLength);
                lock (gate)
                {
                    actor.PendingReply = reply;
                    actor.PendingReplySequence = header.Sequence;
                }
                continue;
            }
            if (header.Type != OnlineFrameType.MovementInput) return;
            await stream.ReadExactlyAsync(buffer.AsMemory(OnlineWire.HeaderSize, 3), deadline.Token);
            if (!OnlineWire.TryReadInput(buffer, 0, OnlineWire.HeaderSize + 3, out _, out var input))
                return;
            lock (gate)
            {
                if (town.Actors[actor.Slot] != actor) return;
                if (header.Sequence <= actor.LastSeen || town.Reviewing) continue;
                actor.LastSeen = header.Sequence;
                actor.Input = input;
                actor.LastInputAt = now;
            }
        }
    }

    async Task<byte[]> GuestCommand(Actor actor, byte[] buffer, int length)
    {
        object result;
        try
        {
            if (actor.Ticket.Visit == Guid.Empty || !configuration.GetValue<bool>("Multiplayer:EnablePlacements"))
                throw new GameError(403, "guest-placements-unavailable");
            var operation = JsonSerializer.Deserialize<GuestPlacementOperation>(
                buffer.AsSpan(OnlineWire.HeaderSize, length), CommandJson)
                ?? throw new GameError(400, "invalid-placement-operation");
            result = await database.MutateGuestPlacement(actor.Ticket.Device, actor.Ticket.Visit, actor.ConnectionId, operation);
        }
        catch (GameError error) { result = new { status = error.Status, error = error.Message }; }
        catch (JsonException) { result = new { status = 400, error = "invalid-placement-operation" }; }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, CommandJson);
        return bytes.Length <= 512 ? bytes : "{\"status\":500}"u8.ToArray();
    }

    static void StepTown(TownSession town, long now)
    {
        if (now - town.LastStep < 90) return;
        town.LastStep = now;
        foreach (var actor in town.Actors)
        {
            if (actor is null || town.Reviewing || now - actor.LastInputAt > 250) continue;
            float x = actor.Input.X / 127f, z = actor.Input.Z / 127f;
            float length = MathF.Sqrt(x * x + z * z);
            if (length > 1) { x /= length; z /= length; }
            float step = actor.Input.Run ? .45f : .25f;
            if (town.World.CanWalk(actor.X + x * step, actor.Z, 0)) actor.X += x * step;
            if (town.World.CanWalk(actor.X, actor.Z + z * step, 0)) actor.Z += z * step;
            if (length > .01f)
                actor.Facing = (ushort)((MathF.Atan2(x, z) / (2 * MathF.PI) + 1) * 65536);
            actor.Applied = actor.LastSeen;
        }
    }
}

sealed partial class GameDatabase
{
    internal LiveTownTransport? LiveTransport { private get; set; }

    internal bool TryGetLiveGuestPose(Guid host, Guid guest, Guid visit, DevicePrincipal device, Guid connectionId, out LiveGuestPose pose)
    {
        if (LiveTransport is { } transport) return transport.TryGetGuestPose(host, guest, visit, device, connectionId, out pose);
        pose = default;
        return false;
    }

    internal async Task<(Guid Gate, GameState? World)> ValidateLiveAccess(DevicePrincipal device, Guid host, Guid visit, bool loadWorld)
    {
        if (host == Guid.Empty || (visit == Guid.Empty) != (host == device.Player))
            throw new GameError(403, "invalid-gameplay-seat");
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await RequireActiveDevice(connection, transaction, device);
        await using (var binding = Cmd(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM initial_town_imports WHERE player=$1 AND town_identity=$2)",
            device.Player, device.Town))
            if (await binding.ExecuteScalarAsync() is not true)
                throw new GameError(409, "town-checkpoint-required");
        Guid gateId;
        await using (var opened = Cmd(connection, transaction,
            "SELECT generation FROM gates WHERE host=$1 AND expires>now()", host))
            gateId = await opened.ExecuteScalarAsync() is Guid value ? value : throw new GameError(403, "gate-closed");
        if (visit != Guid.Empty)
        {
            await CheckVisit(connection, transaction, device.Player, host);
            await using var command = Cmd(connection, transaction, """
                SELECT EXISTS(SELECT 1 FROM visits WHERE host=$1 AND guest=$2 AND id=$3 AND NOT ended AND expires>now())
                """, host, device.Player, visit);
            if (await command.ExecuteScalarAsync() is not true) throw new GameError(403, "invalid-gameplay-seat");
        }
        await using (var command = Cmd(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM initial_town_imports WHERE player=$1)", host))
            if (await command.ExecuteScalarAsync() is not true) throw new GameError(409, "town-checkpoint-required");
        GameState? world = loadWorld ? (await Load(connection, transaction, host, false)).State : null;
        await transaction.CommitAsync();
        return (gateId, world);
    }
}
