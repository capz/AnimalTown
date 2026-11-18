using AnimalTown;
using Dolphin.Input;
using Dolphin.Diagnostics;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    internal void Step(ControllerState pad)
    {
        if(resourcesReady)RefreshResidencyDemand(Clock.Timestamp);
        if(!resourcesReady||room==0&&!terrain.Ready){playerRunning=false;runningMinimap.Update(Clock.Timestamp,false,false);return;}
        bool running=false;float beforeX=state.X,beforeZ=state.Z;
        moveClip=-1; // UI focus must not leave the player walking in place.
        tick++;
        if(tick%30==0)ReadWeatherClock(NativeCalendar.Read());
        if(tick%900==0&&session.Draft==null){ReadCalendar();state.Rollover(day,month);}
        if(screen=="dialogue"&&typeAt<message.Length){
            typeAt++;
            if(typeAt==message.Length)dialogueVoice.Stop();
            else if(++voiceTick%3==0&&dialogueVoice.Available&&!dialogueVoice.Pulse())DebugOverlay.WriteLine("Dialogue audio unavailable; continuing text");
        }
        if(screen=="world"||screen=="plot")
        {
            float inputX=pad.Stick.X,inputY=pad.Stick.Y;
            if(room!=0&&inputX*inputX+inputY*inputY<PlayerMovement.DeadZone*PlayerMovement.DeadZone){int held=(int)pad.HeldButtons;inputX=((held&2)!=0?1:0)-((held&1)!=0?1:0);inputY=((held&8)!=0?1:0)-((held&4)!=0?1:0);}
            if(room!=0){indoorCamera.Configure(BuildingWorld.Definition(state,BuildingWorld.Building(room)));indoorCamera.Tick(pad.L.IsHeld&&pad.R.IsHeld?0:pad.CStick.X,pad.L.WasPressed&&!pad.R.IsHeld);indoorCamera.Movement(inputX,inputY,out inputX,out inputY);}
            if(!villagers.Doors.Busy(0)&&!(pad.L.IsHeld&&pad.R.IsHeld)&&PlayerMovement.Move(state,room,inputX,inputY,room==0?(int)pad.HeldButtons:0,pad.B.IsHeld,out float dx,out float dz,session.Draft==null))
            {
                facingYaw=PlayerMovement.Yaw(dx,dz);PlayerMovement.Target(dx,dz,out faceX,out faceZ);
                int clip=pad.B.IsHeld?1:0;moveClip=clip;running=clip==1&&(state.X!=beforeX||state.Z!=beforeZ);desiredPlayerSpeed=1.05f;
            }
        }
        // Menus own player input, never the live town simulation. Only the
        // uncommitted onboarding preview stays frozen.
        bool simulateWorld=session.Draft==null;
        if(simulateWorld){
            RefreshResidencyDemand(Clock.Timestamp);
            if(villagers.Doors.Busy(0)&&!villagers.Doors.ResourcesReady[0])modelResidency.WaitForPortal();
            int oldRoom=room;villagers.Tick(state);
            if(room!=oldRoom){facingYaw=state.Facing;faceX=0;faceZ=room==0?1:-1;indoorCamera.Reset();}
        }
        if(villagers.Doors.Busy(0)){
            facingYaw=state.Facing;
            if(villagers.Doors.Moving(0)){moveClip=0;desiredPlayerSpeed=1;}
        }
        if(simulateWorld)state.Town.Traffic.Tick(state.Town);
        runningMinimap.Update(Clock.Timestamp,running,screen=="world"&&room==0&&waterInspection==0&&!cliffPreview);
        playerRunning=running;
        state.Facing=facingYaw;cachedPlayerHeight=PlayerHeight();if(room==0)RenderView.Prepare(state.X,state.Z,cachedPlayerHeight);
        animationClock.Step(0,moveClip,models[state.Species].Animations.Count>0);
        for(int i=1;i<7;i++){
            var v=state.Town.Residents[i-1];bool moving=v.Space==room&&simulateWorld&&villagers.Moving[i-1];
            if(moving!=villagersMoving[i-1]){
                villagersMoving[i-1]=moving;animationClock.Reset(i);animationClock.Step(i,moving?0:-1,false);restartClips[i]=moving;
            }
            animationClock.Step(i,animationClock.Clip(i),moving);
        }
    }
    private int weatherDay;
    private void ReadWeatherClock(int date)
    {
        townClock.Update(NativeCalendar.ReadClock());
        // Town rollover uses 06:00; rain follows the calendar day at midnight.
        weatherDay=(date&0x1ffff)+(townClock.MinuteOfDay<360?1:0);
    }
    private void ReadCalendar(){int date=NativeCalendar.Read();day=date&0x1ffff;month=(int)((uint)date>>17);ReadWeatherClock(date);}
    private float PlayerHeight()=>room==0?TerrainElevation.At(state.Town,state.X,state.Z):0;
}
