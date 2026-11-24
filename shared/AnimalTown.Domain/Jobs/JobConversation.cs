namespace AnimalTown.Jobs;

public readonly struct JobConversation
{
    public readonly ulong Session;
    public readonly JobIdentity Player,Npc;
    internal JobConversation(ulong session,JobIdentity player,JobIdentity npc){Session=session;Player=player;Npc=npc;}
}
public readonly struct JobCommand
{
    public readonly JobCommandKind Kind;
    public readonly JobIdentity Player,Npc,Subject;
    public readonly ulong Session,Sequence;
    public readonly int ExpectedRevision,Slot;
    public JobCommand(JobCommandKind kind,JobConversation context,ulong sequence,JobIdentity subject=default,int expectedRevision=-1,int slot=-1)
    {Kind=kind;Player=context.Player;Npc=context.Npc;Session=context.Session;Sequence=sequence;Subject=subject;ExpectedRevision=expectedRevision;Slot=slot;}
}
public interface IOfflineJobTravel
{
    bool Reachable(ulong town,JobIdentity npc);
}
