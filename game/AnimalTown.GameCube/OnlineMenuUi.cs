#if ANIMALTOWN_ONLINE_PLAY
using System;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Memory;
using Dolphin.Timing;

// The approved boards use the game's existing fonts, cream panels, leaf focus
// and seafoam backdrop. The approved review layout adds a read-only world pane;
// ordinary card/TLS setup does not retain a town renderer.
internal sealed class OnlineMenuUi:IDisposable,Hypercube.GameCube.IFrameScene
{
    readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=UiFont.BatchCapacity});
    readonly UiFont font=UiFont.LoadSystem(),heading=UiFont.Load("gorditas-bold");
    readonly MenuNavigation navigation=new MenuNavigation();
    readonly TextPages paragraphs=new TextPages(45);
    readonly TextPages reviewParagraphs=new TextPages(28);
    string title="Online",subtitle="",details="",hint="";
    string[] rows=new string[0];int selected;
    ChatEntry? entry;
    CachedTexture? icons;int preview;
    TownGame? reviewWorld;string reviewSubject="Current town";bool reviewLoading;
    Action<int>? selectionChanged;
    internal void Set(string title,string subtitle,string details,string[]? rows=null,string hint="B: back")
    {
        if(title!="Town review"&&icons!=null){TextureCache.Release(icons);icons=null;}
        preview=0;selectionChanged=null;this.title=title;this.subtitle=subtitle;this.details=details;this.rows=rows??new string[0];this.hint=hint;paragraphs.Set(details);reviewParagraphs.Set(details);selected=0;
    }
    internal void ReviewScene(GameState scene,DevConfig config)
    {
        if(reviewWorld==null)
        {
            if(!Aram.Initialize(2*1024*1024))throw new InvalidOperationException("Review scene ARAM reservation failed.");
            reviewWorld=new TownGame(config,TownSession.Review(scene));reviewWorld.BeginReview(this);
        }
        else reviewWorld.UpdateReview(scene);
    }
    internal void ReviewFocus(int room,float x,float z,string subject)
    {reviewSubject=subject;reviewWorld?.FocusReview(room,x,z);}
    internal void ReviewSelection(Action<int> changed)
    {selectionChanged=changed;changed(selected);}
    internal void EndReview(){reviewWorld?.Dispose();reviewWorld=null;selectionChanged=null;}
    internal void PreviewItem(int item)
    {if(!Catalog.Valid(item))throw new ArgumentOutOfRangeException(nameof(item));icons??=TextureCache.Load(ContentPaths.Asset("item-icons.dndtex"),true);preview=item;}
    internal void Draw()
    {
        UiNoise.Prepare();
        if(reviewWorld!=null){reviewWorld.AdvanceLoading();reviewWorld.AdvanceResidency();reviewWorld.Draw();reviewWorld.AdvanceResidency(true);return;}
        Hypercube.GameCube.FrameRenderer.Draw(GraphicsDevice.Default,new Color32(149,198,186),this);
    }
    internal void DrawReviewLoading()
    {
        reviewLoading=true;
        try{Hypercube.GameCube.FrameRenderer.Draw(GraphicsDevice.Default,new Color32(149,198,186),this);}
        finally{reviewLoading=false;}
    }
    void Text(string value,float x,float y,float scale,float width,Color32 color)
    {float measured=font.Measure(value,scale);if(measured>width)scale*=width/measured;font.Draw(ui,value,x,y,color,scale);}
    public void SubmitFramePart()
    {
        ui.Begin();
        if(reviewWorld!=null){DrawReviewPanel();ui.End();return;}
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
    void DrawReviewPanel()
    {
        ui.FillRectangle(new RectF(320,0,320,480),TitleArt.Ink);
        ui.FillRectangle(new RectF(324,0,316,480),TitleArt.Cream);
        float scale=Math.Min(.82f,280/heading.Measure(title));heading.Draw(ui,title,340,24,TitleArt.Ink,scale);
        Text(subtitle,340,67,.64f,282,TitleArt.Leaf);
        int count=Math.Min(rows.Length,4),limit=count==4?5:7;
        for(int i=0;i<Math.Min(reviewParagraphs.Lines,limit);i++)Text(reviewParagraphs.Window(i,1),340,106+i*24,.7f,280,TitleArt.Ink);
        float start=count==4?244:302;
        for(int i=0;i<count;i++)
        {
            TitleArt.Rounded(ui,336,start+i*47,288,42,14,i==selected?new Color32(221,231,188):new Color32(236,226,197));
            Text(rows[i],350,start+12+i*47,.75f,260,TitleArt.Ink);
        }
        Text(hint,340,452,.58f,280,TitleArt.Ink);
        TitleArt.Rounded(ui,12,15,296,32,14,TitleArt.Cream);Text("Current town",24,23,.68f,270,TitleArt.Ink);
        TitleArt.Rounded(ui,12,398,296,66,16,TitleArt.Cream);
        Text(reviewSubject,24,412,.72f,270,TitleArt.Ink);
        Text(reviewLoading?"Loading the current scene…":"Stick: inspect the current scene",24,439,.6f,270,TitleArt.Ink);
    }
    internal int Choose(Action? tick=null,bool canBack=true,long until=long.MaxValue)
    {
        while(true)
        {
            if(Clock.Timestamp>=until)return -2;
            tick?.Invoke();Draw();RuntimeOptions.Wait();Controller.Update();var pad=Controller.GetState(0);
            if(canBack&&pad.B.WasPressed)return -1;
            if(reviewWorld!=null)reviewWorld.InspectReview(pad);
            int direction=navigation.Read("online",0,reviewWorld==null?pad.Stick.X:0,reviewWorld==null?pad.Stick.Y:0,(int)pad.PressedButtons,Clock.Timestamp,true);
            if(rows.Length>0)
            {
                if(direction==MenuNavigation.Up)selected=(selected+rows.Length-1)%rows.Length;
                if(direction==MenuNavigation.Down)selected=(selected+1)%rows.Length;
                if(direction!=0)selectionChanged?.Invoke(selected);
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
    public void Dispose(){EndReview();if(icons!=null)TextureCache.Release(icons);icons=null;heading.Release();font.Release();ui.Dispose();}
}
#endif
