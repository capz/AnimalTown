using AnimalTown;

internal static class RiverCoastChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(7319);var t=s.Town;
        Array.Fill(t.Tiles,(byte)Terrain.Grass);Array.Clear(t.Elevation);
        t.SetTerrain(80,80,Terrain.Water);t.SetTerrain(81,80,Terrain.Water);t.SetTerrain(80,81,Terrain.Sea);
        check(TerrainElevation.AtCell(t,80,80,80,80)==-.5f,"visible river and bed drop exactly half a metre");
        check(TerrainElevation.AtCell(t,80,81,80,81)==WaterTerrain.RiverHeight,"sea and river share the same half-metre water plane");
        check(TerrainElevation.AtCell(t,80,81,80,81)==TerrainElevation.AtCell(t,80,80,80,81),"river mouth has no artificial water step or connecting wall");
        check(!WaterSurface.IsBank(t,81,80)&&WaterSurface.IsBank(t,79,80),"adjacent water emits no internal bank; dry shore does");
        t.SetTerrain(79,80,Terrain.Bridge);check(!WaterSurface.IsBank(t,79,80)&&t.Walkable(79,80)&&TerrainElevation.At(t,79.5f,80.5f)==0,"bridge approach stays on local land plane");
        check(WaterSurface.HeightAt(t,79,80,79,80)==-.5f,"lowered river water continues beneath bridge decks without a sky gap");
        foreach(var kind in new[]{Terrain.CoastNorth,Terrain.CoastEast,Terrain.CoastSouth,Terrain.CoastWest}){
            int wet=0,dry=0;
            for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++){
                int x=90+dx,z=90+dz;t.SetTerrain(x,z,kind);
                bool sea=WaterTerrain.CoastWater((int)kind,x,z);if(sea)wet++;else dry++;
                check(WaterSurface.IsWater(t,x,z)==sea,"coast orientation splits horizontal footprint");
                check(TerrainElevation.AtCell(t,x,z,x,z)==(sea?WaterSurface.Height:2),"half section retains two metre land cliff and sea plane");
                check(!t.Walkable(x,z),"coastal cliff footprint excludes ordinary traversal");
            }
            check(wet==2&&dry==2,"each two metre coast section is exactly half sea");
            check(SaveCodec.Decode(SaveCodec.Encode(s)).Town.At(90,90)==(int)kind,"new coastal ID survives combined save");
            check(SplitSaveCodec.Decode(SplitSaveCodec.Encode(s)).Town.At(90,90)==(int)kind,"new coastal ID survives split save");
        }
        // Lower ocean level must not leave players/NPCs floating over beaches.
        Array.Fill(t.Tiles,(byte)Terrain.Beach);
        for(int mask=0;mask<16;mask++){
            t.SetTerrain(81,80,(mask&1)!=0?Terrain.Sea:Terrain.Beach);t.SetTerrain(80,81,(mask&2)!=0?Terrain.Sea:Terrain.Beach);
            t.SetTerrain(79,80,(mask&4)!=0?Terrain.Sea:Terrain.Beach);t.SetTerrain(80,79,(mask&8)!=0?Terrain.Sea:Terrain.Beach);
            for(int iz=0;iz<=8;iz++)for(int ix=0;ix<=8;ix++){
                float u=ix/8f,v=iz/8f;
                // Boundary coordinates belong to the neighboring cell in At;
                // use the beach cell explicitly for the rendered edge tests.
                float h=TerrainElevation.AtCell(t,80,80,80+u,80+v);
                check(h>=WaterSurface.Height&&h<=0,"all shoreline support stays between beach and shared water levels");
                check(h==GroundSurface.HeightAt(t,80,80,80+u,80+v),"beach rendering and player/NPC support share the same triangles");
                if(mask==1)check(Math.Abs(h-WaterSurface.Height*u)<.00001f,"east shoreline support follows its actual sloped quad");
                if(mask==2)check(Math.Abs(h-WaterSurface.Height*v)<.00001f,"south shoreline support follows its actual sloped quad");
                if(mask==3)check(Math.Abs(h-WaterSurface.Height*Math.Max(u,v))<.00001f,"corner beach uses triangle interpolation rather than a bilinear floating patch");
            }
            check(TerrainElevation.Traverse(t,80.1f,80.1f,80.9f,80.9f),"sloping dry beach remains traversable for player and shared villager navigation");
            check(!TerrainElevation.Traverse(t,80.5f,80.5f,81.5f,80.5f)||(mask&1)==0,"wet edge remains untraversable");
        }
        Array.Fill(t.Tiles,(byte)Terrain.Beach);t.SetTerrain(81,80,Terrain.Sea);
        float edge=TerrainElevation.AtCell(t,80,80,81,81),corner=TerrainElevation.AtCell(t,80,81,81,81);
        check(edge==corner&&edge==WaterSurface.Height,"diagonally touching sea shares one wet corner across adjacent beach meshes");
        check(Math.Abs(TerrainElevation.At(t,80.75f,81.25f)-WaterSurface.Height*.5f)<.00001f,"diagonal shoreline has continuous support across its closing beach triangle");
        s.Town.Tiles[0]=13;
        bool rejected=false;try{SaveCodec.Decode(SaveCodec.Encode(s));}catch(InvalidOperationException){rejected=true;}
        check(rejected,"current save rejects unknown terrain IDs");s.Town.Tiles[0]=0;
        var legacyWithCoast=SaveCodec.Encode(s);legacyWithCoast[4]=5;
        rejected=false;try{SaveCodec.Decode(legacyWithCoast);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"v5 cannot smuggle new coastal terrain IDs");
        // Recreate v5/v1 wire records with only historical IDs. Edits, player
        // progress, buildings and object records must survive both migrations.
        var old=new GameState(7319){Name="Estuary",Wallet=1234,Tutorial=4};
        for(int i=0;i<Town.Cells;i++)if(old.Town.Tiles[i]>7)old.Town.Tiles[i]=(byte)(old.Town.Tiles[i]==8?Terrain.Water:Terrain.Beach);
        old.Town.SetTerrain(20,140,Terrain.Plaza);old.Town.SetTerrain(80,120,Terrain.Water);old.Town.SetTerrain(80,140,Terrain.Water);
        var expected=(byte[])old.Town.Tiles.Clone();var elevation=(byte[])old.Town.Elevation.Clone();
        void Put(byte[] bytes,int at,int value){for(int i=0;i<4;i++)bytes[at+i]=(byte)(value>>(8*i));}
        var combined=SaveCodec.Encode(old);Array.Resize(ref combined,combined.Length-BuildingSave.FieldCount*4);Put(combined,4,5);Put(combined,16,combined.Length);Put(combined,20,(int)SaveCodec.Crc(combined,24,combined.Length-24));
        var a=SaveCodec.Decode(combined);
        var parts=SplitSaveCodec.Encode(old);var historicalPlayer=new byte[parts[3].Length-BuildingSave.FieldCount*4];Array.Copy(parts[3],historicalPlayer,historicalPlayer.Length-4);parts[3]=historicalPlayer;Put(parts[3],24,parts[3].Length);foreach(var part in parts){Put(part,4,1);Put(part,part.Length-4,(int)SaveCodec.Crc(part,0,part.Length-4));}
        var b=SplitSaveCodec.Decode(parts);
        foreach(var loaded in new[]{a,b}){
            for(int i=0;i<Town.Cells;i++)check(loaded.Town.Tiles[i]==(expected[i]==1&&i/Town.Width>=WaterTerrain.SeaStart?8:expected[i]),"legacy migration only relabels southern water, never regenerates terrain");
            check(loaded.Town.At(80,120)==1&&loaded.Town.At(80,140)==8,"connected river remains river above authored mouth");
            check(loaded.Wallet==1234&&loaded.Tutorial==4&&loaded.Name=="Estuary"&&loaded.Town.GroundCount==old.Town.GroundCount&&loaded.Town.Elevation.SequenceEqual(elevation),"migration preserves progress, possessions and edited elevation");
            check(SaveCodec.Encode(loaded).SequenceEqual(SaveCodec.Encode(SaveCodec.Decode(SaveCodec.Encode(loaded)))),"migration becomes stable after first write");
        }
        for(int seed=0;seed<16;seed++){
            var town=new Town(seed);int coast=0;
            for(int z=1;z<Town.Height-1;z++)for(int x=1;x<Town.Width-1;x++)if(WaterTerrain.IsSea(town,x,z)){
                if(WaterTerrain.IsCoast(town.At(x,z)))coast++;
                for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0),kind=town.At(nx,nz);
                    check(WaterSurface.IsWater(town,nx,nz)||kind==(int)Terrain.Beach||WaterTerrain.IsCoast(kind),"generated sea borders only water, beach or facing land cliff");}
            }
            check(coast>0,"new towns include half-footprint coastal soil headlands");
        }
        Console.WriteLine("River/coast levels, footprint orientations, shore adjacency and v5/v1 migrations passed");
    }
}
