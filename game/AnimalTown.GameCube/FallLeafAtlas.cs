using Dolphin.Graphics;

internal static class FallLeafAtlas
{
    private const int Width=32,Height=16,CellSize=16;

    // UVs at texel centers: willow U=0.5/32..15.5/32, maple U=16.5/32..31.5/32.
    // Both use V=0.5/16..15.5/16. Each cell has a transparent perimeter.
    internal static Texture2D Create()
    {
        var pixels=new byte[Width*Height];
        WriteLeaf(pixels,0,new ushort[]{
            0b0000000000000000,
            0b0000000000000000,
            0b0000000000001000,
            0b0000000000111000,
            0b0000000001111000,
            0b0000000111110000,
            0b0000001111100000,
            0b0000011111100000,
            0b0000111111000000,
            0b0000111110000000,
            0b0000111100000000,
            0b0000111000000000,
            0b0001100000000000,
            0b0010000000000000,
            0b0000000000000000,
            0b0000000000000000
        });
        WriteLeaf(pixels,CellSize,new ushort[]{
            0b0000000000000000,
            0b0000000000000000,
            0b0000000110000000,
            0b0000000110000000,
            0b0000001111000000,
            0b0010001111000100,
            0b0011011111101100,
            0b0011111111111100,
            0b0001111111111000,
            0b0111111111111110,
            0b0011111111111100,
            0b0000111111110000,
            0b0000011111100000,
            0b0000000110000000,
            0b0000000100000000,
            0b0000000000000000
        });
        var texture=Texture2D.CreateNative(Width,Height,NativeTextureFormat.Ia4,true);
        try{
            texture.ReplaceNative(pixels);
            texture.ConfigureSampler(TextureWrapMode.Clamp,TextureWrapMode.Clamp,TextureMinFilter.Linear,TextureMagFilter.Linear);
            return texture;
        }catch{
            texture.Dispose();
            throw;
        }
    }

    private static void WriteLeaf(byte[] pixels,int start,ushort[] rows)
    {
        for(int y=0;y<CellSize;y++)for(int x=0;x<CellSize;x++){
            int column=start+x;
            // GX IA4 uses 8×4 tiles and stores alpha above intensity.
            int address=((y/4)*(Width/8)+column/8)*32+(y%4)*8+column%8;
            bool covered=(rows[y]&(0x8000>>x))!=0;
            int vein=start==0?15-y:7;
            int intensity=x==vein?15:x>vein?13:12;
            // Keep intensity under the transparent border so linear filtering
            // carries the leaf tint to the edge without a dark outline.
            pixels[address]=(byte)((covered?0xf0:0)|intensity);
        }
    }
}
