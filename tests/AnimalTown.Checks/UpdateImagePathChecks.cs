using System.IO;

internal static class UpdateImagePathChecks
{
    internal static void Run(Action<bool,string> check)
    {
        void Reject(string[] args,string label,string mounted="",string installed="")
        {
            bool rejected=false;try{UpdateImagePath.Find(args,mounted,installed);}catch(IOException){rejected=true;}
            check(rejected,label);
        }
        const string image="gcloader:/Games/Cozy village/My renamed town.nkit.iso";
        check(UpdateImagePath.Find(new[]{"gcldr:/Games/Cozy village/My renamed town.nkit.iso"})==image,"Swiss argv image preserves renamed NKit and subdirectories");
        check(UpdateImagePath.Find(new[]{"dvd:/Games/Cozy village/My renamed town.nkit.iso"})==image,"Swiss dvd alias resolves only to a GC Loader candidate");
        check(UpdateImagePath.Find(new[]{"sd:/apps/game.dol","--animaltown-image="+image})==image,"standalone DOL uses its explicit image path");
        check(UpdateImagePath.Find(new[]{"--animaltown-image="+image})==image,"explicit image option does not require a synthetic argv zero");
        check(UpdateImagePath.Find(Array.Empty<string>(),image)==image,"mounted disc-image provenance needs no launch argv");
        check(UpdateImagePath.Find(new[]{"gcldr:/games/cozy village/my renamed town.nkit.ISO","--animaltown-image="+image},image)==image,"authoritative mounted spelling is retained across equivalent aliases and ASCII case");
        check(UpdateImagePath.Find(new[]{"GCLOADER:/日本語/Étoile 🌿.GCM"})=="gcloader:/日本語/Étoile 🌿.GCM","Unicode including supplementary characters and image extension case survive canonicalization");
        check(UpdateImagePath.Find(new[]{"dvd:/Any basename.iso","--debug","seed=3"})=="gcloader:/Any basename.iso","unrelated nonimage arguments are not update target candidates");
        check(UpdateImagePath.Find(new[]{"game.dol","--animaltown-image=dvd:/Any basename.iso","--animaltown-image=gcldr:/Any basename.iso"})=="gcloader:/Any basename.iso","identical explicit aliases are unambiguous");
        check(UpdateImagePath.Find(Array.Empty<string>(),"",image)==image,"direct boot without argv uses an explicit installer record");
        check(UpdateImagePath.Find(new[]{"sd:/apps/renamed.dol"},"","gcldr:/日本語/My arbitrary name.GCM")=="gcloader:/日本語/My arbitrary name.GCM","standalone DOL can resolve an arbitrary Unicode image from its installer record");
        check(UpdateImagePath.Find(new[]{"dvd:/games/cozy village/my renamed town.nkit.ISO"},image,"gcldr:/Games/Cozy village/My renamed town.nkit.iso")==image,"mounted, launch and installer candidates must agree after alias normalization");
        check(UpdateImagePath.Find(new[]{"gcldr:/Games/Cozy village/My renamed town.nkit.iso"},"",image)==image,"installer record confirms rather than replaces an explicit launch path");
        Reject(Array.Empty<string>(),"missing provenance does not guess an image filename");
        Reject(null!,"null launch args fail with IOException");
        Reject(new string[]{null!},"null argv entries fail with IOException");
        Reject(new[]{"gcldr:/apps/AnimalTown.dol"},"DOL path is never mistaken for the update image");
        Reject(new[]{"gcldr:/One.iso","--animaltown-image=gcldr:/Two.iso"},"argv and explicit image conflicts fail closed");
        Reject(new[]{"game.dol","--animaltown-image=gcldr:/One.iso","--animaltown-image=dvd:/Two.iso"},"conflicting explicit image options are rejected");
        Reject(new[]{"gcldr:/Other.iso"},"mounted and argv images must agree",image);
        Reject(new[]{"--animaltown-image="+image},"invalid authoritative mount never falls back to argv","sd:/Other.iso");
        Reject(Array.Empty<string>(),"installer and mounted image conflicts are rejected",image,"gcloader:/Other.iso");
        Reject(new[]{"gcldr:/Other.iso"},"installer and argv image conflicts are rejected","",image);
        Reject(new[]{"game.dol","--animaltown-image=gcldr:/Other.iso"},"installer and explicit image option conflicts are rejected","",image);
        Reject(new[]{"gcldr:/Games/Cozy village/My renamed town.nkit.iso"},"invalid installer scheme does not fall back to valid argv","","sd:/Other.iso");
        Reject(Array.Empty<string>(),"invalid installer path does not fall back to mounted image",image,"gcloader:/../Other.iso");
        Reject(Array.Empty<string>(),"installer record is not implicitly trimmed","",image+"\n");
        Reject(new[]{"--animaltown-image="+image},"whitespace installer mapping is invalid despite explicit target",""," ");
        Reject(new[]{"game.dol","gcldr:/Target.iso"},"bare extra argument cannot supply standalone DOL provenance");
        Reject(new[]{"gcldr:/One.iso","gcldr:/Two.iso"},"extra bare image arguments are ambiguous");
        Reject(new[]{"game.dol","--animaltown-image"},"explicit image option requires equals syntax");
        Reject(new[]{"--animaltown-image="},"empty explicit image path is rejected");
        foreach(string path in new[]{"sd:/Town.iso","sda:/Town.iso","/Town.iso","Town.iso","gcldr:Town.iso","gcloader://Town.iso","gcloader:/","gcloader:/.iso","gcloader:/.gcm","gcloader:/Folder/","gcloader:/Town.dol","gcloader:/Town.iso.old"})
            Reject(new[]{"--animaltown-image="+path},"unsupported/nonimage path: "+path);
        foreach(string relative in new[]{"../Town.iso","./Town.iso","Games/../Town.iso","Games/./Town.iso","Games//Town.iso","Games./Town.iso","Games /Town.iso","Town.iso ","Town.iso."})
            Reject(new[]{"--animaltown-image=gcloader:/"+relative},"FAT traversal/normalization ambiguity rejected: "+relative);
        foreach(char invalid in new[]{'\\',':','=','*','?','"','<','>','|','\0','\n','\t','\x7f','\x85'})
            Reject(new[]{"--animaltown-image=gcloader:/My"+invalid+"Town.iso"},"unsafe FAT/control character rejected: "+(int)invalid);
        Reject(new[]{"--animaltown-image=gcloader:/Bad\ud800.iso"},"unpaired high surrogate rejected");
        Reject(new[]{"--animaltown-image=gcloader:/Bad\udc00.iso"},"unpaired low surrogate rejected");
        string longestName=new string('a',244)+".iso";
        check(UpdateImagePath.Find(new[]{"gcldr:/"+longestName})=="gcloader:/"+longestName,"248-character image basename retains recovery suffix room");
        Reject(new[]{"gcldr:/a"+longestName},"249-character basename leaves insufficient recovery suffix room");
        string longestPath="gcloader:/"+new string('a',255)+"/"+new string('b',255)+"/"+new string('c',255)+"/"+new string('d',118)+".iso";
        check(longestPath.Length==900&&UpdateImagePath.Find(new[]{longestPath})==longestPath,"900-byte canonical launch path is accepted");
        Reject(new[]{longestPath.Insert(longestPath.Length-4,"d")},"901-byte canonical path rejected");
        Reject(new[]{"gcloader:/"+new string('a',256)+"/Town.iso"},"FAT directory components are bounded to 255 UTF-16 code units");
        string unicodePath="gcldr:/"+new string('é',200)+"/"+new string('é',200)+"/"+new string('é',40)+".iso";
        check(UpdateImagePath.Find(new[]{unicodePath}).Length<900,"UTF-8 path limit permits valid Unicode below 900 bytes");
        Reject(new[]{unicodePath.Insert(unicodePath.Length-4,new string('é',5))},"UTF-8 encoded length, not UTF-16 count, bounds the protocol path");

        const string version="0.11.24+0123456789ab";
        byte[] Header(string title="AnimalTown / "+version)
        {
            var value=new byte[0x440];
            for(int i=0;i<6;i++)value[i]=(byte)"AMTE01"[i];
            value[0x1c]=0xc2;value[0x1d]=0x33;value[0x1e]=0x9f;value[0x1f]=0x3d;
            for(int i=0;i<title.Length;i++)value[0x20+i]=(byte)title[i];
            return value;
        }
        void RejectHeader(byte[] header,string label,string running=version)
        {
            bool rejected=false;try{UpdateImagePath.VerifyHeader(header,running);}catch(IOException){rejected=true;}
            check(rejected,label);
        }
        UpdateImagePath.VerifyHeader(Header(),version);check(true,"exact running game/build header is accepted");
        byte[] larger=new byte[0x800];Header().CopyTo(larger,0);UpdateImagePath.VerifyHeader(larger,version);check(true,"header verification accepts a larger read buffer");
        foreach(int length in new[]{0,6,0x20,0x43f})RejectHeader(Header().Take(length).ToArray(),"truncated image header rejected: "+length);
        RejectHeader(null!,"null image header rejected");
        byte[] otherGame=Header();otherGame[0]=(byte)'G';RejectHeader(otherGame,"other game ID is rejected even with a spoofed AnimalTown title");
        byte[] otherMaker=Header();otherMaker[5]=(byte)'2';RejectHeader(otherMaker,"other maker ID is rejected");
        byte[] wrongMagic=Header();wrongMagic[0x1c]=0;RejectHeader(wrongMagic,"GameCube magic is required");
        RejectHeader(Header("Other game / "+version),"AnimalTown title prefix must match exactly");
        RejectHeader(Header("AnimalTown / 0.11.24+fedcba987654"),"different build of the same game is rejected");
        RejectHeader(Header("AnimalTown / "+version+"0"),"version prefix is not sufficient");
        RejectHeader(Header("AnimalTown / "+version+" "),"trailing title data is rejected");
        RejectHeader(Header("AnimalTown / 0.11.24"),"missing build identity is rejected");
        RejectHeader(Header(),"empty running version is rejected","");
        RejectHeader(Header(),"embedded control in running version is rejected",version+"\0");
        RejectHeader(Header(),"title overflow in running version is rejected",new string('v',0x3e0));
    }
}
