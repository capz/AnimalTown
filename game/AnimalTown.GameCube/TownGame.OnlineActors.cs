#if ANIMALTOWN_ONLINE_PLAY
using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    sealed class OnlineActorView
    {
        internal ModelInstance? Instance;internal int Species=-1,Clip=-2,Room;
        internal bool Drawn;
        internal float X,Z,Yaw,FromX,FromZ,ToX,ToZ;internal long ChangedAt,AnimatedAt;
        internal readonly Transform Pose=new Transform();
    }
    OnlineActorView[]? onlineActors;
    void DisposeOnlineActors()
    {
        if(onlineActors==null)return;
        for(int i=0;i<onlineActors.Length;i++){onlineActors[i].Instance?.Dispose();onlineActors[i].Instance=null;}
        onlineActors=null;
    }
    void PrepareOnlineActors(long now,bool allocate)
    {
        var visit=session.Online;if(visit==null||!resourcesReady)return;
        if(onlineActors==null){onlineActors=new[]{new OnlineActorView(),new OnlineActorView(),new OnlineActorView()};for(int i=0;i<onlineActors.Length;i++)onlineActors[i].Pose.Parent=worldRoot;}
        int mask=0;
        for(byte slot=0;slot<3;slot++)
        {
            var actor=onlineActors[slot];var human=visit.State.Humans[slot];
            bool present=slot!=visit.Slot&&human.Present&&visit.Connection.TryGetActor(slot,out _);
            if(!present){actor.Instance?.Dispose();actor.Instance=null;actor.Species=-1;continue;}
            visit.Connection.TryGetActor(slot,out var sample);float x=sample.X/65536f,z=sample.Z/65536f;
            bool visible=sample.Room==room&&(room!=0||(x-state.X)*(x-state.X)+(z-state.Z)*(z-state.Z)<26*26);
            if(actor.Species!=human.Species||actor.Room!=sample.Room||!visible)
            {actor.Instance?.Dispose();actor.Instance=null;actor.Clip=-2;actor.ChangedAt=0;}
            actor.Species=human.Species;actor.Room=sample.Room;actor.Yaw=sample.Facing*(6.2831853f/65536f);
            if(actor.ChangedAt==0||Math.Abs(x-actor.ToX)+Math.Abs(z-actor.ToZ)>0.0001f)
            {
                bool snap=actor.ChangedAt==0||(x-actor.X)*(x-actor.X)+(z-actor.Z)*(z-actor.Z)>4;
                actor.FromX=snap?x:actor.X;actor.FromZ=snap?z:actor.Z;actor.ToX=x;actor.ToZ=z;actor.ChangedAt=now;
            }
            float fraction=Math.Clamp((now-actor.ChangedAt)/100000f,0,1);actor.X=actor.FromX+(actor.ToX-actor.FromX)*fraction;actor.Z=actor.FromZ+(actor.ToZ-actor.FromZ)*fraction;
            if(!visible)continue;
            mask|=1<<human.Species;
            if(allocate&&actor.Instance==null&&modelResidency.Ready(human.Species)&&modelLod.Level(human.Species)==0&&modelLod.PendingSpecies!=human.Species)
            {
                int ticket=0;
                if(!startupAdmission.TryReserveInstance(human.Species,now,out ticket))continue;
                try
                {
                    actor.Instance=models[human.Species].CreateInstance(ModelMemoryPlan.MaximumInstanceBytes);actor.AnimatedAt=now;actor.Clip=-2;actor.Drawn=false;
                    int bytes=0;for(int i=0;i<actors.Length;i++)if(actors[i]?.Model==models[human.Species])bytes+=actors[i]!.NativeBytes;
                    for(int i=0;i<onlineActors.Length;i++)if(onlineActors[i].Species==human.Species&&onlineActors[i].Instance!=null)bytes+=onlineActors[i].Instance!.NativeBytes;
                    modelResidency.RecordWarmPeak(human.Species,bytes);visualMemory.Pressure.Complete(ticket);
                    visualMemory.Sample(now,true);
                    Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN ONLINE ACTOR READY slot "+slot+" species "+human.Species+" instance bytes "+actor.Instance.NativeBytes+" sampled free "+visualMemory.Pressure.FreeBytes);
                }
                catch(OutOfMemoryException){visualMemory.Pressure.Cancel(ticket);visualMemory.Pressure.AllocationFailed(startupAdmission.InstancePeak(human.Species),now);}
            }
            if(allocate&&actor.Instance!=null&&Visible(actor.X,actor.Z,1.8f))
            {
                bool moving=(actor.ToX-actor.FromX)*(actor.ToX-actor.FromX)+(actor.ToZ-actor.FromZ)*(actor.ToZ-actor.FromZ)>.0001f&&now-actor.ChangedAt<200000;
                int clip=moving?0:IdleClip;string name=moving?"Walking":"Idle";
                if(actor.Clip!=clip){if(models[actor.Species].TryGetAnimation(name,out _))actor.Instance.Animator.Play(name,true);else actor.Instance.Animator.Stop();actor.Clip=clip;}
                float seconds=Math.Min(.1f,(now-actor.AnimatedAt)/1000000f);if(seconds>0)actor.Instance.Update(seconds);actor.AnimatedAt=now;
            }
        }
        modelResidency.OnlineSpeciesMask=mask;modelLod.OnlineSpeciesMask=mask;
    }
    void DrawOnlineActors()
    {
        var visit=session.Online;if(visit==null||onlineActors==null)return;
        for(byte slot=0;slot<3;slot++)
        {
            var actor=onlineActors[slot];if(actor.Instance==null||actor.Room!=room||!Visible(actor.X,actor.Z,1.8f))continue;
            var pose=actor.Pose;pose.LocalPosition=SurfacePoint(actor.X,actor.Z);pose.LocalScale=new Vector3(1.8f,1.8f,1.8f);pose.LocalRotation=SurfaceRotation(actor.Z,actor.Yaw);
            NativeRim.Enable(RenderDebugOptions.DefaultRimPercent);NativeShadows.Objects(room==0?1:0);
            try{
                renderer.DrawModel(actor.Instance,pose);jobScene.Outfit(renderer,visit.State.Humans[slot].Shirt,actor.Species,pose.LocalPosition,pose.LocalRotation);
                if(!actor.Drawn){actor.Drawn=true;Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN ONLINE ACTOR DRAW slot "+slot+" species "+actor.Species);}
            }
            finally{NativeShadows.Objects(0);NativeRim.Enable(0);}
        }
        // The recipient's outfit uses the same existing mesh as local play.
        if(actorVisibility.Visible[0])jobScene.Outfit(renderer,state.Shirt,state.Species,SurfacePoint(state.X,state.Z),SurfaceRotation(state.Z,facingYaw));
    }
    void PrepareOnlineActorShadows(int size,bool mat)
    {
        if(onlineActors==null)return;int shadow=BuildingWorld.Actors;
        for(int slot=0;slot<onlineActors.Length;slot++)
        {
            if(slot==session.Online!.Slot)continue;
            var actor=onlineActors[slot];
            if(actor.Instance!=null&&actor.Room==room&&Visible(actor.X,actor.Z,1.8f))villagerShadows.Set(shadow,room,actor.X,actor.Z,size,mat);
            shadow++;
        }
    }
}
#endif
