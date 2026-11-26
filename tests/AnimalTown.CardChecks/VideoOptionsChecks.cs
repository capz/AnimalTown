using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;

// Runs the production video controller against the pinned SDK's mode-selection
// contract. It verifies requested timing/transactionality, not GX image quality.
internal static class VideoOptionsChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var device=GraphicsDevice.Default;
        foreach(var boot in new[]{GraphicsResolution.R640x480,GraphicsResolution.R640x576})foreach(bool component in new[]{false,true}){
            device.Reset(boot,ScanMode.Interlaced,component);Controller.HoldX=false;
            var options=new UserOptions();VideoOptions.Load(options);
            check(VideoOptions.FullResolutionAa&&device.Display.Resolution==GraphicsResolution.R640x480&&device.Display.Scan==ScanMode.Interlaced,"default edge smoothing works at 480i and preserves interlaced boot timing regardless of cable");
            check(options.Resolution==0&&options.EdgeSmoothing&&VideoOptions.Status.Length==0,"default AA does not rewrite the stored automatic resolution or report a progressive-cable error");
        }
        device.Reset(GraphicsResolution.R640x480,ScanMode.Progressive,true);VideoOptions.Load(new UserOptions());
        check(VideoOptions.FullResolutionAa&&device.Display.Scan==ScanMode.Progressive,"automatic AA retains an already-progressive boot mode");
        device.Reset(GraphicsResolution.R640x480,ScanMode.Interlaced,true);VideoOptions.Load(new UserOptions{Resolution=1,Widescreen=true});
        check(VideoOptions.FullResolutionAa&&device.Display.Scan==ScanMode.Progressive&&device.Display.Widescreen,"an explicitly saved 480p preference still selects progressive output with AA");
        device.Reset(GraphicsResolution.R640x480,ScanMode.Interlaced,true);VideoOptions.Load(new UserOptions{Resolution=2,EdgeSmoothing=false});
        check(!VideoOptions.FullResolutionAa&&device.Display.Resolution==GraphicsResolution.R640x576&&device.Display.Scan==ScanMode.Progressive,"saved 576p remains available with edge smoothing disabled");
        device.Reset(GraphicsResolution.R640x480,ScanMode.Interlaced,true);VideoOptions.Load(new UserOptions{Resolution=2});
        check(VideoOptions.FullResolutionAa&&device.Display.Resolution==GraphicsResolution.R640x480&&device.Display.Scan==ScanMode.Progressive,"a legacy AA/576p combination keeps its progressive preference at AA-compatible 480 lines");
        int calls=device.Configurations;
        check(!VideoOptions.Apply(2,false,true)&&device.Configurations==calls&&VideoOptions.FullResolutionAa,"incompatible 576p selection cannot alter the active AA mode");
        check(VideoOptions.SetEdgeSmoothing(false)&&!VideoOptions.FullResolutionAa&&VideoOptions.Apply(2,false,true)&&device.Display.Resolution==GraphicsResolution.R640x576,"disabling smoothing permits 576p through the ordinary video path");

        device.Reset(GraphicsResolution.R640x576,ScanMode.Interlaced,false);Controller.HoldX=true;var recovery=new UserOptions{Resolution=1,Widescreen=true};VideoOptions.Load(recovery);
        check(device.Configurations==0&&device.Display.Resolution==GraphicsResolution.R640x576&&!device.Display.Widescreen&&!VideoOptions.FullResolutionAa,"X-held recovery leaves the boot display untouched");
        check(recovery.Resolution==1&&recovery.EdgeSmoothing&&recovery.Widescreen,"recovery does not overwrite persisted preferences");
        Controller.HoldX=false;device.Reset(GraphicsResolution.R640x480,ScanMode.Interlaced,false);VideoOptions.Load(new UserOptions{Resolution=1,EdgeSmoothing=false,Widescreen=true});
        check(device.Display.Scan==ScanMode.Interlaced&&!VideoOptions.FullResolutionAa&&device.Display.Widescreen&&VideoOptions.Status.Contains("compatible cable"),"unsupported saved progressive output retains safe boot timing and reports its fallback");

        device.Reset(GraphicsResolution.R640x480,ScanMode.Interlaced,true);VideoOptions.Load(new UserOptions{EdgeSmoothing=false});device.FailNextAllocation=true;
        check(!VideoOptions.SetEdgeSmoothing(true)&&!VideoOptions.FullResolutionAa&&device.Display.Scan==ScanMode.Interlaced,"failed AA allocation leaves the current output unchanged");
        check(VideoOptions.Apply(2,false,true)&&device.Display.Resolution==GraphicsResolution.R640x576,"a failed AA switch also restores the requested mode used by later video operations");

        var menu=new UserOptions{Resolution=2,EdgeSmoothing=false,Widescreen=true};
        device.Reset(GraphicsResolution.R640x480,ScanMode.Interlaced,true);VideoOptions.Load(menu);
        check(VideoOptions.ApplyEdgeSmoothing(menu,true)&&menu.EdgeSmoothing&&menu.Resolution==1&&menu.Widescreen,"the video-menu toggle records the actual 480p result when enabling smoothing from widescreen 576p");
        VideoOptions.Load(UserOptions.Decode(menu.Encode()));
        check(VideoOptions.FullResolutionAa&&device.Display.Resolution==GraphicsResolution.R640x480&&device.Display.Widescreen,"reloading the menu preference restores the applied mode and aspect");
        check(VideoOptions.Apply(1,false,false)&&device.Display.Resolution==GraphicsResolution.R640x480,"a later aspect change retains the AA-compatible resolution");
        device.FailNextAllocation=true;byte[] before=menu.Encode();
        check(!VideoOptions.ApplyEdgeSmoothing(menu,false)&&VideoOptions.FullResolutionAa&&before.SequenceEqual(menu.Encode()),"a failed menu activation leaves both the live underline state and serializable preferences unchanged");
        check(VideoOptions.ApplyEdgeSmoothing(menu,false)&&!menu.EdgeSmoothing&&menu.Resolution==1&&!menu.Widescreen,"disabling smoothing records the live resolution and aspect");
        device.Reset(GraphicsResolution.R640x576,ScanMode.Interlaced,false);menu=new UserOptions{EdgeSmoothing=false};VideoOptions.Load(menu);
        check(VideoOptions.ApplyEdgeSmoothing(menu,true)&&menu.Resolution==0&&device.Display.Scan==ScanMode.Interlaced,"the menu toggle retains automatic interlaced timing on analogue cables");
        RenderView.SetWidescreen(false);
        Console.WriteLine("Video options: default 480i AA, saved progressive modes, 576p guard, recovery and transactional failure passed.");
    }
}

