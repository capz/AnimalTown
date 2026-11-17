using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;

internal sealed partial class TownGame
{
    private int shopFixture,shopRevision,shopPocket,shopPrice,shopDigit=2,shopInspection;
    private bool shopReprice;
    private int shopHintFixture=-1;
    private string shopHint="";
    private bool ShopScreen=>screen=="shop-display"||screen=="shop-list"||screen=="shop-price";
    private ShopSource ShopKind=>shopFixture<12?ShopSource.Display:ShopSource.Consignment;
    private int ShopSlot=>shopFixture<12?shopFixture:shopFixture-12;
    private bool OwnListing=>shopFixture>=12&&state.Town.Shop.SaleItems[ShopSlot]!=0&&state.Town.Shop.SaleOwners[ShopSlot]==state.ActiveProfile;
    private void InspectShop()
    {
        if(villagers.Doors.Busy(0))return;
        if(room==0){BuildingWorld.Entrance(state,7,out state.X,out state.Z);facingYaw=(float)Math.PI;shopInspection=0;}
        else if(room==TownShop.Room){ShopLayout.Approach(shopInspection++%ShopLayout.FixtureCount,out state.X,out state.Z);facingYaw=(float)Math.PI;}
        state.Facing=facingYaw;screen="world";waterInspection=0;cliffPreview=false;
    }
    private bool OpenShopFixture()
    {
        int fixture=ShopLayout.Near(state.X,state.Z);if(fixture<0)return false;
        selected=0;
        if(fixture==ShopLayout.Counter){screen="store";return true;}
        if(fixture==ShopLayout.Cabinet){screen="shop";return true;}
        shopFixture=fixture;shopRevision=state.Town.Shop.Revision;screen="shop-display";return true;
    }
    private bool ShopInput(ControllerState p,int direction)
    {
        if(!ShopScreen)return false;
        bool up=direction==MenuNavigation.Up,down=direction==MenuNavigation.Down;
        if(p.B.WasPressed){screen=screen=="shop-price"&&!shopReprice?"shop-list":screen=="shop-list"||screen=="shop-price"?"shop-display":"world";selected=0;return true;}
        if(screen=="shop-price"){
            if(direction==MenuNavigation.Left)shopDigit=Math.Min(4,shopDigit+1);
            if(direction==MenuNavigation.Right)shopDigit=Math.Max(0,shopDigit-1);
            int step=1;for(int i=0;i<shopDigit;i++)step*=10;
            if(up||down)shopPrice=Math.Clamp(shopPrice+(up?step:-step),1,99999);
            if(p.A.WasPressed){
                bool ok=shopRevision==state.Town.Shop.Revision&&(shopReprice?state.Town.Shop.Reprice(state,ShopSlot,shopPrice):state.Town.Shop.List(state,ShopSlot,shopPocket,shopPrice));
                Say("Hearth",ok?"Your item is on display. Sale earnings go to your savings.":"The display changed. Please check it again.");
            }
            return true;
        }
        if(screen=="shop-list"){
            if(up)selected=(selected+state.Capacity-1)%state.Capacity;if(down)selected=(selected+1)%state.Capacity;
            if(p.A.WasPressed&&TownShop.Sellable(state.Pockets[selected])){shopPocket=selected;int item=state.Pockets[selected];shopPrice=Math.Max(1,Catalog.Items[item].Buy>0?Catalog.Items[item].Buy:Catalog.Items[item].Sell);shopDigit=2;shopReprice=false;screen="shop-price";}
            return true;
        }
        if(OwnListing&&(up||down))selected=1-selected;
        if(!p.A.WasPressed)return true;
        int id=state.Town.Shop.Item(state,ShopKind,ShopSlot);
        if(shopRevision!=state.Town.Shop.Revision){Say("Hearth","The displays changed while you were browsing. Please check again.");return true;}
        if(OwnListing){
            if(selected==0){shopPrice=state.Town.Shop.SalePrices[ShopSlot];shopReprice=true;screen="shop-price";}
            else Say("Hearth",state.Town.Shop.Remove(state,ShopSlot)?"Your item is back in your pockets.":"Make a free pocket before taking your item back.");
        }else if(id==0&&shopFixture>=12){screen="shop-list";selected=0;}
        else if(id!=0){bool bought=state.Town.Shop.Buy(state,state,ShopKind,ShopSlot,shopRevision,godMode);Say("Hearth",bought?"Thank you! This display is now sold out.":"You need enough acorns and a free pocket.");}
        return true;
    }
    private bool DrawShopPanel(Color32 ink)
    {
        if(!ShopScreen)return false;
        Panel(45,75,550,350);Heading(screen=="shop-list"?"Choose an item to display":screen=="shop-price"?"Set your asking price":shopFixture<12?"Today's display":"Villager sale / "+(ShopSlot+1),68,91,ink);
        if(screen=="shop-price"){
            int item=shopReprice?state.Town.Shop.SaleItems[ShopSlot]:state.Pockets[shopPocket];
            Text(Catalog.Items[item].Name,68,147,ink,1.5f);Strong(shopPrice.ToString("D5")+" acorns",68,201,ink,2);
            int step=1;for(int i=0;i<shopDigit;i++)step*=10;
            Text("Adjust by "+step+" acorns",68,263,ink,1.25f);Text("Left/Right: digit   Up/Down: price",68,318,ink,1.1f);
            Text("A: put on display   B: back",68,382,ink,1.2f);return true;
        }
        if(screen=="shop-list"){
            int first=selected/7*7;for(int i=first;i<Math.Min(first+7,state.Capacity);i++){
                int item=state.Pockets[i];string label=(i+1)+". "+(item==0?"Empty":Catalog.Items[item].Name)+(item!=0&&!TownShop.Sellable(item)?" / cannot list":"");
                float y=140+(i-first)*31;if(selected==i)Rounded(57,y,520,29,14,new Color32(221,231,188));Text((selected==i?"> ":"  ")+label,68,y,ink,1.3f);
            }
            Text("A: choose   B: back   One item per stand",68,382,ink,1.1f);return true;
        }
        int id=state.Town.Shop.Item(state,ShopKind,ShopSlot),price=state.Town.Shop.Price(state,ShopKind,ShopSlot);
        bool growing=shopFixture<12&&shopFixture>=state.Town.StockCount;
        Text(id==0?growing?"More stands open as the shop grows":shopFixture<12?"Sold out for today":"This stand is available":Catalog.Items[id].Name,68,151,ink,1.5f);
        if(id!=0){Strong(price+" acorns",68,202,ink,1.5f);Text("In your wallet: "+state.Wallet,68,243,ink,1.2f);}
        if(shopFixture>=12&&id!=0)Text("Seller: "+state.ProfileName(state.Town.Shop.SaleOwners[ShopSlot]),68,287,ink,1.2f);
        if(OwnListing){Text((selected==0?"> ":"  ")+"Change price",68,327,ink,1.2f);Text((selected==1?"> ":"  ")+"Take item back",68,355,ink,1.2f);}
        Text(OwnListing?"A: select   B: close":id!=0?"A: buy this item   B: close":growing?"Shop with us to unlock more stands.   B: close":shopFixture<12?"New displays arrive tomorrow.   B: close":"A: list an item for sale   B: close",68,392,ink,1.3f);
        return true;
    }
    private void DrawShopHint(Color32 ink)
    {
        int fixture=ShopLayout.Near(state.X,state.Z);if(fixture<0||villagers.Doors.Busy(0)||villagers.Doors.Nearby(state,0,facingYaw)>=0)return;
        if(fixture!=shopHintFixture){shopHintFixture=fixture;shopHint="A  "+(fixture==ShopLayout.Cabinet?"Essentials / unlimited stock":fixture==ShopLayout.Counter?"Service counter":fixture<12?"Daily display "+(fixture+1):"Villager sale "+(fixture-11));}
        Panel(38,26,564,48);Strong(shopHint,57,39,ink,1.4f);
    }
    private void DrawShopStock()
    {
        for(int i=0;i<20;i++){
            ShopLayout.Bounds(i,out float x,out float z,out float w,out float d,out float h);x+=w*.5f;z+=d*.5f;
            if(!indoorCamera.Sphere(x,h+.4f,z,.65f,8))continue;
            int id=state.Town.Shop.Item(state,i<12?ShopSource.Display:ShopSource.Consignment,i<12?i:i-12);
            if(i<12&&i>=state.Town.StockCount)continue;
            if(id==0){Box(x-.13f,h+.01f,z-.08f,.26f,.03f,.16f,i<12?new Color32(174,72,56):new Color32(90,147,111));continue;}
            var wood=new Color32(163,111,65);var cream=new Color32(243,219,158);
            if(id==10){Box(x-.2f,h+.2f,z-.19f,.4f,.08f,.38f,wood);Box(x-.2f,h+.28f,z-.19f,.4f,.38f,.07f,wood);for(int k=0;k<4;k++)Box(x-.18f+(k%2)*.29f,h,z-.17f+(k/2)*.27f,.07f,.2f,.07f,wood);}
            else if(id==11||id==13){Box(x-.25f,h+.35f,z-.2f,.5f,.08f,.4f,wood);Box(x-.2f,h,z-.15f,.06f,.35f,.3f,wood);Box(x+.14f,h,z-.15f,.06f,.35f,.3f,wood);if(id==13)Box(x-.27f,h+.8f,z-.23f,.54f,.08f,.46f,new Color32(81,152,119));}
            else if(id==12||id==25){Box(x-.14f,h,z-.14f,.28f,.06f,.28f,wood);Box(x-.025f,h+.06f,z-.025f,.05f,.4f,.05f,wood);Box(x-.15f,h+.4f,z-.15f,.3f,.18f,.3f,cream);}
            else{var color=Catalog.Has(id,ItemFlags.Plant)?new Color32(108,171,93):Catalog.Has(id,ItemFlags.Tool)?new Color32(120,145,165):new Color32(223,174,119);Box(x-.17f,h,z-.15f,.34f,.3f,.3f,color);Box(x-.025f,h+.3f,z-.16f,.05f,.025f,.32f,cream);}
        }
    }
}
