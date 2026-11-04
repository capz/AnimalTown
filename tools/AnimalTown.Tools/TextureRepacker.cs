using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DolphinDotNet.Imaging;

// Offline GLB editing only. Geometry, skin weights and animation are copied
// verbatim; vertices are split only where a new UV seam requires it.
internal sealed class TextureRepacker
{
    sealed class Patch
    {
        public int Id,X,Y,W,H,OutX,OutY,OutW,OutH;
        public Rgba32? Solid;
        public Patch? Master;
        public int StackTurn;
        public bool Rotated;
        public readonly List<Triangle> Triangles=new();
    }
    sealed class Triangle
    {
        public required Primitive Owner;
        public required int[] Vertices;
        public required Vector2[] Uv;
        public Patch Patch=null!;
    }
    sealed class Primitive
    {
        public required JsonObject Json;
        public required Dictionary<string,int> Attributes;
        public readonly List<Triangle> Triangles=new();
    }
    readonly JsonObject json;
    readonly MemoryStream bin=new();
    readonly List<Primitive> primitives=new();
    readonly List<object> reports=new();
    readonly int? size;
    readonly double maximumRmse;
    const int Gutter=2; // Two base texels protect base + one half-size mip.
    int merged,triangles;
    TextureRepacker(string input,int? size,double maximumRmse)
    {
        byte[] b=File.ReadAllBytes(input);
        if(b.Length<28||U(b,0)!=0x46546c67||U(b,4)!=2||U(b,8)!=b.Length||U(b,16)!=0x4e4f534a)throw new InvalidDataException("Expected GLB v2");
        int n=checked((int)U(b,12)),start=20+n;
        if(start+8>b.Length||U(b,start+4)!=0x004e4942||start+8+(long)U(b,start)!=b.Length)throw new InvalidDataException("Expected one embedded BIN chunk");
        json=JsonNode.Parse(b.AsSpan(20,n))!.AsObject();
        if(json["buffers"]!.AsArray().Count!=1||json["buffers"]![0]!["uri"]!=null)throw new InvalidDataException("Only embedded single-buffer GLBs are supported");
        int declared=I(json["buffers"]![0]!,"byteLength"),chunk=checked((int)U(b,start));
        if(declared<0||declared>chunk||chunk-declared>3)throw new InvalidDataException("Invalid declared BIN length");
        foreach(var v in Array("bufferViews"))if(I(v!,"buffer")!=0||I(v!,"byteOffset")<0||I(v!,"byteLength")<0||I(v!,"byteOffset")+(long)I(v!,"byteLength")>declared)throw new InvalidDataException("Buffer view outside declared BIN");
        if(json["extensionsUsed"] is JsonArray ex&&ex.Count!=0)throw new InvalidDataException("Repacker does not silently approximate glTF extensions");
        bin.Write(b,start+8,checked((int)U(b,start)));this.size=size;this.maximumRmse=maximumRmse;
    }
    static uint U(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at,4));
    static int I(JsonNode n,string key,int fallback=0)=>(int?)n[key]??fallback;
    JsonArray Array(string name)=>json[name]!.AsArray();
    int AppendView(byte[] bytes)
    {
        while(bin.Length%4!=0)bin.WriteByte(0);
        int offset=checked((int)bin.Length);bin.Write(bytes);
        var views=Array("bufferViews");int index=views.Count;
        views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=offset,["byteLength"]=bytes.Length});return index;
    }
    byte[] View(int index)
    {
        var v=Array("bufferViews")[index]!;if(I(v,"buffer")!=0)throw new InvalidDataException("External buffer");
        int start=I(v,"byteOffset"),length=I(v,"byteLength");if(start<0||length<0||start+(long)length>bin.Length)throw new InvalidDataException("Invalid buffer view");
        return bin.GetBuffer().AsSpan(start,length).ToArray();
    }
    static int Components(string type)=>type switch{"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4"=>4,"MAT4"=>16,_=>throw new InvalidDataException("Unsupported accessor shape")};
    static int Bytes(int type)=>type switch{5120 or 5121=>1,5122 or 5123=>2,5125 or 5126=>4,_=>throw new InvalidDataException("Invalid accessor component")};
    byte[] Accessor(int index)
    {
        var a=Array("accessors")[index]!;if(a["sparse"]!=null||a["bufferView"]==null)throw new InvalidDataException("Sparse/implicit accessors are unsupported");
        int view=I(a,"bufferView"),element=Components((string)a["type"]!)*Bytes(I(a,"componentType")),count=I(a,"count"),offset=I(a,"byteOffset"),stride=I(Array("bufferViews")[view]!,"byteStride",element);
        byte[] source=View(view);if(count<0||offset<0||stride<element||offset+(long)Math.Max(0,count-1)*stride+element>source.Length)throw new InvalidDataException("Accessor range");
        var result=new byte[checked(count*element)];for(int k=0;k<count;k++)Buffer.BlockCopy(source,offset+k*stride,result,k*element,element);return result;
    }
    Vector2[] ReadUv(int accessor)
    {
        var a=Array("accessors")[accessor]!;if((string)a["type"]! !="VEC2")throw new InvalidDataException("UV accessor must be VEC2");
        int type=I(a,"componentType"),bytes=Bytes(type);bool normalized=(bool?)a["normalized"]??false;
        var data=Accessor(accessor);var result=new Vector2[I(a,"count")];
        float Read(int at)=>type==5126?BitConverter.ToSingle(data,at):normalized&&type==5123?BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at,2))/65535f:normalized&&type==5121?data[at]/255f:throw new InvalidDataException("Unsupported UV component type");
        for(int k=0;k<result.Length;k++){var uv=new Vector2(Read(k*bytes*2),Read(k*bytes*2+bytes));if(!float.IsFinite(uv.X)||!float.IsFinite(uv.Y)||uv.X<0||uv.X>1||uv.Y<0||uv.Y>1)throw new InvalidDataException("Repeated/out-of-range UVs require an authored unwrap before repacking");result[k]=uv;}return result;
    }
    int[] Indices(JsonObject primitive,int count)
    {
        if(primitive["indices"]==null)return Enumerable.Range(0,count).ToArray();
        int accessor=I(primitive,"indices"),type=I(Array("accessors")[accessor]!,"componentType");var data=Accessor(accessor);int bytes=Bytes(type);var result=new int[data.Length/bytes];
        for(int i=0;i<result.Length;i++)result[i]=type switch{5121=>data[i],5123=>BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i*2,2)),5125=>checked((int)U(data,i*4)),_=>throw new InvalidDataException("Invalid index type")};
        if(result.Any(v=>v<0||v>=count))throw new InvalidDataException("Index out of range");return result;
    }
    void Run()
    {
        var byImage=new SortedDictionary<int,List<Triangle>>();
        foreach(var mesh in Array("meshes"))foreach(var entry in mesh!["primitives"]!.AsArray())
        {
            var p=entry!.AsObject();if(I(p,"mode",4)!=4||p["targets"]!=null||p["extensions"]!=null)throw new InvalidDataException("Repacking requires triangle lists without morph targets or primitive extensions");
            if(p["material"]==null)continue;var material=Array("materials")[I(p,"material")]!;
            if(material["normalTexture"]!=null||material["occlusionTexture"]!=null||material["emissiveTexture"]!=null||material["pbrMetallicRoughness"]?["metallicRoughnessTexture"]!=null||material["extensions"]!=null)throw new InvalidDataException("Repacking currently requires base-colour-only materials; secondary maps are never discarded");
            var texture=material["pbrMetallicRoughness"]?["baseColorTexture"];if(texture==null)continue;
            if(texture["extensions"]!=null||I(texture,"texCoord")!=0)throw new InvalidDataException("Repacking currently requires untransformed TEXCOORD_0");
            int image=I(Array("textures")[I(texture,"index")]!,"source",-1);if(image<0)throw new InvalidDataException("Missing source image");
            var attrs=p["attributes"]!.AsObject().ToDictionary(k=>k.Key,k=>(int)k.Value!);
            if(!attrs.TryGetValue("TEXCOORD_0",out int uvAccessor))throw new InvalidDataException("Missing UVs");
            var uv=ReadUv(uvAccessor);int[] indices=Indices(p,uv.Length);if(indices.Length%3!=0)throw new InvalidDataException("Incomplete triangle");
            var primitive=new Primitive{Json=p,Attributes=attrs};primitives.Add(primitive);
            if(!byImage.TryGetValue(image,out var list))byImage.Add(image,list=new());
            for(int k=0;k<indices.Length;k+=3){int[] corners=indices.AsSpan(k,3).ToArray();var t=new Triangle{Owner=primitive,Vertices=corners,Uv=corners.Select(v=>uv[v]).ToArray()};primitive.Triangles.Add(t);list.Add(t);triangles++;}
        }
        if(byImage.Count==0)throw new InvalidDataException("No supported textured triangles found");
        foreach(var image in byImage)Repack(image.Key,image.Value);
        foreach(var p in primitives)Rewrite(p);
    }
    static (int x,int y,int w,int h) Bounds(IEnumerable<Triangle> triangles,int width,int height)
    {
        var uv=triangles.SelectMany(t=>t.Uv).ToArray();int x=Math.Clamp((int)Math.Floor(uv.Min(v=>v.X)*width-.5f),0,width-1),y=Math.Clamp((int)Math.Floor(uv.Min(v=>v.Y)*height-.5f),0,height-1);
        int right=Math.Clamp((int)Math.Ceiling(uv.Max(v=>v.X)*width+.5f),x+1,width),bottom=Math.Clamp((int)Math.Ceiling(uv.Max(v=>v.Y)*height+.5f),y+1,height);return(x,y,right-x,bottom-y);
    }
    static Rgba32? Solid(RasterImage image,(int x,int y,int w,int h) b)
    {
        var first=image[b.x,b.y];for(int y=b.y;y<b.y+b.h;y++)for(int x=b.x;x<b.x+b.w;x++)if(image[x,y]!=first)return null;return first;
    }
    void Repack(int index,List<Triangle> list)
    {
        var image=Array("images")[index]!;if(image["bufferView"]==null)throw new InvalidDataException("Images must be embedded");
        using var source=RasterImage.Load(View(I(image,"bufferView")));
        var sourceSamplers=Array("textures").Where(t=>I(t!,"source",-1)==index).Select(t=>t!["sampler"]==null?null:Array("samplers")[I(t,"sampler")]).ToArray();
        var wraps=sourceSamplers.Select(s=>(s==null?10497:I(s,"wrapS",10497),s==null?10497:I(s,"wrapT",10497))).Distinct().ToArray();
        if(wraps.Length!=1)throw new InvalidDataException("A shared image with different sampler wrap modes must be split before repacking");
        var wrap=wraps[0];if(new[]{wrap.Item1,wrap.Item2}.Any(v=>v!=10497&&v!=33071&&v!=33648))throw new InvalidDataException("Invalid wrap mode");
        var filters=sourceSamplers.Select(s=>s==null?9729:I(s,"magFilter",9729)).Distinct().ToArray();
        if(filters.Length!=1||(filters[0]!=9728&&filters[0]!=9729))throw new InvalidDataException("A shared image needs one valid magnification filter");bool nearest=filters[0]==9728;
        int width=size??source.Width,height=size??source.Height;
        if(width<16||height<16||width>4096||height>4096)throw new InvalidDataException("Atlas dimensions must be 16..4096");
        var patches=new List<Patch>();var solids=new Dictionary<Rgba32,Patch>();var remaining=new List<Triangle>();int solidTriangles=0;
        foreach(var t in list){bool border=t.Uv.Any(v=>v.X<.5f/source.Width||v.X>1-.5f/source.Width||v.Y<.5f/source.Height||v.Y>1-.5f/source.Height);var color=border?null:Solid(source,Bounds(new[]{t},source.Width,source.Height));if(color==null){remaining.Add(t);continue;}
            if(!solids.TryGetValue(color.Value,out var patch)){var bounds=Bounds(new[]{t},source.Width,source.Height);patch=new Patch{Id=patches.Count,Solid=color,X=bounds.x,Y=bounds.y,W=1,H=1};solids.Add(color.Value,patch);patches.Add(patch);}else merged++;
            patch.Triangles.Add(t);t.Patch=patch;solidTriangles++;
        }
        // Connected UV charts retain their relative layout, including shared
        // edges. Exact colour triangles alone may collapse to a shared texel.
        int[] parent=Enumerable.Range(0,remaining.Count).ToArray();int Find(int a){while(parent[a]!=a){parent[a]=parent[parent[a]];a=parent[a];}return a;}
        var vertices=new Dictionary<Vector2,int>();for(int k=0;k<remaining.Count;k++)foreach(var v in remaining[k].Uv){if(vertices.TryGetValue(v,out int other))parent[Find(k)]=Find(other);else vertices.Add(v,k);}
        foreach(var chart in Enumerable.Range(0,remaining.Count).GroupBy(Find)){
            var ts=chart.Select(k=>remaining[k]).ToList();var b=Bounds(ts,source.Width,source.Height);var p=new Patch{Id=patches.Count,X=b.x,Y=b.y,W=b.w,H=b.h};p.Triangles.AddRange(ts);foreach(var t in ts)t.Patch=p;patches.Add(p);
        }
        int sharedCharts=0,rotatedStacks=0;
        var fingerprints=new Dictionary<string,List<(Patch patch,byte[] pixels)>>();
        foreach(var p in patches.Where(p=>p.Solid==null)){
            bool found=false;
            for(int turn=0;turn<4&&!found;turn++){
                int rw=turn%2==0?p.W:p.H,rh=turn%2==0?p.H:p.W;var pixels=new byte[(rw+4)*(rh+4)*4];
                for(int y=-2;y<rh+2;y++)for(int x=-2;x<rw+2;x++){
                    int sx=turn switch{0=>x,1=>y,2=>p.W-1-x,_=>p.W-1-y},sy=turn switch{0=>y,1=>p.H-1-x,2=>p.H-1-y,_=>x};
                    var pixel=source[Address(p.X+sx,source.Width,wrap.Item1),Address(p.Y+sy,source.Height,wrap.Item2)];int at=((y+2)*(rw+4)+x+2)*4;pixels[at]=pixel.R;pixels[at+1]=pixel.G;pixels[at+2]=pixel.B;pixels[at+3]=pixel.A;
                }
                string key=rw+":"+rh+":"+Convert.ToHexString(SHA256.HashData(pixels));
                if(fingerprints.TryGetValue(key,out var matches))foreach(var match in matches)if(pixels.AsSpan().SequenceEqual(match.pixels)){p.Master=match.patch;p.StackTurn=turn;sharedCharts++;if(turn!=0)rotatedStacks++;found=true;break;}
            }
            if(!found){var pixels=new byte[(p.W+4)*(p.H+4)*4];for(int y=-2;y<p.H+2;y++)for(int x=-2;x<p.W+2;x++){var pixel=source[Address(p.X+x,source.Width,wrap.Item1),Address(p.Y+y,source.Height,wrap.Item2)];int at=((y+2)*(p.W+4)+x+2)*4;pixels[at]=pixel.R;pixels[at+1]=pixel.G;pixels[at+2]=pixel.B;pixels[at+3]=pixel.A;}
                string key=p.W+":"+p.H+":"+Convert.ToHexString(SHA256.HashData(pixels));if(!fingerprints.TryGetValue(key,out var entries))fingerprints.Add(key,entries=new());entries.Add((p,pixels));}
        }
        var packedPatches=patches.Where(p=>p.Master==null).ToList();
        float scale=1;bool fit=Pack(packedPatches,width,height,scale),preserveLayout=!fit&&size==null;
        if(!fit&&!preserveLayout){
            float low=0,high=1;for(int k=0;k<24;k++){float mid=(low+high)/2;if(Pack(packedPatches,width,height,mid))low=mid;else high=mid;}scale=low;if(scale==0||!Pack(packedPatches,width,height,scale))throw new InvalidDataException("Atlas too small for protected chart gutters");}
        using var output=new RasterImage(width,height);
        if(preserveLayout){for(int y=0;y<height;y++)for(int x=0;x<width;x++)output[x,y]=source[x,y];foreach(var p in packedPatches){p.OutX=p.X;p.OutY=p.Y;p.OutW=p.W;p.OutH=p.H;p.Rotated=false;}}
        if(!preserveLayout)
        foreach(var p in packedPatches){
            for(int y=-Gutter;y<p.OutH+Gutter;y++)for(int x=-Gutter;x<p.OutW+Gutter;x++)output[p.OutX+x,p.OutY+y]=p.Solid??(p.Rotated?Area(source,p.X+y*(double)p.W/p.OutH,p.Y+(p.OutW-x-1)*(double)p.H/p.OutW,p.W/(double)p.OutH,p.H/(double)p.OutW,wrap.Item1,wrap.Item2):Area(source,p.X+x*(double)p.W/p.OutW,p.Y+y*(double)p.H/p.OutH,p.W/(double)p.OutW,p.H/(double)p.OutH,wrap.Item1,wrap.Item2));
        }
        Vector2 Map(Triangle t,Vector2 uv){var p=t.Patch;if(p.Solid!=null)return new Vector2((p.OutX+.5f)/width,(p.OutY+.5f)/height);float x=(uv.X*source.Width-p.X)/p.W,y=(uv.Y*source.Height-p.Y)/p.H;for(int k=0;k<p.StackTurn;k++)(x,y)=(1-y,x);p=p.Master??p;if(p.Rotated)(x,y)=(1-y,x);return new Vector2((p.OutX+x*p.OutW)/width,(p.OutY+y*p.OutH)/height);}
        double error=0,max=0,maxTriangleRmse=0;int samples=0;
        foreach(var t in list){double triangleError=0;int triangleSamples=0;for(int a=0;a<=8;a++)for(int b=0;b<=8-a;b++){
            Vector2 uv=t.Uv[0]*(a/8f)+t.Uv[1]*(b/8f)+t.Uv[2]*((8-a-b)/8f);var before=Sample(source,uv,wrap.Item1,wrap.Item2,nearest);var after=Sample(output,Map(t,uv),wrap.Item1,wrap.Item2,nearest);
            for(int c=0;c<4;c++){double delta=before[c]-after[c];triangleError+=delta*delta;error+=delta*delta;max=Math.Max(max,Math.Abs(delta));}samples++;triangleSamples++;
        }maxTriangleRmse=Math.Max(maxTriangleRmse,Math.Sqrt(triangleError/(triangleSamples*4)));}
        double rmse=Math.Sqrt(error/(samples*4));if(maxTriangleRmse>maximumRmse)throw new InvalidDataException($"Appearance check failed for image {index}: worst triangle RGBA RMSE {maxTriangleRmse:F3}, overall {rmse:F3}, limit {maximumRmse}. No output model written");
        foreach(var t in list)for(int k=0;k<3;k++)t.Uv[k]=Map(t,t.Uv[k]);
        using var png=new MemoryStream();output.SaveAsPng(png);image["bufferView"]=AppendView(png.ToArray());image["mimeType"]="image/png";image.AsObject().Remove("uri");
        reports.Add(new{image=index,sourceWidth=source.Width,sourceHeight=source.Height,width,height,charts=patches.Count,sharedCharts,rotatedStacks,solidTriangles,sharedSolidColors=solids.Count,scale,preserveLayout,rgbaRmse=rmse,maxTriangleRmse,maxChannelError=max,samples,gutter=preserveLayout?0:Gutter,atlasPixels=width*height,chartBoundingBoxPixels=packedPatches.Sum(p=>(p.OutW+Gutter*2)*(p.OutH+Gutter*2))});
    }
    static int Address(int value,int size,int wrap)
    {if(wrap==33071)return Math.Clamp(value,0,size-1);int period=wrap==33648?size*2:size,n=(value%period+period)%period;return n>=size?period-1-n:n;}
    static Rgba32 Area(RasterImage image,double left,double top,double width,double height,int wrapS,int wrapT)
    {
        if(width==1&&height==1&&left==Math.Floor(left)&&top==Math.Floor(top))return image[Address((int)left,image.Width,wrapS),Address((int)top,image.Height,wrapT)];
        double r=0,g=0,b=0,a=0,total=0;
        for(int y=(int)Math.Floor(top);y<(int)Math.Ceiling(top+height);y++)for(int x=(int)Math.Floor(left);x<(int)Math.Ceiling(left+width);x++){
            double weight=Math.Max(0,Math.Min(x+1,left+width)-Math.Max(x,left))*Math.Max(0,Math.Min(y+1,top+height)-Math.Max(y,top));var p=image[Address(x,image.Width,wrapS),Address(y,image.Height,wrapT)];a+=p.A*weight;r+=p.R*p.A*weight;g+=p.G*p.A*weight;b+=p.B*p.A*weight;total+=weight;}
        byte C(double n)=>(byte)Math.Clamp((int)Math.Round(n),0,255);return new(C(a==0?0:r/a),C(a==0?0:g/a),C(a==0?0:b/a),C(a/total));
    }
    static float[] Sample(RasterImage image,Vector2 uv,int wrapS,int wrapT,bool nearest)
    {
        if(nearest){var p=image[Address((int)Math.Floor(uv.X*image.Width),image.Width,wrapS),Address((int)Math.Floor(uv.Y*image.Height),image.Height,wrapT)];return new float[]{p.R,p.G,p.B,p.A};}
        float x=uv.X*image.Width-.5f,y=uv.Y*image.Height-.5f;int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);float fx=x-ix,fy=y-iy;var result=new float[4];
        for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++){var p=image[Address(ix+dx,image.Width,wrapS),Address(iy+dy,image.Height,wrapT)];float w=(dx==0?1-fx:fx)*(dy==0?1-fy:fy);result[0]+=p.R*w;result[1]+=p.G*w;result[2]+=p.B*w;result[3]+=p.A*w;}return result;
    }
    readonly record struct Rect(int X,int Y,int W,int H);
    static bool Pack(List<Patch> patches,int width,int height,float scale)
    {
        var free=new List<Rect>{new(0,0,width,height)};
        foreach(var p in patches.OrderByDescending(p=>p.Solid==null?Math.Max(p.W,p.H):1).ThenBy(p=>p.Id)){
            p.OutW=p.Solid!=null?1:Math.Max(1,(int)Math.Ceiling(p.W*scale));p.OutH=p.Solid!=null?1:Math.Max(1,(int)Math.Ceiling(p.H*scale));int w=p.OutW+Gutter*2,h=p.OutH+Gutter*2;
            int best=-1,score=int.MaxValue,shortSide=int.MaxValue;p.Rotated=false;
            for(int k=0;k<free.Count;k++)for(int turn=0;turn<2;turn++){var r=free[k];int tw=turn==0?w:h,th=turn==0?h:w;if(r.W<tw||r.H<th)continue;int waste=r.W*r.H-tw*th,tight=Math.Min(r.W-tw,r.H-th);if(waste<score||waste==score&&tight<shortSide){best=k;score=waste;shortSide=tight;p.Rotated=turn==1;}}
            if(best<0)return false;
            if(p.Rotated){(p.OutW,p.OutH)=(p.OutH,p.OutW);(w,h)=(h,w);}
            var chosen=free[best];free.RemoveAt(best);p.OutX=chosen.X+Gutter;p.OutY=chosen.Y+Gutter;
            // Nonoverlapping guillotine free regions bound the work to O(charts²)
            // per trial; thousands of tiny artist charts cannot explode a
            // MaxRects free-list during the fixed-resolution search.
            int dw=chosen.W-w,dh=chosen.H-h;
            if(dw>dh){if(dw>0)free.Add(new(chosen.X+w,chosen.Y,dw,chosen.H));if(dh>0)free.Add(new(chosen.X,chosen.Y+h,w,dh));}
            else{if(dw>0)free.Add(new(chosen.X+w,chosen.Y,dw,h));if(dh>0)free.Add(new(chosen.X,chosen.Y+h,chosen.W,dh));}
        }return true;
    }
    void Rewrite(Primitive p)
    {
        var ids=new Dictionary<(int vertex,int patch),int>();var originals=new List<int>();var uv=new List<Vector2>();var indices=new List<int>();
        foreach(var t in p.Triangles)for(int k=0;k<3;k++){var key=(t.Vertices[k],t.Patch.Id);if(!ids.TryGetValue(key,out int index)){index=ids.Count;ids.Add(key,index);originals.Add(key.Item1);uv.Add(t.Uv[k]);}indices.Add(index);}
        foreach(var attr in p.Attributes){var old=Array("accessors")[attr.Value]!.DeepClone()!.AsObject();byte[] data;int element;
            if(attr.Key=="TEXCOORD_0"){element=8;data=new byte[uv.Count*8];for(int k=0;k<uv.Count;k++){BitConverter.GetBytes(uv[k].X).CopyTo(data,k*8);BitConverter.GetBytes(uv[k].Y).CopyTo(data,k*8+4);}old["componentType"]=5126;old.Remove("normalized");}
            else{element=Components((string)old["type"]!)*Bytes(I(old,"componentType"));var source=Accessor(attr.Value);if(originals.Any(v=>(long)(v+1)*element>source.Length))throw new InvalidDataException("Attribute counts disagree");data=new byte[originals.Count*element];for(int k=0;k<originals.Count;k++)Buffer.BlockCopy(source,originals[k]*element,data,k*element,element);}
            old["bufferView"]=AppendView(data);old["byteOffset"]=0;old["count"]=originals.Count;old.Remove("min");old.Remove("max");
            if(attr.Key=="POSITION"){
                if(I(old,"componentType")!=5126||(string)old["type"]! !="VEC3")throw new InvalidDataException("Core POSITION must be float VEC3");
                var min=new JsonArray();var max=new JsonArray();for(int c=0;c<3;c++){float low=float.PositiveInfinity,high=float.NegativeInfinity;for(int k=0;k<originals.Count;k++){float value=BitConverter.ToSingle(data,k*12+c*4);if(!float.IsFinite(value))throw new InvalidDataException("Nonfinite position");low=Math.Min(low,value);high=Math.Max(high,value);}min.Add(low);max.Add(high);}old["min"]=min;old["max"]=max;
            }
            int id=Array("accessors").Count;Array("accessors").Add(old);p.Json["attributes"]![attr.Key]=id;
        }
        byte[] ib=new byte[indices.Count*4];for(int k=0;k<indices.Count;k++)BinaryPrimitives.WriteInt32LittleEndian(ib.AsSpan(k*4),indices[k]);int ai=Array("accessors").Count;Array("accessors").Add(new JsonObject{["bufferView"]=AppendView(ib),["componentType"]=5125,["count"]=indices.Count,["type"]="SCALAR"});p.Json["indices"]=ai;
    }
    // Remove abandoned images/vertex streams while preserving accessors used
    // by untouched geometry, inverse bind matrices and animation channels.
    byte[] Finish()
    {
        var references=new List<(JsonNode parent,string key)>();
        foreach(var mesh in Array("meshes"))foreach(var p in mesh!["primitives"]!.AsArray()){foreach(var a in p!["attributes"]!.AsObject())references.Add((p["attributes"]!,a.Key));if(p["indices"]!=null)references.Add((p,"indices"));}
        if(json["skins"] is JsonArray skins)foreach(var s in skins)if(s!["inverseBindMatrices"]!=null)references.Add((s,"inverseBindMatrices"));
        if(json["animations"] is JsonArray animations)foreach(var a in animations)foreach(var s in a!["samplers"]!.AsArray()){references.Add((s!,"input"));references.Add((s!,"output"));}
        var old=Array("accessors");var live=references.Select(r=>(int)r.parent[r.key]!).Distinct().Order().ToArray();var map=live.Select((id,i)=>(id,i)).ToDictionary(v=>v.id,v=>v.i);var accessors=new JsonArray();foreach(int id in live)accessors.Add(old[id]!.DeepClone());foreach(var r in references)r.parent[r.key]=map[(int)r.parent[r.key]!];json["accessors"]=accessors;
        var viewRefs=accessors.Select(a=>(a!,"bufferView")).ToList();foreach(var image in Array("images"))if(image!["bufferView"]!=null)viewRefs.Add((image,"bufferView"));
        var viewIds=viewRefs.Select(r=>(int)r.Item1[r.Item2]!).Distinct().Order().ToArray();var viewMap=new Dictionary<int,int>();var views=new JsonArray();using var packed=new MemoryStream();
        foreach(int id in viewIds){while(packed.Length%4!=0)packed.WriteByte(0);var v=Array("bufferViews")[id]!.DeepClone()!;byte[] bytes=View(id);v["byteOffset"]=(int)packed.Length;packed.Write(bytes);viewMap.Add(id,views.Count);views.Add(v);}
        foreach(var r in viewRefs)r.Item1[r.Item2]=viewMap[(int)r.Item1[r.Item2]!];json["bufferViews"]=views;json["buffers"]![0]!["byteLength"]=(int)packed.Length;
        return EncodeGlb(json,packed.ToArray());
    }
    internal static byte[] EncodeGlb(JsonObject json,byte[] bin)
    {
        byte[] text=System.Text.Encoding.UTF8.GetBytes(json.ToJsonString());int j=(text.Length+3)&~3,b=(bin.Length+3)&~3;byte[] result=new byte[28+j+b];
        void Put(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at),value);
        Put(0,0x46546c67);Put(4,2);Put(8,result.Length);Put(12,j);Put(16,0x4e4f534a);System.Array.Fill(result,(byte)32,20,j);text.CopyTo(result,20);Put(20+j,b);Put(24+j,0x004e4942);bin.CopyTo(result,28+j);return result;
    }
    public static void Command(string[] args)
    {
        if(args.Length<5)throw new ArgumentException("repack-textures ROOT INPUT.glb OUTPUT.glb --size128|--size256|--size512|--size1024|--sizeOriginal [--max-rmse N]");
        int? size=args[4] switch{"--size128"=>128,"--size256"=>256,"--size512"=>512,"--size1024"=>1024,"--sizeOriginal"=>null,_=>throw new ArgumentException("Choose one supported --size flag")};double rmse=size==null?.01:8;
        if(args.Length!=5){if(args.Length!=7||args[5]!="--max-rmse"||!double.TryParse(args[6],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out rmse)||!double.IsFinite(rmse)||rmse<0||rmse>255)throw new ArgumentException("Invalid --max-rmse (0..255)");}
        Repack(args[2],args[3],size,rmse);
    }
    internal static void Repack(string input,string output,int? size,double rmse)
    {
        if(Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Output must differ from the artist source");
        var tool=new TextureRepacker(input,size,rmse);tool.Run();byte[] result=tool.Finish();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllBytes(output,result);
        var report=new{input=Path.GetFileName(input),sourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),outputSha256=Convert.ToHexString(SHA256.HashData(result)),triangles=tool.triangles,stackedSolidTriangles=tool.merged,maximumRgbaRmse=rmse,images=tool.reports};
        File.WriteAllText(output+".repack.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Repacked {tool.triangles} triangles; {tool.merged} identical-colour triangles share UV space: {output}");
    }
}
