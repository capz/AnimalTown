using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Diagnostics;

internal static class VideoOptions
{
    internal static string Status="";
    internal static bool FullResolutionAa=>GraphicsDevice.Default.Display.AntialiasingMode==AntialiasingMode.FullResolution;
    private static bool requestedAa;
    internal static bool ApplyEdgeSmoothing(UserOptions options,bool enabled)
    {
        if(!SetEdgeSmoothing(enabled))return false;
        // Enabling at 576p moves the live display to 480p. Persist that actual
        // result so later widescreen changes and restarts cannot resurrect 576p.
        options.EdgeSmoothing=FullResolutionAa;options.Resolution=ActiveResolution;
        options.Widescreen=GraphicsDevice.Default.Display.Widescreen;return true;
    }
    internal static bool SetEdgeSmoothing(bool enabled)
    {
        bool before=requestedAa;requestedAa=enabled;
        var display=GraphicsDevice.Default.Display;
        try{
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{
                Resolution=requestedAa?GraphicsResolution.R640x480:display.Resolution,
                Scan=display.Scan,Widescreen=display.Widescreen,
                AntialiasingMode=requestedAa?AntialiasingMode.FullResolution:AntialiasingMode.None});
            AaStudy.Reset();SetAspect();Status="";LogAa();return true;
        }catch(OutOfMemoryException){Status="Not enough memory; edge smoothing is unchanged.";}
        catch(InvalidOperationException){Status="Edge smoothing needs 640 x 480 output.";}
        requestedAa=before;return false;
    }
    internal static int ActiveResolution
    {
        get{
            var display=GraphicsDevice.Default.Display;
            if(display.Scan!=ScanMode.Progressive)return 0;
            return display.Resolution==GraphicsResolution.R640x576?2:display.Resolution==GraphicsResolution.R640x480?1:0;
        }
    }
    internal static void Load(UserOptions options)
    {
        requestedAa=options.EdgeSmoothing;
        // The sample's recovery shortcut avoids trapping a different TV in a
        // persisted mode. It does not overwrite the user's saved preference.
        Controller.Update();
        if(Controller.GetState(0).X.IsHeld){requestedAa=false;Status="Saved video bypassed (X held).";SetAspect();return;}
        // AA changes the framebuffer size, not the user's scan preference.
        // Auto must retain 480i on analogue cables. A legacy AA/576p pair
        // still resolves to 480p, matching its explicitly saved progressive mode.
        int resolution=requestedAa&&options.Resolution==2?1:options.Resolution;
        if(!Apply(resolution,options.Widescreen,true)){
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{Widescreen=options.Widescreen});SetAspect();
        }
    }
    internal static bool Apply(int resolution,bool widescreen,bool changeResolution)
    {
        if(requestedAa&&changeResolution&&resolution==2){Status="Turn off edge smoothing to use 576p.";return false;}
        try{
            // Explicit resolution choices request progressive output. When AA
            // forces a 480-line framebuffer from an automatic/PAL boot mode,
            // retain the live scan instead of letting cable detection choose it.
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{
                Resolution=requestedAa?GraphicsResolution.R640x480:!changeResolution||resolution==0?GraphicsResolution.Auto:resolution==2?GraphicsResolution.R640x576:GraphicsResolution.R640x480,
                Scan=!changeResolution||resolution==0?requestedAa?GraphicsDevice.Default.Display.Scan:ScanMode.Auto:ScanMode.Progressive,
                AntialiasingMode=requestedAa?AntialiasingMode.FullResolution:AntialiasingMode.None,
                Widescreen=widescreen
            });
            SetAspect();LogAa();Status="";
            var display=GraphicsDevice.Default.Display;
            DebugOverlay.WriteLine("ANIMALTOWN VIDEO "+ActiveResolution+" "+(display.Widescreen?1:0)+" output "+(int)display.OutputSize.X+"x"+(int)display.OutputSize.Y);
            return true;
        }catch(OutOfMemoryException){Status="Not enough memory; video is unchanged.";return false;}
        catch(InvalidOperationException){Status="Progressive mode needs a compatible cable and display.";return false;}
    }
    private static void LogAa()
    {
        var device=GraphicsDevice.Default;
        DebugOverlay.WriteLine("ANIMALTOWN AA "+(FullResolutionAa?1:0)+" render "+device.Width+" "+device.Height+" parts "+device.FramePartCount);
    }
    private static void SetAspect()=>RenderView.SetWidescreen(GraphicsDevice.Default.Display.Widescreen);
}
