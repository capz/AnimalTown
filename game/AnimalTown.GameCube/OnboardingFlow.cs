using System;
using AnimalTown;
using Dolphin.Input;
using Dolphin.Timing;

// Each view lives in its own frame/scope. No introductory UI survives the
// return to the town loader, including when revisiting identity from the plot.
internal static class OnboardingFlow
{
    internal const int ToLook=1,ToReview=2,ToTitle=3;
    internal static bool Run(DevConfig config,TownSession session)
    {
        int phase=ToLook;bool lookFirst=false;
        while(true){
            if(phase==ToLook){
                if(!Identity(session,lookFirst))return false;
                Retire();phase=0;
            }
            if(phase==0){
                int next=Program.RunSession(config,session);
                // The complete preview, including its model/terrain locals,
                // has unwound before any image-based setup view is allocated.
                Retire();
                if(next==ToTitle)return false;
                if(next==ToLook){lookFirst=true;phase=ToLook;continue;}
                phase=ToReview;
            }
            if(phase==ToReview){
                bool saved=Review(session);Retire();
                if(saved)return true;
                phase=0;
            }
        }
    }
    private static void Retire()
    {LoadingScreen.Finish();TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();}
    private static bool Review(TownSession session)
    {
        var state=session.State;using var view=new OnboardingUi(OnboardingUi.Review,state.Species);string error="";
        while(true){
            view.DrawReview(state,error,false);RuntimeOptions.Wait();Controller.Update();var p=Controller.GetState(0);
            if(p.B.WasPressed){
                int x=state.Town.HouseX,z=state.Town.HouseZ;session.Draft!.ClearHome();
                state.X=x+3.5f;state.Z=z+6.5f;return false;
            }
            if(p.A.WasPressed){
                view.DrawReview(state,"",true);
                if(session.SaveOnboarding())return true;
                error=session.OnboardingSaveError;
            }
        }
    }
    internal static bool Identity(TownSession session,bool lookFirst=false)
    {
        var draft=session.Draft??throw new InvalidOperationException("Missing onboarding draft");
        string entry=draft.IsNewProfile&&!lookFirst?"":draft.State.Name;
        int stage=lookFirst?OnboardingUi.Look:OnboardingUi.Name,key=0;
        while(true){
            using var view=new OnboardingUi(stage,draft.State.Species);
            var navigation=new MenuNavigation();bool exit=false,discard=false;string message="";
            while(true){
                if(stage==OnboardingUi.Name)view.DrawName(entry,key,exit,discard,message);
                else view.DrawLook(entry,draft.State.Species,exit,discard);
                RuntimeOptions.Wait();Controller.Update();var p=Controller.GetState(0);
                int direction=navigation.Read(exit?"onboard-exit":stage==OnboardingUi.Name?"onboard-name":"onboard-look",0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
                if(exit){
                    if(direction==MenuNavigation.Left||direction==MenuNavigation.Up)discard=false;
                    if(direction==MenuNavigation.Right||direction==MenuNavigation.Down)discard=true;
                    if(p.B.WasPressed){exit=false;continue;}
                    if(p.A.WasPressed||p.Start.WasPressed){if(discard)return false;exit=false;}
                    continue;
                }
                if(stage==OnboardingUi.Name){
                    if(direction==MenuNavigation.Left)key=(key+39)%40;
                    if(direction==MenuNavigation.Right)key=(key+1)%40;
                    if(direction==MenuNavigation.Up)key=(key+30)%40;
                    if(direction==MenuNavigation.Down)key=(key+10)%40;
                    if(p.A.WasPressed&&entry.Length<16){
                        char c="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .!?"[key];
                        if(c>='A'&&c<='Z'&&entry.Length>0&&entry[entry.Length-1]!=' ')c=(char)(c+32);
                        entry+=new string(new[]{c});message="";
                    }
                    if(p.X.WasPressed&&entry.Length>0){entry=entry.Substring(0,entry.Length-1);message="";}
                    if(p.B.WasPressed){exit=true;discard=false;continue;}
                    if(p.Start.WasPressed){
                        if(!draft.SetName(entry)){message="Please enter a name (up to 16 characters).";continue;}
                        entry=draft.State.Name;stage=OnboardingUi.Look;break;
                    }
                }else{
                    if(direction==MenuNavigation.Left||direction==MenuNavigation.Up)draft.SetSpecies((draft.State.Species+5)%6);
                    if(direction==MenuNavigation.Right||direction==MenuNavigation.Down)draft.SetSpecies((draft.State.Species+1)%6);
                    if(p.B.WasPressed){stage=OnboardingUi.Name;break;}
                    if(p.A.WasPressed||p.Start.WasPressed)return true;
                }
            }
        }
    }
}
