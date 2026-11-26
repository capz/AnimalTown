namespace AnimalTown.Online;

public sealed class OnlineHuman
{
    public bool Present;
    public OnlineId Id;
    public string Name = "";
    public int Species, Palette, Shirt;
}
public readonly struct OnlineChatMessage
{
    public readonly OnlineId Sender;
    public readonly string Name, Text;
    public OnlineChatMessage(OnlineId sender, string name, string text) { Sender = sender; Name = name; Text = text; }
}

// One recipient's state, separate from the selected home GameState. The same
// parser is used by console connections and the real multi-client fixture.
public sealed class OnlineVisitState : IOnlineMessageSink, IDisposable
{
    public const int SnapshotLimit = OnlineTownProjection.MaximumBytes + 8192;
    public GameState? World { get; private set; }
    public long Cursor { get; private set; }
    public bool NeedsSnapshot { get; private set; } = true;
    public bool ReplayGap {get;private set;}
    public readonly OnlineHuman[] Humans = { new(), new(), new() };
    public readonly int[] Pockets = new int[GameState.MaximumPockets];
    public long InventoryRevision { get; private set; }
    public long Savings { get; private set; }
    public long Debt { get; private set; }
    public int Capacity { get; private set; }
    public int Wallet { get; private set; }
    public int Species { get; private set; }
    public int Palette { get; private set; }
    public int Shirt { get; private set; }
    public string Name { get; private set; } = "";
    public readonly OnlineId[] Placements = new OnlineId[64], PlacementOwners = new OnlineId[64];
    public readonly int[] PlacementVersions = new int[64];
    public int PlacementCount { get; private set; }
    readonly OnlineChatMessage[] chat = new OnlineChatMessage[32];
    int chatStart, chatCount;
    byte[]? snapshot;
    int snapshotId, snapshotAt, snapshotLength;
    long snapshotCursor;
    readonly byte[] result = new byte[512];
    int resultLength;
    bool disposed;
    public int ChatCount => chatCount;
    public void ExpectSnapshot(){if(!disposed)NeedsSnapshot=true;}
    public OnlineChatMessage ChatAt(int index)
    { if ((uint)index >= chatCount) throw new ArgumentOutOfRangeException(nameof(index)); return chat[(chatStart + index) % chat.Length]; }

