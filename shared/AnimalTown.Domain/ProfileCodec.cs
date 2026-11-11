namespace AnimalTown;
internal static class ProfileCodec
{
    internal static byte[] Encode(GameState s,int profile)
    {var w=new SplitSaveCodec.Writer(s,SplitSaveCodec.RecordKind(3+profile),1408);Write(w,s.Profiles[profile]);return w.Finish();}
    internal static VillagerProfile Decode(GameState s,int profile,byte[] bytes)
    {var r=new SplitSaveCodec.Reader(bytes,SplitSaveCodec.RecordKind(3+profile));if(r.Version!=4||r.Sequence!=s.Sequence||r.Seed!=s.Town.Seed)throw new InvalidOperationException("Mixed profile generation");var p=Read(r);r.End();return p;}
    internal static void Write(SplitSaveCodec.Writer w,VillagerProfile p)
    {
        w.Int(p.Created?1:0);w.Int(p.HousePlaced?1:0);
        w.Int(p.BackpackTier);
        w.Int(p.Wallet);
        w.Int(p.Species);
        w.Int(p.Palette);
        w.Int(p.Tool);
        w.Int(p.Shirt);
        w.Int(p.HouseTier);
        w.Int(p.GardenTier);
        w.Int(p.PendingKind);
        w.Int(p.PendingTier);
        w.Int(p.PendingDay);
        w.Int(p.Tutorial);
        w.Int(p.MetMask);
        w.Int(p.Jobs);
        w.Int(p.JobDay);
        w.Int(p.MailCount);
        w.Int(p.Roof);
        w.Int(p.Wall);
        w.Int(p.Fence);
        w.Int(p.Mailbox);
        w.Int(p.Exterior);
        w.Int(p.Space);
        w.Int(p.HouseX);
        w.Int(p.HouseZ);
        w.Int((int)p.Savings);w.Int((int)(p.Savings>>32));w.Int((int)p.Debt);w.Int((int)(p.Debt>>32));
        w.Int((int)(p.X*1024));
        w.Int((int)(p.Z*1024));
        w.Int((int)(p.Facing*1024));
        w.Text(p.Name);w.Text(p.Catchphrase);
        for(int i=0;i<p.Pockets.Length;i++)w.Int(p.Pockets[i]);
        for(int i=0;i<p.Owned.Length;i++)w.Int(p.Owned[i]);
        for(int i=0;i<p.MailCount;i++)w.Int(p.Mail[i]);
        for(int i=0;i<p.Relationships.Length;i++)w.Int(p.Relationships[i]);
    }
    internal static bool Same(VillagerProfile a,VillagerProfile b)
    {
        bool same=a.Created==b.Created;
        same &= a.BackpackTier==b.BackpackTier;
        same &= a.Wallet==b.Wallet;
        same &= a.Species==b.Species;
        same &= a.Palette==b.Palette;
        same &= a.Tool==b.Tool;
        same &= a.Shirt==b.Shirt;
        same &= a.HouseTier==b.HouseTier;
        same &= a.GardenTier==b.GardenTier;
        same &= a.PendingKind==b.PendingKind;
        same &= a.PendingTier==b.PendingTier;
        same &= a.PendingDay==b.PendingDay;
        same &= a.Tutorial==b.Tutorial;
        same &= a.MetMask==b.MetMask;
        same &= a.Jobs==b.Jobs;
        same &= a.JobDay==b.JobDay;
        same &= a.MailCount==b.MailCount;
        same &= a.Roof==b.Roof;
        same &= a.Wall==b.Wall;
        same &= a.Fence==b.Fence;
        same &= a.Mailbox==b.Mailbox;
        same &= a.Exterior==b.Exterior;
        same &= a.Space==b.Space;
        same &= a.HouseX==b.HouseX;
        same &= a.HouseZ==b.HouseZ;
        same &= a.Savings==b.Savings;
        same &= a.Debt==b.Debt;
        same &= a.X==b.X;
        same &= a.Z==b.Z;
        same &= a.Facing==b.Facing;
        same &= a.HousePlaced==b.HousePlaced;
        same &= a.Name==b.Name;
        same &= a.Catchphrase==b.Catchphrase;
        if(!same)return false;
        for(int i=0;i<30;i++)if(a.Pockets[i]!=b.Pockets[i])return false;for(int i=0;i<64;i++)if(a.Owned[i]!=b.Owned[i])return false;for(int i=0;i<a.MailCount;i++)if(a.Mail[i]!=b.Mail[i])return false;for(int i=0;i<24;i++)if(a.Relationships[i]!=b.Relationships[i])return false;return true;
    }
    internal static VillagerProfile Read(SplitSaveCodec.Reader r)
    {
        var p=new VillagerProfile{Created=r.Range(0,1)==1,HousePlaced=r.Range(0,1)==1};
        p.BackpackTier=r.Range(0,2);
        p.Wallet=r.Range(0,99999);
        p.Species=r.Range(0,5);
        p.Palette=r.Range(0,7);
        p.Tool=r.Range(0,25);
        p.Shirt=r.Range(0,25);
        p.HouseTier=r.Range(0,5);
        p.GardenTier=r.Range(0,4);
        p.PendingKind=r.Range(0,2);
        p.PendingTier=r.Range(0,5);
        p.PendingDay=r.Int();
        p.Tutorial=r.Range(0,6);
        p.MetMask=r.Range(0,63);
        p.Jobs=r.Range(0,2);
        p.JobDay=r.Int();
        p.MailCount=r.Range(0,128);
        p.Roof=r.Range(0,11);
        p.Wall=r.Range(0,11);
        p.Fence=r.Range(0,5);
        p.Mailbox=r.Range(0,5);
        p.Exterior=r.Range(0,4);
        p.Space=r.Range(0,43);
        p.HouseX=r.Range(1,145);
        p.HouseZ=r.Range(1,147);
        uint low=(uint)r.Int();int high=r.Range(0,int.MaxValue);p.Savings=((long)high<<32)|low;low=(uint)r.Int();high=r.Range(0,0);p.Debt=((long)high<<32)|low;if(p.Debt>1600000)throw new InvalidOperationException("Invalid profile debt");
        p.X=r.Range(0,Town.Width*1024-1)/1024f;
        p.Z=r.Range(0,Town.Width*1024-1)/1024f;
        p.Facing=r.Range(-6434,6434)/1024f;
        if(!BuildingWorld.ValidSpace(p.Space)||!p.Created&&p.HousePlaced)throw new InvalidOperationException("Invalid profile home/space");
        p.Name=r.Text(16);p.Catchphrase=r.Text(32);
        for(int i=0;i<30;i++){p.Pockets[i]=r.Range(0,25);if(i>=(p.BackpackTier+1)*10&&p.Pockets[i]!=0)throw new InvalidOperationException("Locked profile pocket");}
        for(int i=0;i<64;i++)p.Owned[i]=r.Range(0,1);for(int i=0;i<p.MailCount;i++)p.Mail[i]=r.Range(-25,25);
        for(int i=0;i<6;i++){int at=i*4;p.Relationships[at]=r.Range(0,255);p.Relationships[at+1]=r.Range(0,25);p.Relationships[at+2]=r.Int();p.Relationships[at+3]=r.Range(0,1);}
        return p;
    }
}
