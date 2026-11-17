using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;

// Research branch only. No startup/default/save setting is changed. Activate
// with L+R+D-right; L+R+D-left records 240 frames without a telemetry overlay.
internal static class AaStudy
{
    private const int Frames=240,Fields=6;
    private static int profile=-1,sequence,count;
    private static bool recording;
    private static int[]? rows;
    private static readonly string[] names={"480-rim100","480-rim0","480-rim50","480-aa-rim0","480-aa-rim100","240-rim100","240-aa-rim100"};
    internal static bool Active=>profile>=0;
    internal static void Reset(){profile=-1;recording=false;count=0;}
    internal static bool Input(ControllerState pad,RenderDebugOptions options,ref bool rim)
    {
        if(VideoOptions.FullResolutionAa)return false;
        if(!pad.L.IsHeld||!pad.R.IsHeld)return false;
        int direction=pad.DPadRight.WasPressed?1:Active&&pad.DPadLeft.WasPressed?-1:0;
        if(direction==0)return false;
        if(direction>0){
            if(recording)throw new InvalidOperationException("Wait for AA capture to finish");
            profile=(profile+1)%names.Length;
            bool low=profile>=5,aa=profile==3||profile==4||profile==6;
            int percent=profile==1||profile==3?0:profile==2?50:100;
            GraphicsDevice.Default.ConfigureDisplay(new DisplayOptions{
                Resolution=low?GraphicsResolution.R320x240:GraphicsResolution.R640x480,
                Scan=ScanMode.Progressive,Antialiasing=aa,Widescreen=false});
            RenderView.SetWidescreen(false);rim=percent>0;
            while(options.RimPercent!=percent)options.AdjustRim(options.RimPercent<percent?1:-1);
            FrameProfile.Enabled=true;
            if(rows==null)rows=new int[Frames*Fields];
            var display=GraphicsDevice.Default.Display;
            int width=(int)display.OutputSize.X,height=(int)display.OutputSize.Y;
            DebugOverlay.WriteLine("ANIMALTOWN AASTUDY MODE "+profile+" "+names[profile]+" render "+(int)display.RenderSize.X+" "+(int)display.RenderSize.Y+" output "+width+" "+height+" aa "+(display.Antialiasing?1:0)+" rim "+percent+" xfb_bytes "+(width*height*4)+" native_bytes "+GraphicsDevice.Default.NativeBytes);
        }else if(Active&&!recording){
            count=0;sequence++;recording=true;
            DebugOverlay.WriteLine("ANIMALTOWN AASTUDY BEGIN "+sequence+" "+profile);
        }
        return true;
    }
    internal static void Record(long frame,long gpu,long vi,long animation,int draws,int triangles)
    {
        if(!recording)return;
        int at=count*Fields;
        rows![at]=(int)frame;rows[at+1]=(int)gpu;rows[at+2]=(int)vi;rows[at+3]=(int)animation;rows[at+4]=draws;rows[at+5]=triangles;
        if(++count!=Frames)return;
        recording=false;
        // END precedes the log dump, so host timing excludes reporting costs.
        DebugOverlay.WriteLine("ANIMALTOWN AASTUDY END "+sequence+" "+profile+" "+count);
        for(int i=0;i<count;i++){
            at=i*Fields;
            DebugOverlay.WriteLine("ANIMALTOWN AASTUDY ROW "+sequence+" "+profile+" "+i+" "+rows[at]+" "+rows[at+1]+" "+rows[at+2]+" "+rows[at+3]+" "+rows[at+4]+" "+rows[at+5]);
        }
        DebugOverlay.WriteLine("ANIMALTOWN AASTUDY DONE "+sequence);
    }
}