    public bool Apply(OnlineFrameHeader header, byte[] bytes, int offset, int count)
    {
        if (disposed) return false;
        try
        {
            if (header.Type == OnlineFrameType.SnapshotPage) return Snapshot(bytes, offset, count);
            var r = new OnlinePacketReader(bytes, offset, count); int kind = r.Byte();
            if (kind == OnlineGameplayCodec.Result)
            {
                if (count > result.Length || resultLength != 0) return false;
                Array.Copy(bytes, offset, result, 0, count); resultLength = count; return true;
            }
            if (kind == 3)
            {
                int members = r.Int(); if (members < 0 || members > 3) return false;
                for (int i = 0; i < Humans.Length; i++) Humans[i].Present = false;
                for (int i = 0; i < members; i++)
                {
                    int slot = r.Byte(); if (slot >= Humans.Length || Humans[slot].Present) return false;
                    var human = Humans[slot]; human.Present = true; human.Id = r.Id(); human.Name = r.Text(32);
                    human.Species = r.Int(); human.Palette = r.Int(); human.Shirt = r.Int();
                    if (human.Id.IsEmpty || human.Species < 0 || human.Species >= ModelResidencyDemand.SpeciesCount ||
                        human.Palette < 0 || human.Palette > 63 || !Catalog.Valid(human.Shirt) && human.Shirt != 0) return false;
                }
                r.End(); return true;
            }
            if (kind != OnlineGameplayCodec.Event) return false;
            long cursor = r.Long(); int eventKind = r.Int();
            if (eventKind == 255) { NeedsSnapshot = true;ReplayGap=true;return true; }
            if (cursor <= Cursor) return true; // already represented in a snapshot/replay
            if (cursor != Cursor + 1) { NeedsSnapshot = true;ReplayGap=true;return true; }
            if (eventKind == 0) { if (r.Byte() != 0) return false; }
            else if (eventKind == 1) { if (r.Byte() != 1) return false; NeedsSnapshot = true; }
            else if (eventKind == 2) Inventory(r);
            else if (eventKind == 4)
            {
                var message = new OnlineChatMessage(r.Id(), r.Text(32), r.Text(TownChat.MaximumLength));
                if (message.Sender.IsEmpty || !TownChat.Supported(message.Text)) return false;
                int at = (chatStart + chatCount) % chat.Length;
                if (chatCount == chat.Length) { at = chatStart; chatStart = (chatStart + 1) % chat.Length; } else chatCount++;
                chat[at] = message;
            }
            else if(eventKind==8)
            {
                int actors=r.Int();if(actors<0||actors>Town.SavedVillagerCount)return false;
                int seen=0;
                for(int i=0;i<actors;i++)
                {
                    int npc=r.Int(),room=r.Int(),x=r.Int(),z=r.Int(),facing=r.Int();
                    if((uint)npc>=Town.SavedVillagerCount||(seen&(1<<npc))!=0||!BuildingWorld.ValidSpace(room)||
                        x<0||x>=Town.Width*65536||z<0||z>=Town.Height*65536)return false;
                    seen|=1<<npc;
                    if(World!=null){var actor=World.Town.Residents[npc];actor.Space=room;actor.PosX=x/65536f;actor.PosZ=z/65536f;actor.Yaw=facing/65536f;actor.X=(int)actor.PosX;actor.Z=(int)actor.PosZ;}
                }
            }
            else return false;
            r.End(); Cursor = cursor; return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    bool Snapshot(byte[] bytes, int offset, int count)
    {
        var r = new OnlinePacketReader(bytes, offset, count);
        if (r.Byte() != 1) return false;
        int id = r.Int(); long cursor = r.Long(); int total = r.Int(), position = r.Int();
        if (id <= 0 || cursor < Cursor || total < 1 || total > SnapshotLimit || position < 0 || r.Remaining == 0) return false;
        if (position == 0)
        {
            if (snapshot != null) Array.Clear(snapshot, 0, snapshot.Length);
            snapshot = new byte[total]; snapshotId = id; snapshotCursor = cursor; snapshotLength = total; snapshotAt = 0;
        }
        if (snapshot == null || id != snapshotId || cursor != snapshotCursor || total != snapshotLength || position != snapshotAt || r.Remaining > total - position) return false;
        int take = r.Remaining; r.Bytes(snapshot, snapshotAt, take); snapshotAt += take;
        if (snapshotAt != snapshotLength) return true;
        var decoded = new OnlinePacketReader(snapshot, 0, snapshotLength);
        if (decoded.Byte() != 1 || decoded.Long() != cursor) return false;
        int worldLength = decoded.Int(); if (worldLength < 1 || worldLength > OnlineTownProjection.MaximumBytes) return false;
        var worldBytes = new byte[worldLength]; decoded.Bytes(worldBytes, 0, worldLength);
        var world = OnlineTownProjection.Read(worldBytes, worldLength); Array.Clear(worldBytes, 0, worldBytes.Length);
        int inventoryLength = decoded.Int(); if (inventoryLength < 1 || inventoryLength > 512) return false;
        var privateBytes = new byte[inventoryLength]; decoded.Bytes(privateBytes, 0, inventoryLength);
        var inventory = new OnlinePacketReader(privateBytes, 0, privateBytes.Length); Inventory(inventory); inventory.End(); Array.Clear(privateBytes, 0, privateBytes.Length);
        int placements = decoded.Int(); if (placements < 0 || placements > Placements.Length) return false;
        for (int i = 0; i < placements; i++)
        { Placements[i] = decoded.Id(); PlacementOwners[i] = decoded.Id(); PlacementVersions[i] = decoded.Int(); if (Placements[i].IsEmpty || PlacementOwners[i].IsEmpty || PlacementVersions[i] < 1) return false; }
        for (int i = placements; i < Placements.Length; i++) { Placements[i] = default; PlacementOwners[i] = default; PlacementVersions[i] = 0; }
        decoded.End(); PlacementCount = placements;
        if(World==null)World=world;else OnlineTownProjection.Update(World,world);
        Cursor = cursor; NeedsSnapshot = false;ReplayGap=false;
        Array.Clear(snapshot, 0, snapshot.Length); snapshot = null; return true;
    }

    void Inventory(OnlinePacketReader r)
    {
        long revision = r.Long(); int capacity = r.Int(), wallet = r.Int(); long savings = r.Long(), debt = r.Long();
        int species = r.Int(), palette = r.Int(), shirt = r.Int(); string name = r.Text(32);
        if (revision < 1 || capacity is not (10 or 20 or 30) || wallet < 0 || wallet > GameState.WalletLimit || savings < 0 || debt < 0 ||
            species < 0 || species >= ModelResidencyDemand.SpeciesCount || palette < 0 || palette > 63 || shirt != 0 && !Catalog.Valid(shirt))
            throw new InvalidOperationException("Invalid private inventory projection.");
        for (int i = 0; i < Pockets.Length; i++) { int item = r.Int(); if (item != 0 && !Catalog.Valid(item) || i >= capacity && item != 0) throw new InvalidOperationException("Invalid private pocket."); Pockets[i] = item; }
        InventoryRevision = revision; Capacity = capacity; Wallet = wallet; Savings = savings; Debt = debt;
        Species = species; Palette = palette; Shirt = shirt; Name = name;
    }
    public bool TakeResult(byte[] target, out int count)
    {
        count = resultLength; if (count == 0 || target.Length < count) return false;
        Array.Copy(result, target, count); Array.Clear(result, 0, count); resultLength = 0; return true;
    }
    public void Dispose()
    {
        disposed = true; World = null; Name = "";
        if (snapshot != null) { Array.Clear(snapshot, 0, snapshot.Length); snapshot = null; }
        Array.Clear(Pockets, 0, Pockets.Length); Array.Clear(result, 0, result.Length); Array.Clear(chat, 0, chat.Length);
        for (int i = 0; i < Humans.Length; i++) { Humans[i].Present = false; Humans[i].Name = ""; Humans[i].Id = default; }
    }
}
