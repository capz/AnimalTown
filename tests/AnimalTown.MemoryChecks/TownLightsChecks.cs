using AnimalTown;

internal static class TownLightsChecks
{
    private static GameState State(int seed=73)
    {
        var state=new GameState(seed){HousePlaced=true,X=80,Z=80};
        Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);
        state.Town.GroundCount=state.Town.PlacedCount=0;state.Town.HouseX=10;state.Town.HouseZ=90;
        for(int p=1;p<GameState.ProfileCount;p++){
            var profile=state.Profiles[p];profile.Created=profile.HousePlaced=true;profile.HouseX=10+p*25;profile.HouseZ=90;
        }
        return state;
    }
    // Independent direct-cylinder oracle: every active uploaded point retains
    // the original surface equation, height, radius and source ordering.
    private static void PointsMatch(GameState state,Action<bool,string> check)
    {
        check(NativeShadows.PointCount==NativeShadows.SourceCount,"active model points retain the complete static light list");
        for(int i=0;i<NativeShadows.PointCount;i++){
            int s=i*4,p=i*5;float x=NativeShadows.Sources[s],z=NativeShadows.Sources[s+1];
            float height=TerrainElevation.At(state.Town,x,z)+(NativeShadows.Sources[s+3]==210?1.3f:2.3f);
            var direct=CylinderWorld.Surface(z,state.Z,height);
            check(NativeShadows.PointsData[p]==x&&NativeShadows.PointsData[p+1]==direct.Y&&NativeShadows.PointsData[p+2]==direct.Z&&
                NativeShadows.PointsData[p+3]==NativeShadows.Sources[s+2]*1.65f&&NativeShadows.PointsData[p+4]==NativeShadows.Sources[s+3],
                "cached point upload exactly matches direct cylinder projection and original lighting attributes");
        }
    }
    internal static void Run(Action<bool,string> check)
    {
        var state=State();var lights=new TownLights();NativeShadows.ResetLighting();
        lights.Prepare(state,0);
        check(lights.Count>0&&NativeShadows.LayoutUploads==1&&NativeShadows.PointCalls==0&&lights.ProjectedSources==0,
            "cold daylight still publishes static pools and lamp props without projecting or uploading disabled model lights");
        for(int frame=0;frame<120;frame++){state.X+=.01f;state.Z+=.01f;lights.Prepare(state,0);}
        check(NativeShadows.LightCalls==121&&NativeShadows.Gain==0&&NativeShadows.LayoutUploads==1&&NativeShadows.PointCalls==0&&lights.ProjectedSources==0,
            "daylight camera movement preserves gain updates while avoiding every point transform and upload");
        lights.Prepare(state,123);int count=NativeShadows.SourceCount;
        check(count>0&&count<=19&&NativeShadows.PointCalls==1&&lights.ProjectedSources==count,
            "first lit frame prepares all current curved points before models can sample them");
        PointsMatch(state,check);
        int calls=NativeShadows.LightCalls;long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<120;frame++)lights.Prepare(state,frame%255+1);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"unchanged lit frames allocate no managed memory");
        check(NativeShadows.LightCalls==calls+120&&NativeShadows.Gain==120&&NativeShadows.LayoutUploads==1&&NativeShadows.PointCalls==1&&lights.ProjectedSources==count,
            "120 stationary lit frames avoid 120 point uploads and two trigonometric calls per source per frame while gain keeps changing");
        state.X+=.125f;lights.Prepare(state,87);
        check(NativeShadows.Gain==87&&NativeShadows.PointCalls==1&&lights.ProjectedSources==count,"east-west camera motion does not change cylinder light geometry");
        state.Z+=.25f;lights.Prepare(state,87);
        check(NativeShadows.PointCalls==2&&lights.ProjectedSources==count*2,"north-south motion projects and uploads exactly one new point set");
        PointsMatch(state,check);

        int layouts=NativeShadows.LayoutUploads,points=NativeShadows.PointCalls;
        int cell=(int)NativeShadows.Sources[1]*Town.Width+(int)NativeShadows.Sources[0];
        state.Town.Elevation[cell]=TerrainElevation.Block;state.Town.NotifyTerrainChanged();lights.Prepare(state,87);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1,"terrain edits invalidate source placement and point heights without camera movement");
        PointsMatch(state,check);
        layouts=NativeShadows.LayoutUploads;points=NativeShadows.PointCalls;
        state.Town.NotifyCastersChanged();lights.Prepare(state,87);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1,"changed tree/furnishing sockets invalidate static sources and model points together");
        PointsMatch(state,check);
        layouts=NativeShadows.LayoutUploads;points=NativeShadows.PointCalls;
        check(WorldLighting.StreetLamp(state,7,out float lampX,out float lampZ),"fixture has a street lamp beside the shop");
        var placed=state.Town.Placed[0];placed.Item=1;placed.Room=0;placed.X=(int)lampX;placed.Z=(int)lampZ;
        state.Town.PlacedCount=1;state.Town.NotifyObjectsChanged();lights.Prepare(state,87);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1&&
            WorldLighting.StreetLamp(state,7,out float shiftedX,out _)&&shiftedX!=lampX,
            "bulk placement changes invalidate walkable lamp sockets through the object revision without requiring a caster edit");
        PointsMatch(state,check);
        layouts=NativeShadows.LayoutUploads;points=NativeShadows.PointCalls;
        state.Town.HouseX++;lights.Prepare(state,87);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1,"moving an existing home invalidates its points independently of terrain revisions");
        PointsMatch(state,check);
        layouts=NativeShadows.LayoutUploads;points=NativeShadows.PointCalls;
        state.HousePlaced=false;lights.Prepare(state,87);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1,"removing a home republishes the reduced source and point lists");
        PointsMatch(state,check);
        layouts=NativeShadows.LayoutUploads;points=NativeShadows.PointCalls;
        state.BuildingTypes[1]=1;lights.Prepare(state,87);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1,"building definition changes invalidate sockets even when origins and existence stay fixed");
        PointsMatch(state,check);

        points=NativeShadows.PointCalls;int projected=lights.ProjectedSources;
        state.Space=1;state.X=state.Z=2;lights.Prepare(state,0);
        state.Town.HouseZ++;state.Town.NotifyTerrainChanged();lights.Prepare(state,0);
        check(NativeShadows.PointCalls==points&&lights.ProjectedSources==projected&&NativeShadows.Gain==0,
            "indoor zero-gain views and geometry edits do no disabled point work");
        state.Space=0;state.Z=82.5f;lights.Prepare(state,91);
        check(NativeShadows.PointCalls==points+1,"zero-to-lit transition publishes current geometry before local lighting returns");
        PointsMatch(state,check);
        points=NativeShadows.PointCalls;lights.Prepare(state,0);lights.Prepare(state,91);
        check(NativeShadows.PointCalls==points+1,"reactivation explicitly republishes points instead of relying on disabled native state");

        // A new town can have the same revision numbers and building layout.
        var first=State(73);var second=State(73);lights=new TownLights();NativeShadows.ResetLighting();
        lights.Prepare(first,120);points=NativeShadows.PointCalls;layouts=NativeShadows.LayoutUploads;
        check(first.Town.TerrainVersion==second.Town.TerrainVersion&&first.Town.ShadowVersion==second.Town.ShadowVersion,"town-switch fixture has equal revision counters");
        lights.Prepare(second,120);
        check(NativeShadows.LayoutUploads==layouts+1&&NativeShadows.PointCalls==points+1,"town identity invalidates otherwise identical cached revisions and view");
        PointsMatch(second,check);
        second.Z+=.5f;NativeShadows.FailPointsNext=true;bool rejected=false;
        try{lights.Prepare(second,120);}catch(InvalidOperationException){rejected=true;}
        points=NativeShadows.PointCalls;lights.Prepare(second,120);
        check(rejected&&NativeShadows.PointCalls==points+1,"failed native point publication is retried instead of cached as ready");
        PointsMatch(second,check);
        lights=new TownLights();NativeShadows.ResetLighting();NativeShadows.FailLightsNext=true;rejected=false;
        try{lights.Prepare(second,0);}catch(InvalidOperationException){rejected=true;}
        lights.Prepare(second,120);
        check(rejected&&NativeShadows.LightCalls==2&&NativeShadows.LayoutUploads==1&&NativeShadows.PointCalls==1,
            "fresh native-owner initialization retries failed layout publication with complete source data");
        PointsMatch(second,check);
        Console.WriteLine("Town lighting: "+count+" sources; 120 daylight + 120 stationary lit frames avoid "+(count*240)+" source projections / "+(count*480)+" trigonometric calls and 240 point uploads.");
    }
}
