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
    internal static bool AaSaveFailed;
    internal static bool ToggleFullResolutionAa(DevConfig config)
    {
        bool before=requestedAa;requestedAa=!FullResolutionAa;
        var display=GraphicsDevice.Default.Display;
        try{
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{
                Resolution=requestedAa?GraphicsResolution.R640x480:display.Resolution,
                Scan=display.Scan,Widescreen=display.Widescreen,
                AntialiasingMode=requestedAa?AntialiasingMode.FullResolution:AntialiasingMode.None});
            config.Values[DevConfig.FullResolutionAaIndex]=requestedAa?1:0;
            AaStudy.Reset();SetAspect();Status="";LogAa();return true;
        }catch(OutOfMemoryException){Status="Not enough memory; AA is unchanged.";}
        catch(InvalidOperationException){Status="Full-resolution AA needs 640 x 480 output.";}
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
    internal static void Load(UserOptions options,DevConfig config)
    {
        requestedAa=config.Values[DevConfig.FullResolutionAaIndex]!=0;
        // The sample's recovery shortcut avoids trapping a different TV in a
        // persisted mode. It does not overwrite the user's saved preference.
        Controller.Update();
        if(Controller.GetState(0).X.IsHeld){requestedAa=false;Status="Saved video bypassed (X held).";SetAspect();return;}
        if(!Apply(requestedAa?1:options.Resolution,options.Widescreen,true)){
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{Widescreen=options.Widescreen});SetAspect();
        }
    }
    internal static bool Apply(int resolution,bool widescreen,bool changeResolution)
    {
        if(requestedAa&&changeResolution&&resolution==2){Status="Turn off full-resolution AA to use 576p.";return false;}
        try{
            // Match VideoModeOutput in the SDK sample. Aspect-only changes use
            // Auto/Auto, preserving output timing and avoiding replacement XFBs.
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{
                Resolution=requestedAa?GraphicsResolution.R640x480:!changeResolution||resolution==0?GraphicsResolution.Auto:resolution==2?GraphicsResolution.R640x576:GraphicsResolution.R640x480,
                Scan=!changeResolution||resolution==0?ScanMode.Auto:ScanMode.Progressive,
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
