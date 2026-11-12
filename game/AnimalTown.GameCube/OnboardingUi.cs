using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

// One short-lived surface per setup stage. No setup-only GPU resource or layout
// array survives Dispose. Each view owns its canvas outside the town lifetime.
internal sealed class OnboardingUi : IDisposable
{
    internal const int Name=0,Look=1,Review=2;
    private static readonly Color32 Honey=new Color32(255,183,64),Warm=new Color32(245,226,186),Sage=new Color32(158,181,136),Clay=new Color32(195,109,84);
    private Draw2D? ui;
    private TitleBackdrop? background;
    private UiFont? font,heading;
    private CachedTexture? logo;
    private CachedTexture?[]? portraits;
    private string[]? files,labels,keys,counts;
    private string? cachedEntry;
    private float entryScale;
    private bool disposed,counted;
    internal static int LiveViews {get;private set;}
    internal bool IsDisposed=>disposed;
    internal int PortraitBytes
    {
        get{int bytes=0;if(portraits!=null)for(int i=0;i<portraits.Length;i++)if(portraits[i]!=null)bytes+=portraits[i]!.Bytes;return bytes;}
    }

    internal OnboardingUi(int stage,int species=0)
    {
        try{
            if(stage<Name||stage>Review||(uint)species>=6)throw new ArgumentOutOfRangeException("stage/species");
            ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=128});
            background=new TitleBackdrop();font=UiFont.LoadSystem();heading=UiFont.Load("gorditas-bold");
            logo=TextureCache.Load("dvd:/wordmark.dndtex",true);
            portraits=new CachedTexture?[6];
            files=new[]{"fox","bunny","giraffe","cat","hamster","spaniel"};
            labels=new[]{"Fox","Bunny","Giraffe","Maine coon","Hamster","Spaniel"};
            if(stage==Name){
                LoadPortrait(5);keys=new string[40];counts=new string[17];
                const string glyphs="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .!?";
                for(int i=0;i<40;i++)keys[i]=glyphs[i]==' '?"space":glyphs.Substring(i,1);
                for(int i=0;i<17;i++)counts[i]=i+" / 16";
            }else if(stage==Look){for(int i=0;i<6;i++)LoadPortrait(i);}
            else LoadPortrait(species);
            LiveViews++;counted=true;
        }catch{Dispose();throw;}
    }
    private void LoadPortrait(int species)
    {portraits![species]=TextureCache.Load("dvd:/picker-"+files![species]+".dndtex",true);}
    private void Begin(string title)
    {
        if(disposed)throw new ObjectDisposedException("OnboardingUi");
        UiNoise.Prepare();GraphicsDevice.Default.BeginFrame(TitleBackdrop.Color);ui!.Begin();background!.Draw(ui);
        TitleArt.Panel(ui,36,18,568,440);
        TitleArt.Rounded(ui,312,27,16,17,8,TitleArt.Leaf);TitleArt.Rounded(ui,317,34,6,11,3,TitleArt.Cream);
        TitleArt.Rounded(ui,309,21,8,5,2,Honey);TitleArt.Rounded(ui,323,21,8,5,2,Honey);
        TitleArt.Rounded(ui,306,45,28,2,1,Clay);
        logo!.Draw(ui,new RectF(0,0,logo.SourceWidth,logo.SourceHeight),new RectF(186,30,268,67),TitleArt.Ink);
        Leaves(186,65);Leaves(454,65);
        float width=Math.Min(506,heading!.Measure(title,1.08f)+40);
        TitleArt.Rounded(ui,320-width*.5f,84,width,37,18,Warm);
        Head(title,320,86,1.08f,width-24);
    }
    private void End(){ui!.End();GraphicsDevice.Default.EndFrame();}
    private void Text(string text,float center,float y,float scale,float width,Color32 color)
    {
        float measured=font!.Measure(text);if(measured>0)scale=Math.Min(scale,width/measured);
        font.Draw(ui!,text,center-font.Measure(text,scale)*.5f,y,color,scale);
    }
    private void Head(string text,float center,float y,float scale,float width)
    {
        scale=Math.Min(scale,width/heading!.Measure(text));
        heading.Draw(ui!,text,center-heading.Measure(text,scale)*.5f,y,TitleArt.Ink,scale);
    }

    internal void DrawName(string entry,int key,bool exitPrompt=false,bool discardSelected=false,string message="")
    {
        if(keys==null||counts==null)throw new InvalidOperationException("Name surface was not prepared");
        Begin("Welcome, new neighbor");
        portraits![5]!.Draw(ui!,new RectF(32,0,192,190),new RectF(74,128,136,135),Color32.White);
        Rays(203,145,.7f);
        TitleArt.Rounded(ui!,226,136,332,62,22,Warm);
        TitleArt.Rounded(ui!,228,138,328,58,20,TitleArt.Cream);
        using(UiNoise.Scope()){
        ui!.FillTriangle(new Vector2(228,159),new Vector2(215,169),new Vector2(228,179),Warm);
        ui.FillTriangle(new Vector2(229,162),new Vector2(220,169),new Vector2(229,176),TitleArt.Cream);
        }
        TitleArt.Rounded(ui,239,122,103,26,13,TitleArt.Leaf);Text("Hearth",290,124,.88f,90,TitleArt.Cream);
        Text("What should we call you?",393,159,.98f,296,TitleArt.Ink);
        TitleArt.Rounded(ui,226,207,332,44,22,Honey);TitleArt.Rounded(ui,228,209,328,40,20,TitleArt.Cream);
        if(cachedEntry!=entry){cachedEntry=entry;entryScale=Math.Min(1.06f,236/Math.Max(1,font!.Measure(entry)));}
        if(entry.Length>0)font!.Draw(ui,entry,243,219,TitleArt.Ink,entryScale);
        else font!.Draw(ui,"Your name",243,219,new Color32(49,72,62,120),1);
        if(Clock.Timestamp%1000000<650000){float x=243+font.Measure(entry,entryScale)+3;ui.FillRectangle(new RectF(x,218,2,24),Clay);}
        Text(counts[Math.Min(16,entry.Length)],524,223,.63f,49,TitleArt.Leaf);
        if(message.Length>0)Text(message,390,256,.61f,330,Clay);
        for(int i=0;i<40;i++){
            float x=57+i%10*53,y=278+i/10*31;
            if(i==key)TitleArt.Rounded(ui,x-2,y-2,52,29,11,Honey);
            TitleArt.Rounded(ui,x,y,48,25,9,i==key?new Color32(255,216,136):Warm);
            Text(keys[i],x+24,y+2,i==36?.66f:.91f,42,TitleArt.Ink);
        }
        Footer();Button("A","Type",103);Button("X","Erase",233);Button("START","Done",365);Button("B","Back",486);
        if(exitPrompt)DrawExit(discardSelected);
        End();
    }

    internal void DrawLook(string name,int species,bool exitPrompt=false,bool discardSelected=false)
    {
        if((uint)species>=6||portraits?[species]==null)throw new InvalidOperationException("Look surface was not prepared");
        Begin("Find your look");
        Vignette(species,207,188);
        TitleArt.Rounded(ui!,347,139,230,32,16,TitleArt.Leaf);Text(name,462,144,.87f,210,TitleArt.Cream);
        Text("Choose a look that",462,192,.88f,232,TitleArt.Ink);Text("feels like you.",462,216,.88f,232,TitleArt.Ink);
        Divider(264);
        for(int i=0;i<6;i++){
            float x=55+i*89;bool focused=i==species;
            TitleArt.Rounded(ui!,x+2,289,84,105,21,new Color32(225,203,157));
            if(focused){TitleArt.Rounded(ui!,x-3,281,90,116,24,Honey);TitleArt.Rounded(ui!,x,284,84,110,22,TitleArt.Cream);}
            TitleArt.Rounded(ui!,x+3,287,78,104,20,focused?new Color32(255,216,136):Warm);
            portraits![i]!.Draw(ui!,i==2?new RectF(60,0,136,126):new RectF(32,0,192,174),new RectF(x+2,291,80,74),Color32.White);
            TitleArt.Rounded(ui!,x+4,365,76,25,12,TitleArt.Cream);TitleArt.Rounded(ui!,x+6,367,72,21,10,new Color32(107,151,90));
            Text(labels![i],x+42,369,.79f,65,TitleArt.Cream);
            if(focused)Rays(x-2,280,.6f);
        }
        Footer();Button("A","Choose",205);Button("B","Back",374);
        if(exitPrompt)DrawExit(discardSelected);
        End();
    }

    internal void DrawReview(GameState state,string saveError,bool busy)
    {
        if((uint)state.Species>=6||portraits?[state.Species]==null)throw new InvalidOperationException("Review surface was not prepared");
        Begin("Ready to put down roots?");
        Vignette(state.Species,207,190);
        SummaryIcon(347,153,0);Text(state.Name,457,145,1.0f,192,TitleArt.Ink);Rule(335,180,230);
        SummaryIcon(347,197,1);Text(labels![state.Species],457,189,.98f,192,TitleArt.Ink);Rule(335,224,230);
        SummaryIcon(347,241,2);Text("Your new home",457,233,.93f,192,TitleArt.Ink);Rule(335,268,230);
        Text("Your town and neighbors are kept.",320,301,.88f,510,TitleArt.Ink);Divider(331);
        TitleArt.Rounded(ui!,108,350,287,49,24,TitleArt.Leaf);Badge("A",139,374,13,TitleArt.Cream,TitleArt.Leaf);
        Text("Save & begin",269,362,1.04f,222,TitleArt.Cream);
        TitleArt.Rounded(ui!,422,355,131,40,20,Warm);Badge("B",443,375,10,TitleArt.Cream,TitleArt.Leaf);
        font!.Draw(ui!,"Review",461,364,TitleArt.Ink,.91f);
        Text(saveError.Length>0?saveError:"Save your new villager, then enjoy your first day.",320,419,.69f,512,saveError.Length>0?Clay:TitleArt.Ink);
        if(busy){
            ui!.FillRectangle(new RectF(0,0,640,480),new Color32(25,44,36,160));TitleArt.Panel(ui,84,150,472,182);
            Head("Saving your new villager",320,175,1.03f,416);
            Text("Keep the memory card in Slot A.",320,231,.9f,416,TitleArt.Ink);
            Text("Checking your save...",320,275,.76f,416,TitleArt.Ink);
        }
        End();
    }
    private void DrawExit(bool discardSelected)
    {
        ui!.FillRectangle(new RectF(0,0,640,480),new Color32(25,44,36,160));TitleArt.Panel(ui,84,111,472,264);
        Head("Leave this new villager?",320,139,1.02f,416);
        Text("Your new villager has not been saved.",320,194,.79f,416,TitleArt.Ink);
        Text("Your town and other villagers will stay.",320,223,.76f,416,TitleArt.Ink);
        TitleArt.Rounded(ui,121,293,190,42,21,discardSelected?Warm:TitleArt.Leaf);
        TitleArt.Rounded(ui,329,293,190,42,21,discardSelected?Clay:Warm);
        Text("Keep editing",216,303,.91f,170,discardSelected?TitleArt.Ink:TitleArt.Cream);
        Text("Discard",424,303,.91f,170,discardSelected?TitleArt.Cream:TitleArt.Ink);
        Text("A: confirm    B: keep editing",320,352,.66f,416,TitleArt.Ink);
    }
    private void Footer()=>TitleArt.Rounded(ui!,66,414,508,34,17,TitleArt.Leaf);
    private void Button(string key,string caption,float x)
    {
        bool wide=key.Length>1;
        if(wide){TitleArt.Rounded(ui!,x-20,422,40,18,9,TitleArt.Cream);Text(key,x,423,.49f,36,TitleArt.Leaf);}
        else Badge(key,x,431,10,TitleArt.Cream,TitleArt.Leaf);
        font!.Draw(ui!,caption,x+(wide?25:17),421,TitleArt.Cream,.82f);
    }
    private void Badge(string key,float x,float y,float radius,Color32 fill,Color32 ink)
    {TitleArt.Ellipse(ui!,x,y,radius,radius,fill);Text(key,x,y-11,.94f,radius*1.65f,ink);}
    private void Rule(float x,float y,float width)=>TitleArt.Rounded(ui!,x,y,width,1.5f,.75f,new Color32(187,205,163));
    private void Divider(float y)
    {
        Rule(61,y,240);Rule(339,y,240);TitleArt.Rounded(ui!,318,y-1,3,8,1,new Color32(183,130,65));
        TitleArt.Ellipse(ui!,314,y-3,6,3,Sage);TitleArt.Ellipse(ui!,325,y-3,6,3,Sage);
    }
    private void Leaves(float x,float y)
    {
        var leaf=new Color32(120,158,104);TitleArt.Ellipse(ui!,x-5,y-6,8,5,leaf);TitleArt.Ellipse(ui!,x+4,y+3,7,4,Sage);
        ui!.FillTriangle(new Vector2(x-7,y-5),new Vector2(x+11,y+9),new Vector2(x+12,y+7),leaf);TitleArt.Ellipse(ui,x+15,y+5,2,2,Honey);
    }
    private void Vignette(int species,float x,float y)
    {
        TitleArt.Ellipse(ui!,x,y+52,101,16,new Color32(175,202,151));Pine(x-81,y+42,.8f);Pine(x-54,y+35,1.1f);
        float hx=x+55,hy=y+22;
        TitleArt.Rounded(ui!,hx,hy,35,25,2,new Color32(240,201,128));
        ui!.FillTriangle(new Vector2(hx-6,hy+1),new Vector2(hx+17,hy-24),new Vector2(hx+41,hy+1),new Color32(85,141,116));
        ui.FillTriangle(new Vector2(hx+1,hy),new Vector2(hx+17,hy-16),new Vector2(hx+33,hy),new Color32(250,216,153));
        ui.FillRectangle(new RectF(hx+13,hy+9,9,16),new Color32(159,115,65));ui.FillRectangle(new RectF(hx+4,hy+7,6,7),new Color32(255,236,172));
        ui.FillRectangle(new RectF(hx+25,hy+7,6,7),new Color32(255,236,172));
        TitleArt.Rounded(ui,x-61,y+44,34,3,1,new Color32(233,183,101));
        for(int i=0;i<4;i++)TitleArt.Rounded(ui,x-59+i*9,y+37,3,18,1,new Color32(248,205,127));
        TitleArt.Ellipse(ui,x+11,y+61,35,5,new Color32(133,162,111));
        portraits![species]!.Draw(ui,new RectF(0,0,256,256),new RectF(x-62,y-76,146,146),Color32.White);Rays(x+52,y-50,1);
    }
    private void Pine(float x,float y,float scale)
    {
        TitleArt.Rounded(ui!,x-4*scale,y-33*scale,8*scale,33*scale,2,new Color32(183,143,91));
        for(int level=0;level<3;level++){float top=y-(82-level*17)*scale,half=(16+level*4)*scale;
            ui!.FillTriangle(new Vector2(x,top),new Vector2(x-half,top+30*scale),new Vector2(x+half,top+30*scale),level==1?new Color32(133,184,119):new Color32(112,172,111));}
    }
    private void SummaryIcon(float x,float y,int kind)
    {
        if(kind==0){TitleArt.Ellipse(ui!,x,y-5,4,4,TitleArt.Leaf);TitleArt.Rounded(ui!,x-6,y,12,9,5,TitleArt.Leaf);}
        else if(kind==1){TitleArt.Ellipse(ui!,x,y+3,6,5,Clay);for(int i=0;i<3;i++)TitleArt.Ellipse(ui!,x-6+i*6,y-6,2.5f,3,Clay);}
        else{ui!.FillTriangle(new Vector2(x-9,y-2),new Vector2(x,y-10),new Vector2(x+9,y-2),Sage);ui.FillRectangle(new RectF(x-6,y-2,12,12),new Color32(239,203,133));ui.FillRectangle(new RectF(x-2,y+3,4,7),TitleArt.Leaf);}
    }
    private void Rays(float x,float y,float s)
    {
        ui!.FillTriangle(new Vector2(x-8*s,y-4*s),new Vector2(x-11*s,y-3*s),new Vector2(x-15*s,y-13*s),Honey);
        ui.FillTriangle(new Vector2(x-2*s,y-8*s),new Vector2(x+1*s,y-8*s),new Vector2(x,y-20*s),Honey);
        ui.FillTriangle(new Vector2(x+7*s,y-5*s),new Vector2(x+9*s,y-2*s),new Vector2(x+18*s,y-12*s),Honey);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(counted){LiveViews--;counted=false;}
        if(portraits!=null)for(int i=0;i<portraits.Length;i++)if(portraits[i]!=null){TextureCache.Release(portraits[i]!);portraits[i]=null;}
        if(logo!=null)TextureCache.Release(logo);logo=null;
        heading?.Release();heading=null;font?.Release();font=null;
        background?.Dispose();background=null;ui?.Dispose();ui=null;
        portraits=null;files=null;labels=null;keys=null;counts=null;cachedEntry=null;
        TextureCache.TrimUnused();
    }
}
