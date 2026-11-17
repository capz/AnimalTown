using System.Text.Json;
using AnimalTown;

sealed partial class GameDatabase
{
    public async Task<object> Shop(Guid player,Guid host)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        if(player!=host)await CheckVisit(c,t,player,host);
        var loaded=await Load(c,t,host,true);var s=loaded.State;int day=s.Town.Day;
        s.Rollover(Day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
        if(day!=s.Town.Day)await Save(c,t,host,s);
        var shop=s.Town.Shop;
        var result=new{
            revision=shop.Revision,
            display=Enumerable.Range(0,s.Town.StockCount).Select(i=>new{slot=i,item=s.Town.Stock[i],price=shop.Price(s,ShopSource.Display,i)}).ToArray(),
            cabinet=Enumerable.Range(0,TownShop.CabinetCount).Select(i=>new{slot=i,item=TownShop.CabinetItem(i),price=shop.Price(s,ShopSource.Cabinet,i)}).ToArray(),
            consignment=Enumerable.Range(0,TownShop.SaleSlots(s)).Select(i=>new{slot=i,item=shop.SaleItems[i],price=shop.SalePrices[i],seller=shop.SaleOwners[i]}).ToArray(),
            storedConsignment=player==host?Enumerable.Range(TownShop.SaleSlots(s),TownShop.StoredSaleCount-TownShop.SaleSlots(s)).Where(i=>shop.SaleItems[i]!=0&&shop.SaleOwners[i]==s.ActiveProfile).Select(i=>new{slot=i,item=shop.SaleItems[i]}).ToArray():null
        };
        await t.CommitAsync();return result;
    }
    public async Task<object> ShopOperation(Guid player,Guid host,TownShopOp op)
    {
        if(op.Id==Guid.Empty||op.Revision<0)throw new GameError(400,"invalid-shop-operation");
        var sourceKind=op.Source switch{"display"=>ShopSource.Display,"cabinet"=>ShopSource.Cabinet,"consignment"=>ShopSource.Consignment,_=>throw new GameError(400,"invalid-shop-source")};
        string fingerprint=Hash("town-shop:"+host+":"+JsonSerializer.Serialize(op));
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        // All cross-account purchases take player locks in the existing trade order.
        Guid first=player.CompareTo(host)<0?player:host,second=first==player?host:player;
        var a=await Load(c,t,first,true);var b=first==second?a:await Load(c,t,second,true);
        var customer=first==player?a.State:b.State;var town=first==host?a.State:b.State;
        await using(var cmd=Cmd(c,t,"SELECT fingerprint,result::text FROM operations WHERE player=$1 AND id=$2",player,op.Id))await using(var r=await cmd.ExecuteReaderAsync())if(await r.ReadAsync()){
            if(r.GetString(0)!=fingerprint)throw new GameError(409,"idempotency-key-reused");
            return JsonSerializer.Deserialize<JsonElement>(r.GetString(1));
        }
        if(player!=host){await CheckVisit(c,t,player,host);if(op.Kind!="buy")throw new GameError(403,"only-local-villagers-may-stock-shop");}
        town.Rollover(Day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
        if(customer!=town)customer.Rollover(Day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
        if(op.Revision!=town.Town.Shop.Revision)throw new GameError(409,"shop-changed");
        var shop=town.Town.Shop;
        bool ok=op.Kind switch{
            "buy"=>shop.Buy(town,customer,sourceKind,op.Slot,op.Revision),
            "list"=>sourceKind==ShopSource.Consignment&&shop.List(town,op.Slot,op.Pocket,op.Price),
            "remove"=>sourceKind==ShopSource.Consignment&&shop.Remove(town,op.Slot),
            "price"=>sourceKind==ShopSource.Consignment&&shop.Reprice(town,op.Slot,op.Price),
            _=>false
        };
        if(!ok)throw new GameError(409,"shop-operation-rejected");
        await Save(c,t,host,town);if(player!=host)await Save(c,t,player,customer);
        var result=new{ok=true,revision=shop.Revision,wallet=customer.Wallet,savings=customer.Savings};
        await using(var cmd=Cmd(c,t,"INSERT INTO operations VALUES($1,$2,$3,$4::jsonb)",player,op.Id,fingerprint,JsonSerializer.Serialize(result)))await cmd.ExecuteNonQueryAsync();
        await t.CommitAsync();return result;
    }
}
