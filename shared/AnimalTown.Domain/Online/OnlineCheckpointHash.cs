namespace AnimalTown.Online;

// Incremental SHA-256 for the console's bounded checkpoint cursor. No full
// envelope, runtime cryptography provider or per-block allocation is needed.
public sealed class OnlineCheckpointHash : IDisposable
{
    static readonly uint[] constants={
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
    readonly uint[] state={0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    readonly uint[] words=new uint[64];readonly byte[] block=new byte[64];
    int used;long length;bool finished;
    public void Append(byte[] bytes,int offset,int count)
    {
        if(finished)throw new InvalidOperationException("The checkpoint hash is complete.");
        if(offset<0||count<0||offset>bytes.Length-count)throw new ArgumentOutOfRangeException(nameof(count));
        if(count>SaveCodec.MaximumBytes-length)throw new InvalidOperationException("The checkpoint exceeds its format bound.");
        length+=count;
        while(count>0){int take=Math.Min(count,64-used);Array.Copy(bytes,offset,block,used,take);offset+=take;count-=take;used+=take;if(used==64){Transform();used=0;}}
    }
    static uint Rotate(uint v,int n)=>(v>>n)|(v<<(32-n));
    void Transform()
    {unchecked{
        for(int i=0;i<16;i++){int p=i*4;words[i]=(uint)block[p]<<24|(uint)block[p+1]<<16|(uint)block[p+2]<<8|block[p+3];}
        for(int i=16;i<64;i++){uint a=words[i-15],b=words[i-2];words[i]=words[i-16]+(Rotate(a,7)^Rotate(a,18)^(a>>3))+words[i-7]+(Rotate(b,17)^Rotate(b,19)^(b>>10));}
        uint a0=state[0],b0=state[1],c=state[2],d=state[3],e=state[4],f=state[5],g=state[6],h=state[7];
        for(int i=0;i<64;i++)
        {uint t=h+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+constants[i]+words[i];uint s=(Rotate(a0,2)^Rotate(a0,13)^Rotate(a0,22))+((a0&b0)^(a0&c)^(b0&c));h=g;g=f;f=e;e=d+t;d=c;c=b0;b0=a0;a0=t+s;}
        state[0]+=a0;state[1]+=b0;state[2]+=c;state[3]+=d;state[4]+=e;state[5]+=f;state[6]+=g;state[7]+=h;
    }}
    public string Complete()
    {
        if(finished)throw new InvalidOperationException("The checkpoint hash is complete.");
        ulong bits=unchecked((ulong)length*8);block[used++]=128;
        if(used>56){Array.Clear(block,used,64-used);Transform();used=0;}
        Array.Clear(block,used,56-used);for(int i=0;i<8;i++)block[63-i]=(byte)(bits>>(8*i));Transform();
        const string digits="0123456789ABCDEF";var text=new char[64];
        for(int i=0;i<8;i++)for(int j=0;j<8;j++)text[i*8+j]=digits[(int)(state[i]>>(28-j*4))&15];
        Dispose();return new string(text);
    }
    public void Dispose(){finished=true;Array.Clear(block,0,block.Length);Array.Clear(words,0,words.Length);Array.Clear(state,0,state.Length);}
}
