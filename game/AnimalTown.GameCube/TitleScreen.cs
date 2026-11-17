using System;
using System.IO;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

// The title owns only UI resources. TownGame is constructed only after Start.
internal sealed class TitleScreen : IDisposable,Hypercube.GameCube.IFrameScene
{
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{VertexCapacity=8192,SpriteCapacity=128});
    private readonly Camera2D composition=new Camera2D();
    private float canvasMargin;
    private readonly UiFont font=UiFont.LoadSystem();
    private readonly CachedTexture logo=TextureCache.Load(AnimalTown.ContentPaths.Asset("wordmark.dndtex"),true);
    private readonly TitleBackdrop background=new TitleBackdrop();
    private readonly MenuNavigation navigation=new MenuNavigation();
    private readonly DevConfig config;
    private readonly UserOptions options;
    private UiFont? optionsHeading;
    private readonly int[] rows=new int[DevConfig.SettingCount+1];
    private int selected,debugSelected,optionRow,optionChoice;
    private bool debugOpen,dirty,optionsOpen,confirmDelete,deleteFocus,deleting;
    private string optionsStatus="";
    private string status="";
    private const float BodyScale=1.2f,HelpScale=1;
    private const int TitleColumnX=140,TitleColumnWidth=360,TitleRowHeight=34,TitleRowStep=42;
    private const int DebugPageSize=6;
    private int MenuCount=>options.ShowDebugMenus?4:3;
    private int lastTitleLayout=-1;
    private string statusSource="",statusLine1="",statusLine2="";
    private float statusWidth;
    private TitleScreen(DevConfig config,UserOptions options)
    {
        this.config=config;this.options=options;status=VideoOptions.Status;
        int at=0;rows[at++]=0;rows[at++]=DevConfig.FullResolutionAaIndex;
        for(int i=1;i<DevConfig.Count;i++)if(DevConfig.IsSetting(i)&&i!=DevConfig.FullResolutionAaIndex&&i!=DevConfig.GroundMipBandsIndex){
            rows[at++]=i;if(i==23)rows[at++]=DevConfig.GroundMipBandsIndex;
        }
        rows[at]=DevConfig.Count;
    }
    internal static bool Run(DevConfig config,UserOptions options)
    {
        using var title=new TitleScreen(config,options);
        DebugOverlay.WriteLine("ANIMALTOWN TITLE READY (town assets deferred)");
        DebugOverlay.WriteLine("ANIMALTOWN TITLE LOGO "+title.logo.SourceWidth+"x"+title.logo.SourceHeight+" bytes "+title.logo.Bytes);
        while(true){
            title.Draw();RuntimeOptions.Wait();Controller.Update();var p=Controller.GetState(0);
            if(DebugMenuSettings.Read(p)){
                if(!options.ShowDebugMenus){title.Save();title.debugOpen=false;title.selected=Math.Min(title.selected,title.MenuCount-1);}
                title.status=UserOptionsStorage.Status;continue;
            }
            int direction=title.navigation.Read(title.confirmDelete?"delete-town":title.optionsOpen?"title-options":title.debugOpen?"dev-config":"title",0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
            if(title.optionsOpen){title.OptionsInput(direction,p.A.WasPressed,p.B.WasPressed);continue;}
            if(title.debugOpen){
                if(p.B.WasPressed||p.Start.WasPressed){title.Save();title.debugOpen=false;continue;}
                if(direction==MenuNavigation.Up)title.debugSelected=(title.debugSelected+title.rows.Length-1)%title.rows.Length;
                if(direction==MenuNavigation.Down)title.debugSelected=(title.debugSelected+1)%title.rows.Length;
                if(p.A.WasPressed||direction==MenuNavigation.Left||direction==MenuNavigation.Right||p.X.WasPressed){
                    int row=title.rows[title.debugSelected];
                    if(row==DevConfig.Count){if(p.X.WasPressed)UiNoiseSettings.Reset();else UiNoiseSettings.Adjust(direction==MenuNavigation.Left?-1:1);continue;}
                    if(row==DevConfig.FullResolutionAaIndex){
                        if(!p.X.WasPressed&&VideoOptions.ToggleFullResolutionAa(config)){title.dirty=true;title.Save();}
                        title.status=VideoOptions.Status;continue;
                    }
                    if(p.X.WasPressed&&DevConfig.IsSlider(row))config.Reset(row);
                    else if(!p.X.WasPressed)config.Adjust(row,direction==MenuNavigation.Left?-1:1);
                    else continue;
                    title.dirty=true;DevConfigStorage.ApplyGlobal(config);
                }
                continue;
            }
            if(direction==MenuNavigation.Up)title.selected=(title.selected+title.MenuCount-1)%title.MenuCount;
            if(direction==MenuNavigation.Down)title.selected=(title.selected+1)%title.MenuCount;
            if(!p.A.WasPressed&&!(p.Start.WasPressed&&title.selected==0))continue;
            if(title.selected==3&&options.ShowDebugMenus){title.debugOpen=true;continue;}
            if(title.selected==2){
                title.optionsHeading=UiFont.Load("gorditas-bold");title.optionsOpen=true;title.optionRow=0;title.ResetOptionChoice();
                title.optionsStatus=VideoOptions.Status.Length>0?VideoOptions.Status:UserOptionsStorage.Status;continue;
            }
            if(title.selected==0){
                if(TownDataStorage.Pending()){title.status="Finish town deletion in Options before starting.";continue;}
                DevConfigStorage.ApplyGlobal(config);LoadingScreen.Draw();
                title.Save();return true;
            }
            title.Save();
            try{
                title.status="Starting updater...";title.Draw();
                UpdaterLaunch.Request();return false;
            }catch(Exception error){
                title.status=error.Message??"";
                if(title.status.Length==0)title.status="Update failed: "+UpdaterLaunch.Operation+".";
            }
        }
    }
    private void Save()
    {
        if(!dirty)return;
        if(DevConfigStorage.Save(config)){dirty=false;VideoOptions.AaSaveFailed=false;}else VideoOptions.AaSaveFailed=true;
        status=DevConfigStorage.Status;
    }
    private void Draw()
    {
        UiNoise.Prepare();background.Prepare();Hypercube.GameCube.FrameRenderer.Draw(GraphicsDevice.Default,TitleBackdrop.Color,this);
    }
    public void SubmitFramePart()
    {
        DebugOverlay.TextVisible=false;DebugOverlay.PerformanceVisible=false;
        ui.Begin();background.Draw(ui);ui.End();
        // Reuse one batch for the full backdrop and centered composition. Scale
        // height as Fit would, while retaining the full display for modal shade.
        var size=ui.LogicalSize;composition.Zoom=size.Y/480;canvasMargin=(size.X/composition.Zoom-640)*.5f;
        composition.Position=new Vector2(-canvasMargin,0);ui.Begin(composition);
        // Fit the approved logo and menu column with 36px side padding and
        // 24-25px above/below. Status messages retain their own bounded footer.
        bool showStatus=status.Length>0&&!optionsOpen&&!debugOpen;
        int panelHeight=showStatus?456:MenuCount==3?384:424,panelY=(480-panelHeight)/2;
        int contentOffset=(4-MenuCount)*21+(showStatus?0:24),logoY=28+contentOffset,menuY=244+contentOffset;
        TitleArt.Panel(ui,104,panelY,432,panelHeight);
        TitleArt.Logo(ui,logo,new RectF(TitleColumnX,logoY,TitleColumnWidth,180));
        const string tagline="A little life, well rooted.";
        CenteredText(tagline,new RectF(TitleColumnX,212+contentOffset,TitleColumnWidth,26),TitleArt.Ink,1.1f);
        MenuRow(0,"Start",menuY);MenuRow(1,"Update",menuY+TitleRowStep);MenuRow(2,"Options",menuY+TitleRowStep*2);
        if(options.ShowDebugMenus)MenuRow(3,"Debug configuration",menuY+TitleRowStep*3);
        int layout=MenuCount+(showStatus?8:0);
        if(!optionsOpen&&!debugOpen&&layout!=lastTitleLayout){
            lastTitleLayout=layout;
            DebugOverlay.WriteLine("ANIMALTOWN TITLE LAYOUT rows "+MenuCount+" status "+(showStatus?1:0)+" panel 104,"+panelY+",432,"+panelHeight+" logo 140,"+logoY+",360,180 menu 140,"+menuY+",360,34 step 42");
        }
        if(optionsOpen)DrawOptions();
        else if(debugOpen)DrawDebug();
        else if(showStatus)DrawStatus(status,TitleColumnX,410,TitleColumnWidth,true);
        ui.End();
    }
    private RectF ScreenBounds()=>new RectF(-canvasMargin,0,640+2*canvasMargin,480);
    private void MenuRow(int index,string label,float y)
    {
        bool focus=index==selected;
        TitleArt.Rounded(ui,TitleColumnX,y,TitleColumnWidth,TitleRowHeight,17,focus?TitleArt.Leaf:new Color32(236,226,197));
        CenteredText(label,new RectF(TitleColumnX,y,TitleColumnWidth,TitleRowHeight),focus?TitleArt.Cream:TitleArt.Ink,BodyScale);
    }
    private void CenteredText(string text,RectF bounds,Color32 color,float scale)
    {
        var ink=font.GlyphBounds(text,scale);
        font.Draw(ui,text,bounds.X+(bounds.Width-ink.Width)*.5f-ink.X,
            bounds.Y+(bounds.Height-ink.Height)*.5f-ink.Y,color,scale);
    }
    private void ResetOptionChoice()
    {optionChoice=optionRow==0?Math.Max(0,VideoOptions.ActiveResolution-1):device.Display.Widescreen?0:1;}
    private void OptionsInput(int direction,bool activate,bool back)
    {
        if(confirmDelete){
            if(back){confirmDelete=false;return;}
            if(direction==MenuNavigation.Left||direction==MenuNavigation.Up)deleteFocus=false;
            if(direction==MenuNavigation.Right||direction==MenuNavigation.Down)deleteFocus=true;
            if(!activate)return;
            if(deleteFocus){
                deleting=true;Draw();
                TownDataStorage.Delete();optionsStatus=TownDataStorage.Status;deleting=false;
            }
            confirmDelete=false;return;
        }
        if(back){optionsOpen=false;optionsHeading?.Release();optionsHeading=null;TextureCache.TrimUnused();return;}
        if(direction==MenuNavigation.Up||direction==MenuNavigation.Down){
            optionRow=(optionRow+(direction==MenuNavigation.Up?2:1))%3;ResetOptionChoice();
        }
        if(optionRow<2&&(direction==MenuNavigation.Left||direction==MenuNavigation.Right))optionChoice=1-optionChoice;
        if(!activate)return;
        if(optionRow==2){confirmDelete=true;deleteFocus=false;return;}
        int resolution=optionRow==0?optionChoice+1:options.Resolution;
        bool wide=optionRow==1?optionChoice==0:device.Display.Widescreen;
        if(!VideoOptions.Apply(resolution,wide,optionRow==0)){optionsStatus=VideoOptions.Status;return;}
        options.Resolution=resolution;options.Widescreen=wide;
        // Present before physical-card I/O; mode switching holds output black
        // until the first completed frame in the replacement mode.
        optionsStatus="Saving to memory card in Slot A...";Draw();
        UserOptionsStorage.Save(options);optionsStatus=UserOptionsStorage.Status;
    }
    private void DrawOptions()
    {
        ui.FillRectangle(ScreenBounds(),new Color32(25,44,36,155));
        TitleArt.Panel(ui,44,24,552,432);
        optionsHeading!.Draw(ui,"Options",74,40,TitleArt.Ink,1.4f);
        font.Draw(ui,"Video",76,89,TitleArt.Leaf,1.25f);
        OptionRow(0,"Resolution","480p","576p",127,VideoOptions.ActiveResolution-1);
        OptionRow(1,"Widescreen","Yes","No",175,device.Display.Widescreen?0:1);
        font.Draw(ui,"Language",76,225,TitleArt.Leaf,1.25f);
        font.Draw(ui,"Coming soon",82,260,new Color32(128,135,112),BodyScale);
        font.Draw(ui,"Data",76,300,TitleArt.Leaf,1.25f);
        TitleArt.Rounded(ui,62,331,516,44,19,optionRow==2?new Color32(232,202,168):new Color32(236,226,197));
        font.Draw(ui,"Delete town data",82,338,TitleArt.Ink,BodyScale);
        DrawStatus(optionsStatus,74,383,492);
        font.Draw(ui,"Left/Right: select   A: apply   B: back",74,431,TitleArt.Ink,HelpScale);
        if(confirmDelete)DrawDeleteConfirmation();
    }
    private void OptionRow(int row,string label,string first,string second,float y,int active)
    {
        if(optionRow==row)TitleArt.Rounded(ui,62,y-7,516,44,19,new Color32(221,231,188));
        font.Draw(ui,label,82,y,TitleArt.Ink,BodyScale);
        float secondWidth=font.Measure(second,BodyScale),firstWidth=font.Measure(first,BodyScale);
        float secondX=558-secondWidth,firstX=secondX-30-firstWidth;
        OptionValue(row,0,first,firstX,y,firstWidth,active);
        OptionValue(row,1,second,secondX,y,secondWidth,active);
    }
    private void OptionValue(int row,int value,string label,float x,float y,float width,int active)
    {
        if(optionRow==row&&optionChoice==value)TitleArt.Rounded(ui,x-8,y-5,width+16,35,12,new Color32(244,238,204));
        font.Draw(ui,label,x,y,TitleArt.Ink,BodyScale);
        if(active==value)ui.FillRectangle(new RectF(x,y+28,width,3),TitleArt.Leaf);
    }
    private void DrawDeleteConfirmation()
    {
        ui.FillRectangle(ScreenBounds(),new Color32(25,44,36,165));
        TitleArt.Panel(ui,44,108,552,268);
        optionsHeading!.Draw(ui,deleting?"Deleting town...":"Delete town data?",76,132,TitleArt.Ink,1.25f);
        font.Draw(ui,"Erase this town and ALL villagers\nfrom the memory card in Slot A?\nThis cannot be undone.",76,184,TitleArt.Ink,1.15f);
        if(deleting)return;
        TitleArt.Rounded(ui,76,298,216,48,22,!deleteFocus?TitleArt.Leaf:new Color32(236,226,197));
        TitleArt.Rounded(ui,348,298,216,48,22,deleteFocus?new Color32(162,69,55):new Color32(236,226,197));
        font.Draw(ui,"Cancel",184-font.Measure("Cancel",BodyScale)/2,307,!deleteFocus?TitleArt.Cream:TitleArt.Ink,BodyScale);
        font.Draw(ui,"Delete",456-font.Measure("Delete",BodyScale)/2,307,deleteFocus?TitleArt.Cream:TitleArt.Ink,BodyScale);
    }
    private void DrawDebug()
    {
        ui.FillRectangle(ScreenBounds(),new Color32(25,44,36,150));
        TitleArt.Panel(ui,44,24,552,432);
        font.Draw(ui,"Debug configuration",74,45,TitleArt.Ink,1.4f);
        int first=debugSelected/DebugPageSize*DebugPageSize;
        for(int i=first;i<Math.Min(rows.Length,first+DebugPageSize);i++){
            float y=124+(i-first)*36;
            if(i==debugSelected)TitleArt.Rounded(ui,62,y-4,516,34,15,new Color32(221,231,188));
            font.Draw(ui,Label(rows[i]),76,y,TitleArt.Ink,1.1f);
        }
        string page=(first+1)+"-"+Math.Min(rows.Length,first+DebugPageSize)+" / "+rows.Length;
        font.Draw(ui,page,558-font.Measure(page,HelpScale),90,TitleArt.Ink,HelpScale);
        font.Draw(ui,"A / Left / Right: change",74,342,TitleArt.Ink,HelpScale);
        font.Draw(ui,"X: reset slider   B: save and close",74,371,TitleArt.Ink,HelpScale);
        DrawStatus(rows[debugSelected]==DevConfig.Count?"Session only; resets on restart.":dirty?"Changes will be saved to dev config.":DevConfigStorage.Status,74,404,492);
    }
    // Reflow only when a message changes. Keep two readable lines instead of
    // scaling small status/error text down to fit a TV-sized panel.
    private void DrawStatus(string text,float x,float y,float width,bool centered=false)
    {
        if(statusSource!=text||statusWidth!=width){
            statusSource=text;statusWidth=width;
            // Exception text may contain line breaks. Bound physical lines as
            // well as width so feedback always fits the two-line footer.
            string message=text.Replace('\r',' ').Replace('\n',' ');
            statusLine1=message;statusLine2="";
            int count=FitText(message,width);
            if(count<message.Length){
                for(int i=count-1;i>0;i--)if(message[i]==' '){count=i;break;}
                statusLine1=message.Substring(0,count);int start=count;while(start<message.Length&&message[start]==' ')start++;
                string rest=message.Substring(start);int tail=FitText(rest,width);
                statusLine2=tail<rest.Length?rest.Substring(0,FitText(rest,width-font.Measure("...",HelpScale)))+"...":rest;
            }
        }
        if(centered){
            var first=font.GlyphBounds(statusLine1,HelpScale);var second=font.GlyphBounds(statusLine2,HelpScale);
            float top=first.Y,bottom=first.Y+first.Height;
            if(statusLine2.Length>0){top=Math.Min(top,24+second.Y);bottom=Math.Max(bottom,24+second.Y+second.Height);}
            float originY=y+(46-(bottom-top))*.5f-top;
            font.Draw(ui,statusLine1,x+(width-first.Width)*.5f-first.X,originY,TitleArt.Ink,HelpScale);
            font.Draw(ui,statusLine2,x+(width-second.Width)*.5f-second.X,originY+24,TitleArt.Ink,HelpScale);
        }else{
            font.Draw(ui,statusLine1,x,y,TitleArt.Ink,HelpScale);
            font.Draw(ui,statusLine2,x,y+24,TitleArt.Ink,HelpScale);
        }
    }
    private int FitText(string text,float width)
    {
        int low=0,high=text.Length;
        while(low<high){int count=(low+high+1)/2;if(font.Measure(text.Substring(0,count),HelpScale)<=width)low=count;else high=count-1;}
        return low;
    }
    private string Label(int row)
    {
        if(row==DevConfig.Count)return "UI bubble noise: "+UiNoiseSettings.Percent+"% (session)";
        int value=config.Values[row];string toggle=value==0?"Off":"On";
        switch(row){
            case DevConfig.FullResolutionAaIndex:return "Full-resolution AA: "+(VideoOptions.FullResolutionAa?"On":"Off")+(VideoOptions.AaSaveFailed?" (not saved)":" (saved)");
            case 0:return "Performance display: "+toggle;
            case 1:return "Model drawing: "+(value==1?"GX indexed":"Direct comparison");
            case 2:return "UV island colors: "+toggle;case 3:return "UV island outlines: "+toggle;
            case 4:return "Batched drawing: "+toggle;case 5:return "Batched text: "+toggle;
            case 6:return "Actor rim lighting: "+toggle;
            case 9:return "Cliff preview: "+toggle;case 10:return "Water inspection: "+value+" / 5";
            case 11:return "Frame pacing: "+(value==0?"Yield":"Uncapped");case 12:return "Productive waits: "+toggle;case 13:return "Reuse frame clear: "+toggle;
            case 16:return "Rain preview: "+toggle;case 17:return "Rough ocean in rain: "+toggle;
            case 18:return "Season preview: "+(value==0?"Auto":TerrainTiles.SeasonName(value-1));
            case 19:return "Villager attention bubbles: "+toggle;case 20:return "Rim intensity: "+value+"%";
            case 21:return "SDK performance overlay: "+toggle;case 22:return "Ground mipmaps: "+toggle;
            case 23:return "Ground mip distance: "+(value==0?"Auto":value+" squares");
            case DevConfig.GroundMipBandsIndex:return "Ground mip bands: "+toggle;
            case 24:return "Freeze landscape motion: "+toggle;case 25:return "Recreate terrain meshes: "+toggle;
            case 26:return "Legacy ground ring: "+toggle;
            case DevConfig.TreeNoiseStrength:return "Tree noise strength: "+config.TreeNoiseStrengthPercent+"%";
            case DevConfig.TreeNoiseScale:return "Tree noise scale: "+config.TreeNoiseScalePercent+"%";
            default:return "discard terrain cache when indoors: "+toggle;
        }
    }
    public void Dispose(){optionsHeading?.Release();background.Dispose();TextureCache.Release(logo);font.Release();ui.Dispose();TextureCache.TrimUnused();}
}
