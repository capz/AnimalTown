using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Diagnostics;

internal static class VideoOptions
{
    internal static string Status="";
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
        // The sample's recovery shortcut avoids trapping a different TV in a
        // persisted mode. It does not overwrite the user's saved preference.
        Controller.Update();
        if(Controller.GetState(0).X.IsHeld){Status="Saved video bypassed (X held).";SetAspect();return;}
        if(!Apply(options.Resolution,options.Widescreen,true)){
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{Widescreen=options.Widescreen});SetAspect();
        }
    }
    internal static bool Apply(int resolution,bool widescreen,bool changeResolution)
    {
        try{
            // Match VideoModeOutput in the SDK sample. Aspect-only changes use
            // Auto/Auto, preserving output timing and avoiding replacement XFBs.
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{
                Resolution=!changeResolution||resolution==0?GraphicsResolution.Auto:resolution==2?GraphicsResolution.R640x576:GraphicsResolution.R640x480,
                Scan=!changeResolution||resolution==0?ScanMode.Auto:ScanMode.Progressive,
                Widescreen=widescreen
            });
            SetAspect();Status="";
            var display=GraphicsDevice.Default.Display;
            DebugOverlay.WriteLine("ANIMALTOWN VIDEO "+ActiveResolution+" "+(display.Widescreen?1:0)+" output "+(int)display.OutputSize.X+"x"+(int)display.OutputSize.Y);
            return true;
        }catch(OutOfMemoryException){Status="Not enough memory; video is unchanged.";return false;}
        catch(InvalidOperationException){Status="Progressive mode needs a compatible cable and display.";return false;}
    }
    private static void SetAspect()=>RenderView.SetWidescreen(GraphicsDevice.Default.Display.Widescreen);
}
