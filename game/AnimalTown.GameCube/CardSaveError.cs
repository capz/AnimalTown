internal static class CardSaveError
{
    internal static string Describe(string phase,int operation,int mount,int recovery,string detail)
    {
        if(phase=="mount"&&mount!=0){
            string reason=mount switch{
                -1=>"Slot A is busy. Wait, then retry.",
                -2=>"Slot A did not identify a memory card.",
                -3=>"No memory card detected in Slot A.",
                -5=>"Card read failed. Check Slot A and retry.",
                -6=>"Card directory could not be verified. Data kept.",
                -13=>"Card encoding is unsupported. Data kept.",
                -2001=>"Not enough memory to open the card.",
                _=>"Memory card mount failed. Data kept."
            };
            return "Save failed / mount / CARD "+mount+"\n"+reason;
        }
        if(phase=="mount"&&recovery<0)
            return "Save failed / recovery / error "+(-recovery)+"\n"+
                (recovery==-28?"More free blocks needed to finish the pending save.":
                 recovery==-12?"Not enough memory to recover the pending save.":
                 "Pending save could not be recovered. Data kept.");
        return "Save failed / "+phase+" / card "+operation+"\n"+
            (detail.Length<=70?detail:detail.Substring(0,67)+"...");
    }
}
