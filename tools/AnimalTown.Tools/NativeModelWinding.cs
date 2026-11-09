using System.Buffers.Binary;

// Normalize cooked DNMD faces to the same clockwise GX convention as generated
// terrain. Outward authored normals choose the face direction, including mixed
// winding within one mesh. Never change source GLBs, vertices, UVs, skins or clips.
internal static class NativeModelWinding
{
    internal static void ConvertFile(string path)
    {var bytes=File.ReadAllBytes(path);Normalize(bytes);if(Normalize(bytes)!=0)throw new InvalidDataException("Unstable model winding");File.WriteAllBytes(path,bytes);}
    internal static int Normalize(byte[] bytes)
    {
        int Read(int at)=>checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at,4)));
        float Float(int at)=>BitConverter.Int32BitsToSingle(unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at,4))));
        void Put(int at,int value)=>BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at,4),(uint)value);
        uint Hash(int at,int length){uint h=2166136261;for(int i=at;i<at+length;i++)h=unchecked((h^bytes[i])*16777619);return h;}
        if(bytes.Length<160||Read(0)!=0x444e444d||Read(8)!=bytes.Length||Read(12)!=6)throw new InvalidDataException("Invalid model package");
        const int directory=32;int start=Read(directory+4),length=Read(directory+8);
        if(start<160||length<4||start>bytes.Length-length||BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(directory+12,4))!=Hash(start,length))throw new InvalidDataException("Invalid model mesh section");
        int count=Read(start),at=start+4,changed=0;if(count<1||count>4096)throw new InvalidDataException("Model mesh count");
        for(int mesh=0;mesh<count;mesh++){
            if(at>start+length-44)throw new InvalidDataException("Truncated model mesh");
            int vertices=Read(at+12),indices=Read(at+16);if(vertices<1||vertices>65535||indices<3||indices>196605||indices%3!=0)throw new InvalidDataException("Model mesh bounds");
            int data=at+44;at=checked(data+vertices*80);if(at>start+length-indices*4)throw new InvalidDataException("Truncated model indices");
            for(int i=0;i<indices;i+=3){
                int ia=Read(at+i*4),ib=Read(at+(i+1)*4),ic=Read(at+(i+2)*4);
                if((uint)ia>=vertices||(uint)ib>=vertices||(uint)ic>=vertices)throw new InvalidDataException("Model vertex index");
                int a=data+ia*80,b=data+ib*80,c=data+ic*80;
                double ux=(double)Float(b)-Float(a),uy=(double)Float(b+4)-Float(a+4),uz=(double)Float(b+8)-Float(a+8);
                double vx=(double)Float(c)-Float(a),vy=(double)Float(c+4)-Float(a+4),vz=(double)Float(c+8)-Float(a+8);
                double nx=(double)Float(a+12)+Float(b+12)+Float(c+12),ny=(double)Float(a+16)+Float(b+16)+Float(c+16),nz=(double)Float(a+20)+Float(b+20)+Float(c+20);
                double dot=(uy*vz-uz*vy)*nx+(uz*vx-ux*vz)*ny+(ux*vy-uy*vx)*nz;
                if(!double.IsFinite(dot))throw new InvalidDataException("Non-finite model triangle");
                if(dot>0){Put(at+(i+1)*4,ic);Put(at+(i+2)*4,ib);changed++;}
            }
            at+=indices*4;
        }
        if(at!=start+length)throw new InvalidDataException("Model mesh trailing bytes");
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(directory+12,4),Hash(start,length));return changed;
    }
}
