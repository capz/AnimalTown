using System;
using AnimalTown;
using Dolphin.Graphics;

// Portraits are baked from the game's original models. No town, skeleton,
// animation or offscreen render target is loaded to choose a resident.
internal sealed class ProfilePickerUi : IDisposable
{
    private static readonly string[] Species={"fox","bunny","giraffe","cat","hamster","spaniel"};
    private static readonly Color32 Honey=new Color32(255,183,64),Warm=new Color32(245,226,186),Sage=new Color32(158,181,136);
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=128});
    private readonly TitleBackdrop background=new TitleBackdrop();
    private readonly CachedTexture?[] portraits=new CachedTexture?[6];
    private readonly string[] names=new string[4];
    private readonly string[] labels=new string[4];
    private readonly int[] species=new int[4];
    private UiFont? font,heading;
    private CachedTexture? logo;
    internal int Selected;

    internal ProfilePickerUi(GameState state,bool savedTown)
    {
        try{
            font=UiFont.LoadSystem();heading=UiFont.Load("gorditas-bold");logo=TextureCache.Load("dvd:/wordmark.dndtex",true);
            Selected=state.ActiveProfile;
            for(int i=0;i<GameState.ProfileCount;i++){
                bool occupied=state.Profiles[i].Created&&(savedTown||i!=0);
                names[i]=occupied?state.ProfileName(i):"New Villager";
                labels[i]=CardLabel(names[i]);
                species[i]=occupied?(i==state.ActiveProfile?state.Species:state.Profiles[i].Species):-1;
                if(species[i]>=0&&portraits[species[i]]==null)
                    portraits[species[i]]=TextureCache.Load("dvd:/picker-"+Species[species[i]]+".dndtex",true);
            }
        }catch{Dispose();throw;}
    }
    private string CardLabel(string name)
    {
        // Keep even unusually wide 16-character names legible. The selected
        // resident's full name appears beside the vignette when abbreviated.
        if(font!.Measure(name,.60f)<=80)return name;
        int length=name.Length;
        while(length>0&&font.Measure(name.Substring(0,length)+"...",.60f)>80)length--;
        return name.Substring(0,length)+"...";
    }
    private void Center(string text,float x,float y,float scale,float width)
    {
        scale=Math.Min(scale,width/font!.Measure(text));
        font.Draw(ui,text,x-font.Measure(text,scale)*.5f,y,TitleArt.Ink,scale);
    }
    internal void Draw()
    {
        var device=GraphicsDevice.Default;device.BeginFrame(TitleBackdrop.Color);ui.Begin();background.Draw(ui);
        TitleArt.Panel(ui,36,18,568,440);
        TitleArt.Rounded(ui,312,27,16,17,8,TitleArt.Leaf);TitleArt.Rounded(ui,317,34,6,11,3,TitleArt.Cream);
        TitleArt.Rounded(ui,309,21,8,5,2,Honey);TitleArt.Rounded(ui,323,21,8,5,2,Honey);
        TitleArt.Rounded(ui,306,45,28,2,1,new Color32(195,109,84));
        logo!.Draw(ui,new RectF(0,0,logo.SourceWidth,logo.SourceHeight),new RectF(186,30,268,67),TitleArt.Ink);
        Leaves(186,65);Leaves(454,65);
        TitleArt.Rounded(ui,174,84,292,37,18,Warm);
        const string welcome="Welcome back!";
        heading!.Draw(ui,welcome,320-heading.Measure(welcome,1.18f)*.5f,86,TitleArt.Ink,1.18f);
        for(int i=0;i<3;i++){TitleArt.Ellipse(ui,146+i*9,102,2,2,Honey);TitleArt.Ellipse(ui,476+i*9,102,2,2,Honey);}
        Vignette();
        Center("Who is visiting today?",440,154,1,248);
        Center("Choose your villager to continue",440,192,.76f,248);
        Center("playing where you left off.",440,212,.76f,248);
        if(labels[Selected]!=names[Selected])Center(names[Selected],440,238,.82f,248);
        TitleArt.Rounded(ui,61,264,240,2,1,new Color32(187,205,163));TitleArt.Rounded(ui,339,264,240,2,1,new Color32(187,205,163));
        TitleArt.Rounded(ui,318,263,3,8,1,new Color32(183,130,65));
        TitleArt.Ellipse(ui,314,261,6,3,Sage);TitleArt.Ellipse(ui,325,261,6,3,Sage);
        for(int i=0;i<4;i++)Card(i);
        TitleArt.Rounded(ui,66,411,508,35,17,TitleArt.Leaf);Button("A","Select",205);Button("B","Back",374);
        ui.End();device.EndFrame();
    }
    private void Leaves(float x,float y)
    {
        var leaf=new Color32(120,158,104);
        TitleArt.Ellipse(ui,x-5,y-6,8,5,leaf);TitleArt.Ellipse(ui,x+4,y+3,7,4,Sage);
        ui.FillTriangle(new Vector2(x-7,y-5),new Vector2(x+11,y+9),new Vector2(x+12,y+7),leaf);
        TitleArt.Ellipse(ui,x+15,y+5,2,2,Honey);
    }
    private void Vignette()
    {
        TitleArt.Ellipse(ui,207,240,101,16,new Color32(175,202,151));Pine(126,230,.8f);Pine(153,223,1.1f);
        // Tiny original cottage/fence, not loaded world/building resources.
        TitleArt.Rounded(ui,262,210,35,25,2,new Color32(240,201,128));
        ui.FillTriangle(new Vector2(256,211),new Vector2(279,186),new Vector2(303,211),new Color32(85,141,116));
        ui.FillTriangle(new Vector2(263,210),new Vector2(279,194),new Vector2(295,210),new Color32(250,216,153));
        ui.FillRectangle(new RectF(275,219,9,16),new Color32(159,115,65));
        ui.FillRectangle(new RectF(266,217,6,7),new Color32(255,236,172));ui.FillRectangle(new RectF(287,217,6,7),new Color32(255,236,172));
        TitleArt.Rounded(ui,146,232,34,3,1,new Color32(233,183,101));
        for(int i=0;i<4;i++)TitleArt.Rounded(ui,148+i*9,225,3,18,1,new Color32(248,205,127));
        TitleArt.Ellipse(ui,218,249,35,5,new Color32(133,162,111));
        int kind=species[Selected];
        if(kind<0)Silhouette(218,152,1.08f);
        else portraits[kind]!.Draw(ui,new RectF(0,0,256,256),new RectF(145,112,146,146),Color32.White);
        Rays(259,138,1);
    }
    private void Pine(float x,float y,float scale)
    {
        TitleArt.Rounded(ui,x-4*scale,y-33*scale,8*scale,33*scale,2,new Color32(183,143,91));
        for(int level=0;level<3;level++){
            float top=y-(82-level*17)*scale,half=(16+level*4)*scale;
            ui.FillTriangle(new Vector2(x,top),new Vector2(x-half,top+30*scale),new Vector2(x+half,top+30*scale),level==1?new Color32(133,184,119):new Color32(112,172,111));
        }
    }
    private void Card(int index)
    {
        float x=86+index*122;bool focus=index==Selected;
        TitleArt.Rounded(ui,x+2,286,108,109,25,new Color32(225,203,157));
        if(focus){TitleArt.Rounded(ui,x-3,280,114,116,27,Honey);TitleArt.Rounded(ui,x,283,108,110,24,TitleArt.Cream);}
        TitleArt.Rounded(ui,x+3,286,102,104,22,focus?new Color32(255,216,136):Warm);
        int kind=species[index];
        if(kind<0)Silhouette(x+54,299,.73f);
        else portraits[kind]!.Draw(ui,kind==2?new RectF(60,0,136,126):new RectF(32,0,192,174),new RectF(x+7,289,94,85),Color32.White);
        TitleArt.Rounded(ui,x+8,362,92,27,13,TitleArt.Cream);TitleArt.Rounded(ui,x+10,364,88,23,11,new Color32(107,151,90));
        float scale=Math.Min(.94f,80/font!.Measure(labels[index]));
        font.Draw(ui,labels[index],x+54-font.Measure(labels[index],scale)*.5f,365+(20-22*scale)*.5f,TitleArt.Cream,scale);
        if(focus)Rays(x-3,281,.7f);
    }
    private void Silhouette(float x,float y,float scale)
    {
        TitleArt.Ellipse(ui,x,y+62*scale,24*scale,26*scale,Sage);TitleArt.Ellipse(ui,x,y+25*scale,25*scale,24*scale,Sage);
        ui.FillTriangle(new Vector2(x-24*scale,y+21*scale),new Vector2(x-23*scale,y-3*scale),new Vector2(x-6*scale,y+9*scale),Sage);
        ui.FillTriangle(new Vector2(x+24*scale,y+21*scale),new Vector2(x+23*scale,y-3*scale),new Vector2(x+6*scale,y+9*scale),Sage);
        TitleArt.Ellipse(ui,x,y+38*scale,13*scale,13*scale,TitleArt.Cream);
        TitleArt.Rounded(ui,x-8*scale,y+35*scale,16*scale,5*scale,2*scale,Sage);TitleArt.Rounded(ui,x-2.5f*scale,y+30*scale,5*scale,16*scale,2*scale,Sage);
    }
    private void Rays(float x,float y,float scale)
    {
        for(int i=0;i<3;i++){
            float a=(-115+i*32)*.017453293f,dx=(float)Math.Cos(a),dy=(float)Math.Sin(a),cx=x+dx*13*scale,cy=y+dy*13*scale;
            ui.FillTriangle(new Vector2(cx-dy*2*scale,cy+dx*2*scale),new Vector2(cx+dy*2*scale,cy-dx*2*scale),new Vector2(cx+dx*10*scale,cy+dy*10*scale),Honey);
        }
    }
    private void Button(string key,string text,float x)
    {
        TitleArt.Ellipse(ui,x,428,11,11,TitleArt.Cream);font!.Draw(ui,key,x-font.Measure(key,.96f)*.5f,417,TitleArt.Leaf,.96f);
        font.Draw(ui,text,x+20,418,TitleArt.Cream,1);
    }
    public void Dispose()
    {
        for(int i=0;i<portraits.Length;i++)if(portraits[i]!=null)TextureCache.Release(portraits[i]!);
        if(logo!=null)TextureCache.Release(logo);heading?.Release();font?.Release();background.Dispose();ui.Dispose();TextureCache.TrimUnused();
    }
}
