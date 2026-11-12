using System;
using System.IO;

// Resolve only explicit loader, launch-option or installer-record provenance.
// In particular, dvd: is an alias candidate, not proof that a same-named GC
// Loader file is the running image. The caller must mount and verify it.
internal static class UpdateImagePath
{
    private const string Root="gcloader:/",Option="--animaltown-image=";

    internal static string Find(string[] args,string mountedImagePath="",string installedImagePath="")
    {
        if(args==null)throw new IOException("Invalid image launch arguments.");
        string image="";
        if(mountedImagePath!=null&&mountedImagePath.Length>0)image=Canonical(mountedImagePath);
        for(int i=0;i<args.Length;i++){
            string value=args[i];
            if(value==null)throw new IOException("Invalid image launch arguments.");
            if(value.StartsWith(Option)){
                image=Choose(image,Canonical(value.Substring(Option.Length)));continue;
            }
            if(value=="--animaltown-image")throw new IOException("Use --animaltown-image=<full image path>.");
            if(!ImageSuffix(value))continue;
            if(i!=0)throw new IOException("Unexpected extra image path. Use --animaltown-image=<path> with a standalone DOL.");
            image=Choose(image,Canonical(value));
        }
        // Empty means no installer record. A caller that read an existing file
        // must reject an empty/blank record rather than treating it as absent.
        if(installedImagePath!=null&&installedImagePath.Length>0)image=Choose(image,Canonical(installedImagePath));
        if(image.Length==0)throw new IOException("Configure the installed image with provision.py --image before updating.");
        return image;
    }

    private static string Choose(string current,string candidate)
    {
        if(current.Length==0)return candidate;
        if(!Same(current,candidate))throw new IOException("Conflicting update image paths. Check the installer record and launch arguments.");
        return current;
    }

    private static string Canonical(string path)
    {
        if(path==null)throw new IOException("Missing update image path.");
        int prefix=Prefix(path,"gcloader:/")?10:Prefix(path,"gcldr:/")?7:Prefix(path,"dvd:/")?5:0;
        if(prefix==0)throw new IOException("Update image must use a full GC Loader image path.");
        string image=Root+path.Substring(prefix);Utf8Length(image,900);
        if(image.Length<Root.Length+5)throw new IOException("Update target must include an image filename.");
        int start=Root.Length;
        for(int i=start;i<=image.Length;i++){
            if(i<image.Length){
                char c=image[i];
                if(c<32||c>=127&&c<=159||c=='\\'||c==':'||c=='='||c=='*'||c=='?'||c=='"'||c=='<'||c=='>'||c=='|')
                    throw new IOException("Invalid update image path.");
                if(c!='/')continue;
            }
            int length=i-start;
            if(length==0||length>255||length==1&&image[start]=='.'||length==2&&image[start]=='.'&&image[start+1]=='.'||image[i-1]==' '||image[i-1]=='.')
                throw new IOException("Use a canonical update image path without traversal or empty components.");
            if(i==image.Length&&length>248)throw new IOException("Image filename leaves no room for recovery files.");
            start=i+1;
        }
        if(!ImageSuffix(image))throw new IOException("Update target must be an ISO or GCM image.");
        return image;
    }

    private static bool ImageSuffix(string value)
    {
        if(value.Length<4)return false;
        int at=value.Length-4;
        return value[at]=='.'&&(Lower(value[at+1])=='i'&&Lower(value[at+2])=='s'&&Lower(value[at+3])=='o'||
                               Lower(value[at+1])=='g'&&Lower(value[at+2])=='c'&&Lower(value[at+3])=='m');
    }
    private static bool Prefix(string value,string prefix)
    {
        if(value.Length<prefix.Length)return false;
        for(int i=0;i<prefix.Length;i++)if(Lower(value[i])!=prefix[i])return false;
        return true;
    }
    private static bool Same(string a,string b)
    {
        if(a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++)if(Lower(a[i])!=Lower(b[i]))return false;
        return true;
    }
    private static char Lower(char value)=>value>='A'&&value<='Z'?(char)(value+32):value;
    private static void Utf8Length(string value,int maximum)
    {
        int bytes=0;
        for(int i=0;i<value.Length;i++){
            int c=value[i];
            if(c>=0xd800&&c<=0xdbff){
                if(++i>=value.Length||value[i]<0xdc00||value[i]>0xdfff)throw new IOException("Invalid Unicode in update image path.");
                bytes+=4;
            }else if(c>=0xdc00&&c<=0xdfff)throw new IOException("Invalid Unicode in update image path.");
            else bytes+=c<128?1:c<2048?2:3;
            if(bytes>maximum)throw new IOException("Update image path is too long.");
        }
    }

    internal static void VerifyHeader(byte[] header,string runningVersion)
    {
        if(header==null||header.Length<0x440)throw new IOException("The running image header is truncated.");
        if(runningVersion==null||runningVersion.Length==0)throw new IOException("The running build version is missing.");
        for(int i=0;i<runningVersion.Length;i++)if(runningVersion[i]<33||runningVersion[i]>126)
            throw new IOException("Invalid running build version.");
        const string game="AMTE01";for(int i=0;i<game.Length;i++)if(header[i]!=game[i])
            throw new IOException("The selected image is not AnimalTown.");
        if(header[0x1c]!=0xc2||header[0x1d]!=0x33||header[0x1e]!=0x9f||header[0x1f]!=0x3d)
            throw new IOException("The selected file is not a GameCube image.");
        string title="AnimalTown / "+runningVersion;
        if(title.Length>=0x3e0)throw new IOException("The running build version is too long.");
        for(int i=0;i<title.Length;i++)if(header[0x20+i]!=title[i])
            throw new IOException("The image does not match the running AnimalTown build.");
        if(header[0x20+title.Length]!=0)throw new IOException("The image does not match the running AnimalTown build.");
    }
}
