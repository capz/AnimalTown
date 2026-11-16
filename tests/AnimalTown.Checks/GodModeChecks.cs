using AnimalTown;

internal static class GodModeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(73);s.Rollover(100,100);
        check(s.GiveMoney(10000,false)==0&&s.Wallet==0,"disabled God mode cannot grant money");
        check(s.GiveMoney(10000,true)==10000&&s.Wallet==10000,"God mode grants money through a bounded credit");
        check(s.GiveMoney(int.MaxValue,true)==89999&&s.Wallet==99999,"large grants cap at the save-compatible wallet limit without overflow");
        check(s.GiveMoney(1,true)==0&&s.GiveMoney(-100,true)==0&&s.Wallet==99999,"full-wallet and negative grants leave balances unchanged");
        s.Wallet=0;int id=s.Town.Stock[0],price=Catalog.Items[id].Buy;
        check(!s.Buy(0)&&s.Town.Stock[0]==id,"ordinary purchases still require payment");
        check(s.Buy(0,true)&&s.Wallet==0&&s.Find(id)>=0&&s.Town.Stock[0]==0&&s.Town.Spending==price,
            "free stock purchases retain inventory, stock and store progression semantics");
        check(!s.Buy(0,true),"God mode cannot buy a sold-out slot");
        for(int i=0;i<s.Capacity;i++)s.Pockets[i]=1;
        id=s.Town.Stock[1];int spending=s.Town.Spending;
        check(!s.Buy(1,true)&&s.Town.Stock[1]==id&&s.Town.Spending==spending&&s.Wallet==0,
            "free purchases with full pockets fail atomically");
        check(!s.UpgradeBackpack()&&s.UpgradeBackpack(true)&&s.Capacity==20&&s.Wallet==0,
            "only the explicit free option waives backpack payment");
        check(s.UpgradeBackpack(true)&&s.Capacity==30&&!s.UpgradeBackpack(true),"free backpacks retain their maximum tier");
        check(!s.Repay(100)&&s.Debt==24000&&s.Repay(24000,true)&&s.Wallet==0&&s.Debt==0,
            "free repayment clears debt without creating or spending wallet funds");
        check(!s.Repay(1,true),"free repayment cannot overpay the loan");
        check(s.PlaceHouse(12,22,false),"prepare a valid test home");
        s.Debt=24000;
        check(!s.Expand(true,100,true),"free garden upgrades retain the house-tier requirement");
        check(!s.Expand(false,100)&&s.Expand(false,100,true)&&s.Debt==24000&&s.Wallet==0,
            "free expansion bypasses payment while retaining unrelated existing debt");
        check(!s.Expand(false,100,true)&&s.PendingDay==101,"free expansion still allows only one pending construction and keeps overnight timing");
        s.Rollover(101,100);check(s.HouseTier==1&&s.PendingKind==0,"free construction completes through normal rollover");
        check(!s.PlaceHouse(-1,-1,true,true)&&s.Wallet==0,"free relocation retains plot validation");
        check(!s.PlaceHouse(12,22,true)&&s.PlaceHouse(12,22,true,true)&&s.Wallet==0,
            "valid free relocation waives the 50,000-acorn fee");
        Array.Clear(s.Pockets);Array.Clear(s.Owned);
        check(s.Receive(16)&&s.Find(16)>=0&&s.Owned[16]!=0&&s.Wallet==0,"catalog debug grants can receive previously unowned non-store items");
        s.GiveMoney(10000,true);
        var restored=SaveCodec.Decode(SaveCodec.Encode(s));
        check(restored.Wallet==10000&&restored.Find(16)>=0,"granted possessions remain ordinary save data");
        restored.Wallet=0;
        check(!restored.UpgradeBackpack()&&!restored.Buy(1)&&!restored.Repay(1),
            "saving a free transaction never makes future transactions free by default");
    }
}
