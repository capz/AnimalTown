namespace AnimalTown.Jobs;

public sealed partial class OfflineJobLedger
{
    internal readonly JobIdentity[] OpeningParcels=new JobIdentity[OwnerCount];
    internal void WriteOpening(JobWireWriter w){for(int p=0;p<OwnerCount;p++)w.Key(OpeningParcels[p]);}
    internal void ReadOpening(JobWireReader r){for(int p=0;p<OwnerCount;p++)OpeningParcels[p]=r.Key();}
}
public sealed partial class VillagerJobService
{
    private void RecoverOpeningParcel()
    {
        if(state.Visiting||state.Tutorial>3)return;int profile=state.ActiveProfile;
        var binding=ledger.OpeningParcels[profile];
        if(!binding.IsEmpty){for(int i=0;i<state.Capacity;i++)if(state.Pockets[i]==JobContent.Parcel&&ledger.Binding(profile,i)==binding)return;ledger.OpeningParcels[profile]=default;}
        // Only the fixed tutorial's existing parcel is migrated. Arbitrary
        // ordinary parcels never become resident-job instances.
        int slot=state.Find(JobContent.Parcel);if(slot<0||state.IsJobItem(slot)||!ledger.CanAllocate(1))return;
        ledger.OpeningParcels[profile]=ledger.Allocate();ledger.SetBinding(profile,slot,ledger.OpeningParcels[profile]);ledger.Revision++;
    }
    public bool OpeningDelivery(int npc)
    {
        if(!Enabled||state.Visiting||state.Tutorial!=2||npc!=0)return false;int slot=OpeningPocket();if(slot<0)return false;
        ConsumeOpening(slot);state.CreditTutorial(2);return true;
    }
    public bool OpeningPickup(int npc)
    {
        if(!Enabled||state.Visiting||state.Tutorial!=3||npc!=4)return false;
        if(OpeningPocket()>=0)return true;if(!ledger.CanAllocate(1))return false;int slot=state.FreeSlot();if(slot<0)return false;
        ledger.OpeningParcels[state.ActiveProfile]=ledger.Allocate();state.Pockets[slot]=JobContent.Parcel;ledger.SetBinding(state.ActiveProfile,slot,ledger.OpeningParcels[state.ActiveProfile]);ledger.Revision++;return true;
    }
    public bool OpeningReturn()
    {
        if(!Enabled||state.Visiting||state.Tutorial!=3)return false;int slot=OpeningPocket();if(slot<0)return false;ConsumeOpening(slot);state.CreditTutorial(3);state.Receive(Catalog.WritingPaper);return true;
    }
    private int OpeningPocket()
    {var id=ledger.OpeningParcels[state.ActiveProfile];if(id.IsEmpty)return -1;for(int i=0;i<state.Capacity;i++)if(ledger.Binding(state.ActiveProfile,i)==id&&state.Pockets[i]==JobContent.Parcel)return i;return -1;}
    private void ConsumeOpening(int slot)
    {state.Pockets[slot]=0;ledger.SetBinding(state.ActiveProfile,slot,default);ledger.OpeningParcels[state.ActiveProfile]=default;ledger.Revision++;}
}
