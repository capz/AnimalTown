using System;
using System.IO;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Input;

// Recovery is available before choosing a resident, including the locked one.
internal sealed class OfflineTravelRecovery : IDisposable,Hypercube.GameCube.IFrameScene
{
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=2048,SpriteCapacity=UiFont.BatchCapacity});
    private readonly UiFont font=UiFont.LoadSystem(),heading=UiFont.Load("gorditas-bold");
    private readonly TitleBackdrop backdrop=new TitleBackdrop();
    private readonly WrappedText wrap=new WrappedText(43);
    private string status="Insert both town cards, then press A to continue.";
    internal static bool Run(TownSession session,string initial="")
    {
        string status=initial;
        while(Choose(status)){
            // Retire the confirmation surface before materializing two full
            // towns. The loading view owns no font/layout arrays.
            TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();LoadingScreen.Draw();
            try{
                using var cards=new OfflineTravelCards(session.State,File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3")),File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3")));
                long now=session.State.Town.Jobs.Watermark;var result=cards.Execute(session.State.Town.Jobs.NpcMove.Pending?4:2,session.State.ActiveProfile,0,0,now);cards.SelectResult(result);session.AdoptTravel(result);return true;
            }catch(Exception e){status=e.Message??"Keep both cards and recheck. Your traveller stays safe.";}
            finally{LoadingScreen.Finish();}
        }
        return false;
    }
    private static bool Choose(string initial)
    {
        using var view=new OfflineTravelRecovery();
        if(initial.Length!=0)view.status=initial;
        while(true){
            UiNoise.Prepare();view.backdrop.Prepare();Hypercube.GameCube.FrameRenderer.Draw(GraphicsDevice.Default,TitleBackdrop.Color,view);RuntimeOptions.Wait();Controller.Update();var p=Controller.GetState(0);
            if(p.B.WasPressed)return false;
            if(p.A.WasPressed||p.X.WasPressed)return true;
        }
    }
    public void SubmitFramePart()
    {
        ui.Begin();backdrop.Draw(ui);TitleArt.Panel(ui,95,125,450,240);heading.Draw(ui,"Continue your trip",119,146,TitleArt.Ink,1.05f);
        font.Draw(ui,wrap.Get(status),120,202,TitleArt.Ink,.76f);font.Draw(ui,"A / X: recheck and continue   B: back",119,326,TitleArt.Ink,.67f);ui.End();
    }
    public void Dispose(){ui.Dispose();font.Release();heading.Release();backdrop.Dispose();}
}
