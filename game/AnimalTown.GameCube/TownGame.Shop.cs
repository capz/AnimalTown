using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;

internal sealed partial class TownGame
{
    // The cream panel's rounded sides and offset shadow fit inside the right half.
    private const float ShopPanelLeft=336,ShopPanelWidth=270,ShopPanelTop=59,ShopPanelHeight=356;
    private const float ShopTextLeft=353,ShopTextRight=589,ShopFooterY=383;
    private static readonly string[] StoreActions={
        "Essentials cabinet","Sell / return parcel","Part-time job","House expansion","Garden expansion",
        "Repay loan","Daily job","Expand backpack","Reclaim stored sale items"
    };
    private int shopFixture,shopRevision,shopPocket,shopPrice,shopDigit=2,shopInspection;
    private bool shopReprice;
    private int shopHintFixture=-1;
    private string shopHint="";
    private bool ShopScreen=>screen=="shop-display"||screen=="shop-list"||screen=="shop-price"||screen=="shop-stored";
    private int StoredShopCount=>state.Town.Shop.StoredListingCount(state);
    private int StoreMenuCount=>StoredShopCount>0?StoreActions.Length:StoreActions.Length-1;
    private ShopSource ShopKind=>shopFixture<12?ShopSource.Display:ShopSource.Consignment;
    private int ShopSlot=>shopFixture<12?shopFixture:shopFixture-12;
    private bool OwnListing=>shopFixture>=12&&state.Town.Shop.SaleItems[ShopSlot]!=0&&state.Town.Shop.SaleOwners[ShopSlot]==state.ActiveProfile;
    private bool CanSellFromPockets(int slot)=>Catalog.Has(state.Pockets[slot],ItemFlags.Sell)&&!state.IsJobItem(slot);
    private void InspectShop()
    {
        if(villagers.Doors.Busy(0))return;
        if(room==0){BuildingWorld.Entrance(state,7,out state.X,out state.Z);facingYaw=(float)Math.PI;shopInspection=0;}
        else if(room==TownShop.Room){ShopLayout.Approach(ShopLayout.Inspect(state,shopInspection++),out state.X,out state.Z);facingYaw=(float)Math.PI;}
        state.Facing=facingYaw;screen="world";waterInspection=0;cliffPreview=false;
    }
    private bool OpenShopFixture()
    {
        int fixture=ShopLayout.Near(state,state.X,state.Z);if(fixture<0)return false;
        selected=0;
        if(fixture==ShopLayout.Counter){screen="store";return true;}
        if(fixture==ShopLayout.Cabinet){screen="shop";return true;}
        shopFixture=fixture;shopRevision=state.Town.Shop.Revision;screen="shop-display";return true;
    }
    private bool ShopInput(ControllerState p,int direction)
    {
        if(!ShopScreen)return false;
        bool up=direction==MenuNavigation.Up,down=direction==MenuNavigation.Down;
        if(screen=="shop-stored"){
            int count=StoredShopCount;
            if(p.B.WasPressed){screen="store";selected=0;return true;}
            if(count>0){
                selected=Math.Clamp(selected,0,count-1);
                if(up)selected=(selected+count-1)%count;if(down)selected=(selected+1)%count;
                if(p.A.WasPressed)Say("Hearth",state.Town.Shop.Remove(state,state.Town.Shop.StoredListing(state,selected))?"Your stored item is back in your pockets.":"Make a free pocket before reclaiming this item.");
            }
            return true;
        }
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
        if(screen=="store"||screen=="shop"){
            bool cabinet=screen=="shop";
            DrawShopFrame(cabinet?"Essentials cabinet":"Hearth's store",ink);
            if(cabinet)ShopText("Always in stock",ShopTextLeft,107,ink,1);
            int count=cabinet?TownShop.CabinetCount:StoreMenuCount;
            for(int i=0;i<count;i++){
                float y=cabinet?130+i*24:111+i*27;
                if(cabinet){
                    var item=Catalog.Items[TownShop.CabinetItem(i)];
                    DrawShopRow(item.Name,i,y,ink,godMode?"Free":UiFont.Money(item.Buy));
                }
                else DrawShopRow(StoreActions[i],i,y,ink);
            }
            DrawShopFooter(ink,"close",cabinet?"buy":"select");
            return true;
        }
        if(screen=="sell"){DrawShopPockets(ink);return true;}
        if(!ShopScreen)return false;
        if(screen=="shop-stored"){
            DrawShopFrame("Reclaim stored items",ink);
            ShopText("Items kept behind the counter",ShopTextLeft,111,ink,1.05f);
            int count=StoredShopCount;
            for(int i=0;i<count;i++){
                int slot=state.Town.Shop.StoredListing(state,i),item=state.Town.Shop.SaleItems[slot];
                DrawShopRow(Catalog.Items[item].Name,i,143+i*25,ink);
            }
            if(count==0)ShopText("No stored items.",ShopTextLeft,145,ink,1.15f);
            DrawShopFooter(ink,"back",count>0?"take item back":null);return true;
        }
        if(screen=="shop-price"){
            DrawShopFrame("Set your asking price",ink);
            int item=shopReprice?state.Town.Shop.SaleItems[ShopSlot]:state.Pockets[shopPocket];
            ShopText(Catalog.Items[item].Name,ShopTextLeft,140,ink,1.35f);
            Strong(UiFont.Currency+" "+shopPrice.ToString("D5"),ShopTextLeft,183,ink,1.85f);
            int step=1;for(int i=0;i<shopDigit;i++)step*=10;
            ShopText("Adjust by "+UiFont.Money(step),ShopTextLeft,237,ink,1.2f);
            ShopText("Left / right   Digit\nUp / down   Price",ShopTextLeft,281,ink,1.05f);
            DrawShopFooter(ink,"back","put on display");return true;
        }
        if(screen=="shop-list"){
            DrawShopFrame("Choose an item to display",ink);
            int first=selected/7*7;for(int i=first;i<Math.Min(first+7,state.Capacity);i++){
                int item=state.Pockets[i];string label=(i+1)+". "+(item==0?"Empty":Catalog.Items[item].Name)+(item!=0&&!TownShop.Sellable(item)?" / cannot list":"");
                DrawShopRow(label,i,125+(i-first)*30,ink);
            }
            ShopText("One item per stand",ShopTextLeft,348,ink,1);
            DrawShopFooter(ink,"back","choose");return true;
        }
        int id=state.Town.Shop.Item(state,ShopKind,ShopSlot),price=state.Town.Shop.Price(state,ShopKind,ShopSlot);
        bool growing=shopFixture<12&&shopFixture>=state.Town.StockCount;
        bool own=OwnListing;
        DrawShopFrame(shopFixture<12?"Today's display":"Villager sale / "+(ShopSlot+1),ink,98,own?286:246);
        string labelText=id!=0?Catalog.Items[id].Name:growing?"More stands open as the shop grows":shopFixture<12?"Sold out for today":"This stand is available";
        ShopText(labelText,ShopTextLeft,154,ink,1.4f,true);
        if(id!=0){
            Strong(UiFont.Money(price),ShopTextLeft,187,ink,1.8f);
            ShopText("In your wallet   "+UiFont.Money(state.Wallet),ShopTextLeft,230,ink,1.15f);
            if(shopFixture>=12)ShopText("Seller: "+state.ProfileName(state.Town.Shop.SaleOwners[ShopSlot]),ShopTextLeft,254,ink,1.1f);
        }else if(shopFixture<12){
            ShopText(growing?"Shop with us to unlock more stands.":"New displays arrive tomorrow.",ShopTextLeft,203,ink,1.1f);
        }
        if(own){
            DrawShopRow("Change price",0,279,ink);
            DrawShopRow("Take item back",1,305,ink);
        }
        string? action=own?"select":id!=0?"buy":shopFixture>=12?"list an item":null;
        DrawShopFooter(ink,"close",action,own?352:309);
        return true;
    }
    private void DrawShopFrame(string title,Color32 ink,float y=ShopPanelTop,float height=ShopPanelHeight)
    {
        Panel(ShopPanelLeft,y,ShopPanelWidth,height);
        float scale=Math.Min(.85f,(ShopTextRight-ShopTextLeft)/Math.Max(1,headingFont.Measure(title,1)));
        Heading(title,ShopTextLeft,y+13,ink,scale);
    }
    private void ShopText(string text,float x,float y,Color32 ink,float scale,bool strong=false,float right=ShopTextRight)
    {
        var type=strong?strongFont:font;
        float width=type.Measure(text,scale/1.5f);
        type.Draw(ui,text,x,y,ink,scale/1.5f*Math.Min(1,(right-x)/Math.Max(1,width)));
    }
    private void DrawShopFooter(Color32 ink,string back,string? action,float y=ShopFooterY)
    {
        const float labelOffset=20,promptGap=12,labelScale=1;
        float x=ShopTextLeft;
        if(action!=null){
            ButtonGlyph("A",x,y);
            x+=labelOffset;
            Text(action,x,y,ink,labelScale);
            x+=font.Measure(action,labelScale/1.5f)+promptGap;
        }
        ButtonGlyph("B",x,y);
        Text(back,x+labelOffset,y,ink,labelScale);
    }
    private void DrawShopRow(string label,int row,float y,Color32 ink,string? price=null)
    {
        bool active=row==selected;
        if(active)Rounded(347,y,248,23,11,new Color32(221,231,188));
        float labelRight=ShopTextRight;
        if(price!=null){
            float priceX=ShopTextRight-font.Measure(price,1.05f/1.5f);
            Text(price,priceX,y+2,ink,1.05f);
            labelRight=priceX-8;
        }
        ShopText(active?"> "+label:label,ShopTextLeft,y+2,ink,1.15f,active,labelRight);
    }
    private void DrawShopPockets(Color32 ink)
    {
        const float gridTop=116;
        float details=gridTop+(state.Capacity/5)*34+11;
        DrawShopFrame("Sell items",ink,ShopPanelTop,details+84-ShopPanelTop);
        Rounded(483,75,108,24,12,new Color32(232,183,86));
        string wallet=UiFont.Money(state.Wallet);
        Strong(wallet,537-strongFont.Measure(wallet,1.05f/1.5f)*.5f,76,ink,1.05f);
        for(int i=0;i<state.Capacity;i++){
            float x=ShopTextLeft+i%5*49,y=gridTop+i/5*34;
            if(i==selected)Rounded(x-2,y-2,47,34,12,new Color32(232,183,86));
            Rounded(x,y,43,30,11,i==selected?new Color32(255,251,238):new Color32(231,228,202));
            int id=state.Pockets[i];
            if(id!=0){
                int icon=Catalog.IconId(id);
                itemIcons.Draw(ui,new RectF(icon%8*64,icon/8*64,64,64),new RectF(x+8,y+1,28,28),Color32.White);
            }else Rounded(x+20,y+13,4,4,2,new Color32(188,195,166));
        }
        int item=state.Pockets[selected];
        bool sellable=CanSellFromPockets(selected);
        if(item!=0){
            ShopText(itemTitleText.Get(BackpackJobTitle(selected,item)),ShopTextLeft,details,ink,1.15f,true);
            ShopText(sellable?"Sell for "+UiFont.Money(Catalog.Items[item].Sell):"Not for sale",ShopTextLeft,details+22,ink,1.05f);
        }
        DrawShopFooter(ink,"close",sellable?"sell":null,details+52);
    }
    private void DrawShopHint(Color32 ink)
    {
        int fixture=ShopLayout.Near(state,state.X,state.Z);if(fixture<0||villagers.Doors.Busy(0)||villagers.Doors.Nearby(state,0,facingYaw)>=0)return;
        if(fixture!=shopHintFixture){shopHintFixture=fixture;shopHint="A  "+(fixture==ShopLayout.Cabinet?"Essentials / unlimited stock":fixture==ShopLayout.Counter?"Service counter":fixture<12?"Daily display "+(fixture+1):"Villager sale "+(fixture-11));}
        Panel(38,26,564,48);Strong(shopHint,57,39,ink,1.4f);
    }
    private void DrawShopStock()
    {
        for(int i=0;i<TownShop.DisplayCount+TownShop.SaleCount;i++){
            if(!ShopLayout.Active(state,i))continue;
            ShopLayout.Bounds(i,out float x,out float z,out float w,out float d,out float h);x+=w*.5f;z+=d*.5f;
            if(!indoorCamera.Sphere(x,h*.5f+.35f,z,.9f,8))continue;
            if(i>=TownShop.DisplayCount){
                Box(x-w*.5f,0,z-d*.5f,w,h-.05f,d,new Color32(52,124,107));
                Box(x-w*.5f,h-.05f,z-d*.5f,w,.05f,d,new Color32(246,236,204));
            }
            int id=state.Town.Shop.Item(state,i<12?ShopSource.Display:ShopSource.Consignment,i<12?i:i-12);
            if(id==0){Box(x-.13f,h+.01f,z-.08f,.26f,.03f,.16f,i<12?new Color32(174,72,56):new Color32(90,147,111));continue;}
            var wood=new Color32(163,111,65);var cream=new Color32(243,219,158);
            if(id==10){Box(x-.2f,h+.2f,z-.19f,.4f,.08f,.38f,wood);Box(x-.2f,h+.28f,z-.19f,.4f,.38f,.07f,wood);for(int k=0;k<4;k++)Box(x-.18f+(k%2)*.29f,h,z-.17f+(k/2)*.27f,.07f,.2f,.07f,wood);}
            else if(id==11||id==13){Box(x-.25f,h+.35f,z-.2f,.5f,.08f,.4f,wood);Box(x-.2f,h,z-.15f,.06f,.35f,.3f,wood);Box(x+.14f,h,z-.15f,.06f,.35f,.3f,wood);if(id==13)Box(x-.27f,h+.8f,z-.23f,.54f,.08f,.46f,new Color32(81,152,119));}
            else if(id==12||id==25){Box(x-.14f,h,z-.14f,.28f,.06f,.28f,wood);Box(x-.025f,h+.06f,z-.025f,.05f,.4f,.05f,wood);Box(x-.15f,h+.4f,z-.15f,.3f,.18f,.3f,cream);}
            else{var color=Catalog.Has(id,ItemFlags.Plant)?new Color32(108,171,93):Catalog.Has(id,ItemFlags.Tool)?new Color32(120,145,165):new Color32(223,174,119);Box(x-.17f,h,z-.15f,.34f,.3f,.3f,color);Box(x-.025f,h+.3f,z-.16f,.05f,.025f,.32f,cream);}
        }
    }
}
