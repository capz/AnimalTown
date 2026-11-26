using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;
using Dolphin.Diagnostics;

internal sealed partial class TownGame
{
    private const string VillagerVisualScreen="villager-visuals";
    private VillagerVisualSwap? visualSwap;
    private readonly MenuNavigation visualNavigation=new MenuNavigation();
    private int visualActor,visualCandidate=-1,visualRow;
    private void OpenVillagerVisuals()
    {
        try{visualSwap??=VillagerVisualSwap.Open(actors,visualMemory.Pressure);}
        catch(Exception error){DebugOverlay.WriteLine("VILLAGER VISUALS: "+error.Message);Say("Villager visuals","Appearance assets could not be opened.");return;}
        visualRow=0;visualActor=0;visualCandidate=visualSwap.Selection.NextUnused(state,-1,1);screen=VillagerVisualScreen;
        TraceVillagerVisualSelection();
    }
    private void TraceVillagerVisualSelection()=>DebugOverlay.WriteLine("VILLAGER VISUAL MENU actor "+visualActor+" candidate "+visualCandidate+" current "+visualSwap!.Selection.Current(state,visualActor));
    private string VisualActorLabel()=>visualActor==0?state.Name+" (you)":state.Town.NpcAt(visualActor-1).Name;
    private void VillagerVisualInput(ControllerState p)
    {
        if(p.B.WasPressed){screen="menu";menuTab=TestingTab;selected=Array.IndexOf(testingOptions,VillagerVisualOption);return;}
        if(visualSwap!.Busy)return;
        if(!VillagerAppearanceSelection.Present(state,visualActor))visualActor=0;
        int direction=visualNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
        if(direction==MenuNavigation.Up)visualRow=(visualRow+2)%3;
        if(direction==MenuNavigation.Down)visualRow=(visualRow+1)%3;
        int delta=direction==MenuNavigation.Left?-1:direction==MenuNavigation.Right?1:0;
        if(delta!=0&&visualRow==0){
            do{visualActor=(visualActor+BuildingWorld.Actors+delta)%BuildingWorld.Actors;}while(!VillagerAppearanceSelection.Present(state,visualActor));
        }
        if(delta!=0&&visualRow==1)visualCandidate=visualSwap.Selection.NextUnused(state,visualCandidate,delta);
        if(delta!=0)TraceVillagerVisualSelection();
        if(p.A.WasPressed&&visualRow==2&&visualCandidate>=0)visualSwap.Request(state,visualActor,visualCandidate,Clock.Timestamp);
    }
    private void PumpVillagerVisuals()
    {
        if(visualSwap==null)return;
        int actor=visualSwap.Pump(state,Clock.Timestamp);if(actor<0)return;
        visualOverrides[actor]=true;visualIdle[actor]=actors[actor]!.Model.TryGetAnimation("Idle",out _);visualAnimated[actor]=actors[actor]!.Model.Animations.Count>0;
        // The swap can complete on a frame without a simulation tick. Select
        // the standing clip now so the first draw never flashes the bind pose.
        if(animationClock.Clip(actor)<0&&visualIdle[actor])animationClock.Step(actor,IdleClip,false);
        else if(animationClock.Clip(actor)==IdleClip&&!visualIdle[actor])animationClock.Reset(actor);
        restartClips[actor]=true;appliedClips[actor]=-2;
        if(actor>0)attention.Register(actor-1,actors[actor]!);
        visualCandidate=visualSwap.Selection.NextUnused(state,visualCandidate,1);
        DebugOverlay.WriteLine("VILLAGER VISUAL APPLIED actor "+actor+" appearance "+visualSwap.Selection.Current(state,actor));
    }
    private void DrawVillagerVisuals(Color32 ink)
    {
        if(!VillagerAppearanceSelection.Present(state,visualActor))visualActor=0;
        Panel(45,58,550,365);Heading("Villager visuals",67,73,ink);Text("Debug / testing",69,118,ink,1.2f);
        for(int row=0;row<3;row++){
            float y=168+row*52;if(row==visualRow)Rounded(60,y-4,516,40,14,new Color32(221,231,188));
            string label=row==0?"Villager: < "+VisualActorLabel()+" >":row==1?"Appearance: < "+(visualCandidate<0?"None unused":VillagerAppearance.Names[visualCandidate])+" >":"Apply appearance";
            if(row==visualRow)Strong(label,69,y,ink,1.4f);else Text(label,69,y,ink,1.4f);
        }
        Text("Current: "+VillagerAppearance.Names[visualSwap!.Selection.Current(state,visualActor)],69,322,ink,1.2f);
        Text(visualSwap.Status,69,348,ink,1.05f);
        Text("Name, jobs and location stay the same.",69,370,ink,1.05f);
        Text("D-pad: select / change   A: apply   B: back",69,398,ink,1.05f);
    }
}
