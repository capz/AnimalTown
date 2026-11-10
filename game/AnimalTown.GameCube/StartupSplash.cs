using System;
using System.IO;
using AnimalTown.GameCube;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

// Private, one-shot ownership: never insert splash pixels into the gameplay LRU.
internal sealed class StartupSplash : IDisposable
{
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Draw2D ui;
    private Texture2D? texture;
    private bool whiteBackground;

    private StartupSplash()
    {ui=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,ScaleMode=CanvasScaleMode.Fit,VertexCapacity=12});}

    internal static TownGame Run()
    {
        TownGame game;
        using(var splash=new StartupSplash()){
            // Synchronous save/font/terrain setup must not extend either logo.
            // Keep the display black until the identical timed presentations.
            splash.device.BeginFrame(new Color32(0,0,0));splash.device.EndFrame();
            game=new TownGame();
            splash.Load("dolphindotnet");
            var timing=new SplashTiming();
            while(!timing.Complete)splash.Frame(timing,game);
            splash.BlackFrame();
            splash.Load("hypercube-engine");
            timing=new SplashTiming();
            while(!timing.Complete)splash.Frame(timing,game);
            splash.BlackFrame();
        }
        return game;
    }

    private void Load(string name)
    {
        // Previous EndFrame completed GX_DrawDone. No frame/pass is active here.
        texture?.Dispose();texture=null;
        texture=LoadTexture("dvd:/splash-"+name+".dndtex");
        whiteBackground=name=="dolphindotnet";
        NativeTexturePages.Collect();
        DebugOverlay.WriteLine("ANIMALTOWN SPLASH "+name+" native bytes "+texture.NativeBytes);
    }

    private static Texture2D LoadTexture(string path)=>Texture2D.FromPackage(File.ReadAllBytes(path),true);

    private void BlackFrame()
    {device.BeginFrame(new Color32(0,0,0));device.EndFrame();}

    private void Frame(SplashTiming timing,TownGame game)
    {
        long start=Clock.Timestamp;
        Controller.Update(); // Consume splash input; it cannot open the title menu.
        // Clear the whole display, including areas outside the fitted canvas.
        // DolphinDotNet's black mark fades against white without a boxed border.
        device.BeginFrame(whiteBackground?new Color32(255,255,255):new Color32(0,0,0));
        ui.Begin();
        ui.DrawTexture(texture!,new RectF(0,0,384,256),new RectF(32,48,576,384),new Color32(255,255,255,timing.Alpha));
        ui.End();device.EndFrame();
        // Existing async model I/O and staged resource creation run only between
        // frames on the app thread; never Task.Run a constructor or a GX upload.
        game.AdvanceLoading();
        RuntimeOptions.Wait();
        timing.Advance(Clock.Timestamp-start);
    }

    public void Dispose()
    {texture?.Dispose();texture=null;ui.Dispose();}
}