internal static class AaStudy {internal static void Reset(){}}
namespace Dolphin.Input
{
    internal static class Controller
    {
        internal static bool HoldX;
        internal static void Update(){}
        internal static ControllerState GetState(int port)=>new ControllerState();
    }
    internal readonly struct ControllerState {internal ButtonState X=>new ButtonState();}
    internal readonly struct ButtonState {internal bool IsHeld=>Controller.HoldX;}
}
namespace Dolphin.Graphics
{
    internal enum GraphicsResolution {Auto,R320x240,R640x480,R640x576}
    internal enum ScanMode {Auto,Interlaced,Progressive}
    internal enum AntialiasingMode {None,ReducedResolution,FullResolution}
    internal sealed class DisplayOptions
    {
        internal GraphicsResolution Resolution {get;set;}
        internal ScanMode Scan {get;set;}
        internal AntialiasingMode AntialiasingMode {get;set;}
        internal bool Widescreen {get;set;}
    }
    internal sealed class DisplayInfo
    {
        internal GraphicsResolution Resolution;
        internal ScanMode Scan;
        internal AntialiasingMode AntialiasingMode;
        internal bool Widescreen;
        internal (float X,float Y) OutputSize=>(Resolution==GraphicsResolution.R320x240?320:640,Resolution==GraphicsResolution.R640x576?576:Resolution==GraphicsResolution.R320x240?240:480);
    }
    internal sealed class GraphicsDevice
    {
        internal static readonly GraphicsDevice Default=new GraphicsDevice();
        internal readonly DisplayInfo Display=new DisplayInfo();
        internal bool ComponentCable,FailNextAllocation;
        internal int Configurations;
        internal int Width=>(int)Display.OutputSize.X;
        internal int Height=>(int)Display.OutputSize.Y;
        internal int FramePartCount=>Display.AntialiasingMode==AntialiasingMode.FullResolution?2:1;
        internal void Reset(GraphicsResolution resolution,ScanMode scan,bool component)
        {Display.Resolution=resolution;Display.Scan=scan;Display.AntialiasingMode=AntialiasingMode.None;Display.Widescreen=false;ComponentCable=component;FailNextAllocation=false;Configurations=0;}
        internal void ConfigureDisplay(DisplayOptions options)
        {
            Configurations++;
            var resolution=options.Resolution==GraphicsResolution.Auto?Display.Resolution:options.Resolution;
            var scan=options.Scan==ScanMode.Auto?(resolution==Display.Resolution?Display.Scan:ComponentCable?ScanMode.Progressive:ScanMode.Interlaced):options.Scan;
            if(scan==ScanMode.Progressive&&!ComponentCable||options.AntialiasingMode==AntialiasingMode.FullResolution&&resolution!=GraphicsResolution.R640x480)throw new InvalidOperationException("Unsupported display");
            if(FailNextAllocation){FailNextAllocation=false;throw new OutOfMemoryException();}
            Display.Resolution=resolution;Display.Scan=scan;Display.AntialiasingMode=options.AntialiasingMode;Display.Widescreen=options.Widescreen;
        }
    }
}
