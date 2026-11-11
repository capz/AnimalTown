using System;
using System.IO;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using DolLauncher=Dolphin.IO.DolLauncher;
using Dolphin.Timing;

// The title owns only UI resources. TownGame is constructed only after Start.
internal sealed class TitleScreen : IDisposable
{
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=128});
    private readonly UiFont font=UiFont.LoadSystem();
    private readonly CachedTexture logo=TextureCache.Load("dvd:/wordmark.dndtex",true);
    private readonly TitleBackdrop background=new TitleBackdrop();
    private readonly MenuNavigation navigation=new MenuNavigation();
    private readonly DevConfig config;
    private readonly int[] rows=new int[DevConfig.Count-2];
    private int selected,debugSelected;
    private bool debugOpen,dirty;
    private string status="";
    private TitleScreen(DevConfig config)
    {this.config=config;int at=0;for(int i=0;i<DevConfig.Count;i++)if(DevConfig.IsSetting(i))rows[at++]=i;}
    internal static bool Run(DevConfig config)
    {
        using var title=new TitleScreen(config);
        DebugOverlay.WriteLine("ANIMALTOWN TITLE READY (town assets deferred)");
        while(true){
            title.Draw();RuntimeOptions.Wait();Controller.Update();var p=Controller.GetState(0);
            int direction=title.navigation.Read(title.debugOpen?"dev-config":"title",0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
            if(title.debugOpen){
                if(p.B.WasPressed||p.Start.WasPressed){title.Save();title.debugOpen=false;continue;}
                if(direction==MenuNavigation.Up)title.debugSelected=(title.debugSelected+title.rows.Length-1)%title.rows.Length;
                if(direction==MenuNavigation.Down)title.debugSelected=(title.debugSelected+1)%title.rows.Length;
                if(p.A.WasPressed||direction==MenuNavigation.Left||direction==MenuNavigation.Right||p.Z.WasPressed){
                    int row=title.rows[title.debugSelected];
                    if(p.Z.WasPressed&&(row==20||row==23))config.Values[row]=row==20?100:0;
                    else if(!p.Z.WasPressed)config.Adjust(row,direction==MenuNavigation.Left?-1:1);
                    else continue;
                    title.dirty=true;DevConfigStorage.ApplyGlobal(config);
                }
                continue;
            }
            if(direction==MenuNavigation.Up)title.selected=(title.selected+2)%3;
            if(direction==MenuNavigation.Down)title.selected=(title.selected+1)%3;
            if(!p.A.WasPressed&&!(p.Start.WasPressed&&title.selected==0))continue;
            if(title.selected==2){title.debugOpen=true;continue;}
            if(title.selected==0){
                DevConfigStorage.ApplyGlobal(config);LoadingScreen.Draw();
                title.Save();return true;
            }
            title.Save();
            try{
                title.status="Starting updater...";title.Draw();
                string path="dvd:/animal-town-updater.dol";
                if(!System.IO.File.Exists(path)){
                    if(!Dolphin.Storage.Storage.Mount(Dolphin.Storage.StorageDevice.GcLoader))throw new IOException("GC Loader unavailable");
                    path="gcloader:/animal-town-updater.dol";
                }
                using var updater=DolLauncher.Prepare(path);
                updater.SetArguments("gcloader:/animal-town-updater.dol",BuildInfo.Version,"gcloader:/AnimalTown.iso");
                DolLauncher.RequestLaunch(updater);return false;
            }catch(Exception){title.status="Updater unavailable. Check the game disc or GC Loader.";}
        }
    }
    private void Save()
    {
        if(!dirty)return;
        if(DevConfigStorage.Save(config))dirty=false;
        status=DevConfigStorage.Status;
    }
    private void Draw()
    {
        DebugOverlay.TextVisible=false;DebugOverlay.PerformanceVisible=false;
        device.BeginFrame(TitleBackdrop.Color);ui.Begin();background.Draw(ui);
        TitleArt.Panel(ui,70,52,500,392);TitleArt.BrandMark(ui,320,105);
        logo.Draw(ui,new RectF(0,0,logo.SourceWidth,logo.SourceHeight),new RectF(90,128,460,115),TitleArt.Ink);
        const string tagline="A little life, well rooted.";
        font.Draw(ui,tagline,(640-font.Measure(tagline))/2,236,TitleArt.Ink);
        MenuRow(0,"Start",274);MenuRow(1,"Update",326);MenuRow(2,"Debug configuration",378);
        if(debugOpen)DrawDebug();
        else if(status.Length>0)font.Draw(ui,status,(640-font.Measure(status,.65f))/2,455,TitleArt.Ink,.65f);
        ui.End();device.EndFrame();
    }
    private void MenuRow(int index,string label,float y)
    {
        bool focus=index==selected;
        TitleArt.Rounded(ui,167,y,306,44,22,focus?TitleArt.Leaf:new Color32(236,226,197));
        font.Draw(ui,label,(640-font.Measure(label,1.05f))/2,y+10,focus?TitleArt.Cream:TitleArt.Ink,1.05f);
    }
    private void DrawDebug()
    {
        ui.FillRectangle(new RectF(0,0,640,480),new Color32(25,44,36,150));
        TitleArt.Panel(ui,66,44,508,392);
        font.Draw(ui,"Debug configuration",90,65,TitleArt.Ink,1.2f);
        int first=debugSelected/8*8;
        for(int i=first;i<Math.Min(rows.Length,first+8);i++){
            float y=112+(i-first)*29;
            if(i==debugSelected)TitleArt.Rounded(ui,83,y-2,474,27,13,new Color32(221,231,188));
            font.Draw(ui,Label(rows[i]),96,y,TitleArt.Ink,.8f);
        }
        string page=(first+1)+"-"+Math.Min(rows.Length,first+8)+" / "+rows.Length;
        font.Draw(ui,page,518-font.Measure(page,.65f),88,TitleArt.Ink,.65f);
        font.Draw(ui,"A / Left / Right: change    Z: reset slider",90,359,TitleArt.Ink,.7f);
        font.Draw(ui,"B: save and close",90,386,TitleArt.Ink,.85f);
        font.Draw(ui,dirty?"Changes will be saved to dev config.":DevConfigStorage.Status,90,412,TitleArt.Ink,.65f);
    }
    private string Label(int row)
    {
        int value=config.Values[row];string toggle=value==0?"Off":"On";
        switch(row){
            case 0:return "Performance display: "+toggle;
            case 1:return "Model drawing: "+(value==1?"GX indexed":"Direct comparison");
            case 2:return "UV island colors: "+toggle;case 3:return "UV island outlines: "+toggle;
            case 4:return "Batched drawing: "+toggle;case 5:return "Batched text: "+toggle;
            case 6:return "Actor rim lighting: "+toggle;case 7:return "Object / actor shadows: "+toggle;
            case 8:return "Cloud cover: "+(value==0?"Clear":value==1?"Overcast":"Partial");
            case 9:return "Cliff preview: "+toggle;case 10:return "Water inspection: "+value+" / 5";
            case 11:return "Frame pacing: "+(value==0?"Yield":"Uncapped");case 12:return "Productive waits: "+toggle;case 13:return "Reuse frame clear: "+toggle;
            case 16:return "Rain preview: "+toggle;case 17:return "Rough ocean in rain: "+toggle;
            case 18:return "Season preview: "+(value==0?"Auto":TerrainTiles.SeasonName(value-1));
            case 19:return "Villager attention bubbles: "+toggle;case 20:return "Rim intensity: "+value+"%";
            case 21:return "SDK performance overlay: "+toggle;case 22:return "Ground mipmaps: "+toggle;
            case 23:return "Ground mip distance: "+(value==0?"Auto":value+" squares");
            case 24:return "Freeze landscape motion: "+toggle;case 25:return "Recreate terrain meshes: "+toggle;
            default:return "Legacy ground ring: "+toggle;
        }
    }
    public void Dispose(){background.Dispose();TextureCache.Release(logo);font.Release();ui.Dispose();TextureCache.TrimUnused();}
}
