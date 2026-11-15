using System.Buffers.Binary;
using AnimalTown;

internal static class TownTrimChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(Town.Cells*5==160*160*3,"new town has exactly sixty percent of the previous area");
        var state=new GameState(7319){X=Town.Width-.25f,Z=Town.Height-.5f};
        state.Town.SetTerrain(Town.Width-2,Town.Height-2,Terrain.Camp);
        byte[] current=SaveCodec.Encode(state);var loaded=SaveCodec.Decode(current);
        check(Int(current,24)==128&&Int(current,28)==120,"save records the compact rectangular dimensions");
        check(current.SequenceEqual(SaveCodec.Encode(loaded)),"compact save is stable on repeated loading and saving");
        check(loaded.X==state.X&&loaded.Z==state.Z&&loaded.Town.At(Town.Width-2,Town.Height-2)==(int)Terrain.Camp,"last-cell positions and edge edits survive saving");
        check(!loaded.Town.Inside(Town.Width,Town.Height-1)&&!loaded.Town.Inside(Town.Width-1,Town.Height),"rectangular town excludes both outside edges");
        check(!loaded.Town.SetTerrain(Town.Width,0,Terrain.Grass)&&!loaded.Town.Walkable(0,Town.Height),"editing and traversal respect the compact bounds");
        foreach(int version in new[]{1,2,3})Reject(current,4,version,"implicit old town dimensions are unsupported");
        foreach(int version in new[]{4,5,6,7,8}){
            byte[] old=(byte[])current.Clone();Put(old,4,version);
            Reject(old,24,160,"old square width is rejected without cropping");
            Reject(old,28,160,"old square height is rejected without cropping");
        }
        Reject(current,32+28*4,Town.Width*1024,"player X beyond new bounds is rejected");
        Reject(current,32+29*4,Town.Height*1024,"player Z beyond new bounds is rejected");
        foreach(string fixture in new[]{"town-20x20-v3.bin","town-20x20-outskirts-v3.bin"}){
            byte[] old=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures",fixture));
            check(Refuses(()=>SaveCodec.Decode(old)),"historic larger town fixture is rejected without migration");
        }
        foreach(bool legacy in new[]{false,true}){
            var parts=legacy?SplitSaveCodec.EncodeLegacy(state):SplitSaveCodec.Encode(state);
            var split=SplitSaveCodec.Decode(parts);
            check(split.Town.Tiles.SequenceEqual(state.Town.Tiles)&&split.X==state.X&&split.Z==state.Z,"split compact town retains edge edits and positions");
            foreach(int offset in new[]{28,32}){
                var changed=(byte[][])parts.Clone();changed[0]=(byte[])parts[0].Clone();Put(changed[0],offset,160);
                Put(changed[0],changed[0].Length-4,(int)SaveCodec.Crc(changed[0],0,changed[0].Length-4));
                check(Refuses(()=>SplitSaveCodec.Decode(changed)),"split old dimensions are rejected without conversion");
            }
        }
        Console.WriteLine("Compact town: sixty percent area, rectangular edges, save round trips and old-size rejection passed");

        void Reject(byte[] source,int offset,int value,string message){byte[] bytes=(byte[])source.Clone();Put(bytes,offset,value);Put(bytes,20,(int)SaveCodec.Crc(bytes,24,bytes.Length-24));check(Refuses(()=>SaveCodec.Decode(bytes)),message);}
    }
    private static bool Refuses(Action read){try{read();return false;}catch(InvalidOperationException){return true;}}
    private static int Int(byte[] bytes,int at)=>BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static void Put(byte[] bytes,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at),value);
}
