#if ANIMALTOWN_ONLINE_PLAY
using System;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

// The approved boards use the game's existing fonts, cream panels, leaf focus
// and seafoam backdrop. No town renderer is retained during card/TLS setup.
internal sealed class OnlineMenuUi:IDisposable,Hypercube.GameCube.IFrameScene
{
    readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=UiFont.BatchCapacity});
    readonly UiFont font=UiFont.LoadSystem(),heading=UiFont.Load("gorditas-bold");
    readonly MenuNavigation navigation=new MenuNavigation();
    readonly TextPages paragraphs=new TextPages(45);
    string title="Online",subtitle="",details="",hint="";
    string[] rows=new string[0];int selected;
    ChatEntry? entry;
    CachedTexture? icons;int preview;
    internal void Set(string title,string subtitle,string details,string[]? rows=null,string hint="B: back")
    {
        if(title!="Town review"&&icons!=null){TextureCache.Release(icons);icons=null;}
        preview=0;this.title=title;this.subtitle=subtitle;this.details=details;this.rows=rows??new string[0];this.hint=hint;paragraphs.Set(details);selected=0;
    }
    internal void PreviewItem(int item)
    {if(!Catalog.Valid(item))throw new ArgumentOutOfRangeException(nameof(item));icons??=TextureCache.Load(ContentPaths.Asset("item-icons.dndtex"),true);preview=item;}
    internal void Draw()
    {UiNoise.Prepare();Hypercube.GameCube.FrameRenderer.Draw(GraphicsDevice.Default,new Color32(149,198,186),this);}
    void Text(string value,float x,float y,float scale,float width,Color32 color)
    {float measured=font.Measure(value,scale);if(measured>width)scale*=width/measured;font.Draw(ui,value,x,y,color,scale);}
    public void SubmitFramePart()
    {
        ui.Begin();
        for(int y=8;y<480;y+=48)for(int x=8;x<640;x+=48)TitleArt.Ellipse(ui,x,y,2,2,new Color32(177,216,205));
        TitleArt.Panel(ui,44,24,552,432);
        if(entry!=null){DrawServiceEntry();ui.End();return;}
        float scale=Math.Min(.95f,500/heading.Measure(title));heading.Draw(ui,title,74,45,TitleArt.Ink,scale);
        Text(subtitle,76,94,.72f,488,TitleArt.Leaf);
        int count=Math.Min(rows.Length,4);float start=count==4?136:count==3?235:count>0?292:0;
        if(details.Length>0)
        {
            int lines=Math.Min(paragraphs.Lines,count==4?0:count==3?3:count>0?5:9);
            for(int i=0;i<lines;i++)Text(paragraphs.Window(i,1),76,137+i*27,.82f,488,TitleArt.Ink);
        }
        if(preview!=0)
        {
            TitleArt.Rounded(ui,245,126,150,135,22,new Color32(149,198,186));int icon=Catalog.IconId(preview);
            icons!.Draw(ui,new RectF(icon%8*64,icon/8*64,64,64),new RectF(272,144,96,96),Color32.White);
        }
        for(int i=0;i<count;i++)
        {
            TitleArt.Rounded(ui,64,start+i*53,512,43,16,i==selected?new Color32(221,231,188):new Color32(236,226,197));
            Text(rows[i],84,start+10+i*53,.9f,472,TitleArt.Ink);
        }
        Text(hint,74,429,.68f,500,TitleArt.Ink);ui.End();
    }
    internal int Choose(Action? tick=null,bool canBack=true,long until=long.MaxValue)
    {
        while(true)
        {
            if(Clock.Timestamp>=until)return -2;
            tick?.Invoke();Draw();RuntimeOptions.Wait();Controller.Update();var pad=Controller.GetState(0);
            if(canBack&&pad.B.WasPressed)return -1;
            int direction=navigation.Read("online",0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true);
            if(rows.Length>0)
            {
                if(direction==MenuNavigation.Up)selected=(selected+rows.Length-1)%rows.Length;
                if(direction==MenuNavigation.Down)selected=(selected+1)%rows.Length;
                if(pad.A.WasPressed)return selected;
            }
            else if(pad.A.WasPressed)return 0;
        }
    }
    internal string? EditService(string current)
    {
        entry=new ChatEntry(63);entry.RestoreDraft(current.StartsWith("https://")?current.Substring(8):"");
        try
        {
            while(true)
            {
                Draw();RuntimeOptions.Wait();Controller.Update();var pad=Controller.GetState(0);
                if(pad.B.WasPressed)return null;
                if(pad.Start.WasPressed)
                {
                    string origin="https://"+entry.Text;
                    if(OnlineCardCredential.ValidOrigin(origin))return origin;
                    entry.Status="Enter the service hostname without a path.";continue;
                }
                entry.Move(navigation.Read("online-service",0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true));
                if(pad.L.WasPressed)entry.ToggleCase();if(pad.R.WasPressed)entry.Append(' ');if(pad.X.WasPressed)entry.Erase();if(pad.A.WasPressed)entry.Type();
            }
        }
        finally{entry=null;}
    }
    void DrawServiceEntry()
    {
        heading.Draw(ui,"Online service",74,45,TitleArt.Ink,.95f);Text("Use the HTTPS hostname from your backend.",76,94,.72f,488,TitleArt.Leaf);
        TitleArt.Rounded(ui,64,126,512,56,16,new Color32(236,226,197));Text("https://"+entry!.Text,76,144,.82f,488,TitleArt.Ink);
        Text(entry.Status,76,187,.65f,488,TitleArt.Ink);
        for(int i=0;i<ChatEntry.Columns*ChatEntry.Rows;i++)
        {
            float x=67+i%10*51,y=220+i/10*35;
            TitleArt.Rounded(ui,x,y,47,31,11,i==entry.Selected?TitleArt.Leaf:new Color32(221,231,188));
            Text(entry.Label(i),x+12,y+8,.75f,42,i==entry.Selected?TitleArt.Cream:TitleArt.Ink);
        }
        Text("A: type   X: erase   START: save   B: back",74,429,.68f,500,TitleArt.Ink);
    }
    public void Dispose(){if(icons!=null)TextureCache.Release(icons);icons=null;heading.Release();font.Release();ui.Dispose();}
}
#endif
