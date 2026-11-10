using System;
using System.IO;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using DolLauncher=Dolphin.IO.DolLauncher;
using Dolphin.Timing;

// Only UI resources live here. No TownGame, terrain, actor, audio or town save
// is constructed/read until Run returns Start.
internal sealed class TitleScreen : IDisposable
{
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=128});
    private readonly UiFont font=UiFont.LoadSystem();
    private readonly CachedTexture logo=TextureCache.Load("dvd:/wordmark.dndtex",true);
    private readonly Texture2D dots=MakeDots();
    private readonly float[] backgroundQuad=new float[24];
    private readonly Texture2D[] backgroundTextures=new Texture2D[1];
    private readonly MenuNavigation navigation=new MenuNavigation();
    private readonly DevConfig config;
    private readonly int[] rows=new int[DevConfig.Count-2];
    private int selected;private bool dirty;
    private string status=DevConfigStorage.Status;
    private readonly long started=Clock.Timestamp;
    private TitleScreen(DevConfig config){DebugOverlay.TextVisible=false;this.config=config;backgroundTextures[0]=dots;int at=0;for(int i=0;i<DevConfig.Count;i++)if(DevConfig.IsSetting(i))rows[at++]=i;}
    internal static bool Run(DevConfig config)
    {
        using var title=new TitleScreen(config);
        DebugOverlay.WriteLine("ANIMALTOWN TITLE READY (town assets deferred)");
        while(true){
            title.Draw();RuntimeOptions.Wait();Controller.Update();var p=Controller.GetState(0);
            int direction=title.navigation.Read("title",0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
            int count=title.rows.Length+3;
            if(direction==MenuNavigation.Up)title.selected=(title.selected+count-1)%count;
            if(direction==MenuNavigation.Down)title.selected=(title.selected+1)%count;
            if(title.selected>=3&&(p.A.WasPressed||direction==MenuNavigation.Left||direction==MenuNavigation.Right||p.Z.WasPressed)){
                int row=title.rows[title.selected-3];
                if(p.Z.WasPressed&&(row==20||row==23))config.Values[row]=row==20?100:0;
                else config.Adjust(row,direction==MenuNavigation.Left?-1:1);
                title.dirty=true;title.status="Dev config changed - choose Save dev config";DevConfigStorage.ApplyGlobal(config);continue;
            }
            if(!p.A.WasPressed&&!(p.Start.WasPressed&&title.selected==0))continue;
            if(title.selected==2){title.Save();continue;}
            if(title.selected==0){if(title.dirty)title.Save();title.status="Loading your town...";title.Draw();return true;}
            if(title.selected==1){
                if(title.dirty)title.Save();
                try{
                    title.status="Starting updater...";title.Draw();
                    // Prefer the updater shipped with this game; an SD copy also
                    // permits manual deployment without rebuilding the ISO.
                    string path="dvd:/animal-town-updater.dol";
                    if(!System.IO.File.Exists(path)){
                        if(!Dolphin.Storage.Storage.Mount(Dolphin.Storage.StorageDevice.GcLoader))throw new IOException("GC Loader unavailable");
                        path="gcloader:/animal-town-updater.dol";
                    }
                    using var updater=DolLauncher.Prepare(path);
                    updater.SetArguments("gcloader:/animal-town-updater.dol",BuildInfo.Version,"gcloader:/AnimalTown.iso");
                    DolLauncher.RequestLaunch(updater);return false;
                }catch(Exception){title.status="Updater unavailable. Install animal-town-updater.dol on GC Loader.";}
            }
        }
    }
    private void Save(){if(DevConfigStorage.Save(config))dirty=false;status=DevConfigStorage.Status;}
    private void Draw()
    {
        var ink=new Color32(49,72,62);device.BeginFrame(new Color32(149,198,186));ui.Begin();
        float scroll=(float)((Clock.Timestamp-started)/1000000.0*4%64);
        float uv=-scroll/64;
        BackgroundVertex(0,0,0,uv,uv);BackgroundVertex(4,640,0,uv+10,uv);BackgroundVertex(8,640,480,uv+10,uv+7.5f);
        BackgroundVertex(12,0,0,uv,uv);BackgroundVertex(16,640,480,uv+10,uv+7.5f);BackgroundVertex(20,0,480,uv,uv+7.5f);
        ui.DrawTexturedQuads(backgroundTextures,backgroundQuad,1,Color32.White);
        logo.Draw(ui,new RectF(0,0,logo.SourceWidth,logo.SourceHeight),new RectF(90,25,460,115),ink);
        Row(0,"Start",160,1.2f);Row(1,"Update",192,1.2f);Row(2,"Save dev config"+(dirty?" *":""),224,1);
        font.Draw(ui,"Debug options",62,268,ink,.85f);
        int first=selected<3?0:(selected-3)/6*6;
        for(int i=first;i<Math.Min(rows.Length,first+6);i++)Row(i+3,Label(rows[i]),296+(i-first)*20,.75f);
        font.Draw(ui,(first+1)+"-"+Math.Min(rows.Length,first+6)+" / "+rows.Length,514,269,ink,.65f);
        font.Draw(ui,"D-pad / stick: choose    A / Left / Right: change    Z: reset slider",42,432,ink,.65f);
        font.Draw(ui,status,42,454,ink,.65f);ui.End();device.EndFrame();
    }
    private void BackgroundVertex(int at,float x,float y,float u,float v){backgroundQuad[at]=x;backgroundQuad[at+1]=y;backgroundQuad[at+2]=u;backgroundQuad[at+3]=v;}
    private void Row(int index,string text,float y,float scale)
    {
        if(index==selected)ui.FillRectangle(new RectF(48,y-2,544,scale>1?29:23),new Color32(255,245,221,220));
        font.Draw(ui,(selected==index?"> ":"  ")+text,62,y,new Color32(49,72,62),scale);
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
    private static Texture2D MakeDots()
    {
        var pixels=new byte[64*64*4];for(int y=0;y<64;y++)for(int x=0;x<64;x++){
            int dx=Math.Min(x,64-x),dy=Math.Min(y,64-y),bx=x-32,by=y-32;
            bool dot=dx*dx+dy*dy<=9||bx*bx+by*by<=9;int at=(y*64+x)*4;
            pixels[at]=(byte)(dot?177:149);pixels[at+1]=(byte)(dot?216:198);pixels[at+2]=(byte)(dot?205:186);pixels[at+3]=255;
        }
        var texture=Texture2D.Create(64,64,pixels);texture.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,TextureMinFilter.Linear,TextureMagFilter.Linear);return texture;
    }
    public void Dispose(){dots.Dispose();TextureCache.Release(logo);font.Release();ui.Dispose();TextureCache.TrimUnused();}
}
