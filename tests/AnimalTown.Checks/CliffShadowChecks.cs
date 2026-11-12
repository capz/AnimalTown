using AnimalTown;

internal static class CliffShadowChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var t=new Town(101);Array.Fill(t.Tiles,(byte)Terrain.Grass);Array.Clear(t.Elevation);
        void Block(int mode){Array.Clear(t.Elevation);for(int z=40;z<42;z++)for(int x=40;x<42;x++)t.Elevation[z*Town.Width+x]=(byte)mode;}
        Block(TerrainElevation.Block);
        check(CliffShadowMap.Blocked(t,42.25f,41.25f),"raised cliff casts toward south-east onto lower ground");
        check(!CliffShadowMap.Blocked(t,39.75f,41.25f),"upstream ground stays outside the cliff projection");
        check(!CliffShadowMap.Blocked(t,41.25f,41.25f),"a cliff does not shadow its own upper cap");
        check(!CliffShadowMap.Blocked(t,44.25f,41.25f),"cliff shadow reach is bounded by height and sun direction");
        foreach(int mode in new[]{6,7,8,9}){
            Block(mode);
            for(float z=39.125f;z<43.5f;z+=.25f)for(float x=39.125f;x<43.5f;x+=.25f){
                if(!CliffShadowMap.Blocked(t,x,z))continue;
                float h=TerrainElevation.At(t,x,z);bool oracle=false;
                for(int i=1;i<256;i++){
                    float rise=(2-h)*i/256,px=x-CliffShadowMap.SunX*rise,pz=z-CliffShadowMap.SunZ*rise;
                    if(TerrainElevation.At(t,px,pz)>h+rise+.02f){oracle=true;break;}
                }
                check(oracle,"corner shadow samples agree with a finer independent height-field ray");
            }
        }
        Block(TerrainElevation.Block);
        var map=new CliffShadowMap();var packed=new byte[CliffShadowMap.TextureSize*CliffShadowMap.TextureSize];
        void Bake(){
            Array.Clear(packed);map.Begin(t);int jobs=0;
            while(true){
                bool done=map.Advance();check(++jobs<4000,"cliff mask generation has a fixed town-sized work bound");
                if(map.ChunkReady){
                    check(map.Offset>=0&&map.Offset%32==0&&map.Offset+32<=packed.Length,"shadow chunks are aligned complete GX I8 tiles");
                    Array.Copy(map.Chunk,0,packed,map.Offset,32);
                }
                if(done)break;
            }
        }
        byte Pixel(int x,int z)=>packed[((z/4)*(CliffShadowMap.TextureSize/8)+x/8)*32+z%4*8+x%8];
        Bake();
        check(map.HasCoverage&&Pixel(85,83)==CliffShadowMap.Opacity,"packed mask contains the lower-ground cliff shadow");
        check(Pixel(83,83)==0&&Pixel(79,83)==0,"packing leaves the upper cap and sun-facing ground clear");
        foreach(byte alpha in packed)check(alpha<=CliffShadowMap.Opacity,"terrain coverage cannot exceed shared shadow opacity");
        t.Tiles[41*Town.Width+42]=(byte)Terrain.Sea;
        check(CliffShadowMap.Blocked(t,42.25f,41.25f),"opaque water can receive the cliff projection");
        Array.Clear(t.Elevation);Array.Fill(t.Tiles,(byte)Terrain.Grass);
        for(int z=40;z<42;z++)for(int x=40;x<42;x++)t.Tiles[z*Town.Width+x]=(byte)Terrain.CoastEast;
        check(!CliffShadowMap.Blocked(t,40.75f,41.25f)&&CliffShadowMap.Blocked(t,41.25f,41.25f),"coastal land and water halves keep independent receiver heights");
        Array.Fill(t.Tiles,(byte)Terrain.Grass);Bake();
        check(!map.HasCoverage&&packed.All(x=>x==0),"terrain removal rebuilds a completely clear mask");
        long before=GC.GetAllocatedBytesForCurrentThread();var compact=new CliffShadowMap();
        check(GC.GetAllocatedBytesForCurrentThread()-before<4096,"cliff candidates use a bounded bitmap rather than a town-sized byte grid");
        GC.KeepAlive(compact);
        // Compare rendered coverage with direct rays around both sides of a bit
        // word, the first cell and the last rows/columns. The oracle bypasses
        // candidate selection, so a lost bit would leave a visible mismatch.
        var blocks=new[]{(0,0),(31,16),(32,42),(159,158),(158,159)};
        foreach(var (x,z) in blocks)t.Elevation[z*Town.Width+x]=TerrainElevation.Block;
        Bake();
        foreach(var (bx,bz) in blocks){
            for(int z=Math.Max(0,bz*2-2);z<Math.Min(Town.Height*2,bz*2+7);z++)for(int x=Math.Max(0,bx*2-2);x<Math.Min(Town.Width*2,bx*2+9);x++){
                float wx=(x-.5f)/CliffShadowMap.TexelsPerMetre,wz=(z-.5f)/CliffShadowMap.TexelsPerMetre;int hits=0;
                foreach(float dz in new[]{-.125f,.125f})foreach(float dx in new[]{-.125f,.125f})if(CliffShadowMap.Blocked(t,wx+dx,wz+dz))hits++;
                check(Pixel(x,z)==hits*CliffShadowMap.Opacity/4,"bit-packed candidate selection retains exact edge and word-boundary mask bytes");
            }
        }
        Array.Clear(t.Elevation);Bake();
        check(!map.HasCoverage&&packed.All(x=>x==0),"reusing a candidate bitmap clears every word after terrain removal");
        check(!CliffShadowMap.Blocked(t,-.1f,1)&&!CliffShadowMap.Blocked(t,Town.Width,1),"outside-world sampling is safe");
    }
}
