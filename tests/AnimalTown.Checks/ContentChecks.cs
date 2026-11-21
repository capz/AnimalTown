using AnimalTown;
using System.Text;

internal static class ContentChecks
{
    public static void Run(Action<bool,string> check)
    {
        const string commit="0123456789abcdef",version="1.2.3",digest="source";
        const string identity="ANIMALTOWN-CONTENT-1\n0123456789abcdef\n1.2.3\nsource\n0\n";
        check(ContentIdentity.Create(commit,version,digest,0)==identity,"content identity keeps the deployed wire format");
        byte[] bytes=Encoding.ASCII.GetBytes(identity);
        check(ContentIdentity.Matches(bytes,identity),"exact package identity is accepted");
        foreach(string different in new[]{ContentIdentity.Create("different-commit",version,digest,0),ContentIdentity.Create(commit,"1.2.4",digest,0),
            ContentIdentity.Create(commit,version,"different-source",0),ContentIdentity.Create(commit,version,digest,1)})
            check(!ContentIdentity.Matches(Encoding.ASCII.GetBytes(different),identity),"commit/version/source/profile changes cannot share an installation");
        check(!ContentIdentity.Matches(Encoding.ASCII.GetBytes(identity+"extra"),identity),"extra identity bytes are rejected");
        check(!ContentIdentity.Matches(Encoding.ASCII.GetBytes(identity.Substring(0,identity.Length-1)),identity),"truncated identity is rejected");
        check(!ContentIdentity.Matches(Array.Empty<byte>(),identity),"empty identity is rejected");
        string huge=new string('x',ContentIdentity.MaximumLength+1);
        check(!ContentIdentity.Matches(Encoding.ASCII.GetBytes(huge),huge),"identity comparison remains bounded even with matching oversized inputs");
        check(ContentPaths.Asset("wordmark.dndtex")=="dvd:/wordmark.dndtex","legacy host/default paths remain disc compatible");
        foreach(string invalid in new[]{"","relative/","sd:/apps/animaltown"}){
            bool rejected=false;try{ContentPaths.Select(invalid);}catch(ArgumentException){rejected=true;}
            check(rejected,"invalid root metadata cannot select an installation");
        }
        // Canonicalization and device aliases belong to the SDK. This layer
        // preserves its chosen root without retaining the old 512-char parser.
        string root="carda:/"+new string('a',180)+"/"+new string('b',180)+"/"+new string('c',180)+"/";
        ContentPaths.Select(root);
        check(ContentPaths.Root==root&&ContentPaths.Asset("wordmark.dndtex")==root+"wordmark.dndtex"&&
            TexturePolicy.ModelPath("fox",TextureResolution.Size128)==root+"fox-tex128.dndmodel","assets and model variants share the immutable SDK-selected root, including Slot A and long directories");
        bool reselected=false;try{ContentPaths.Select("sd:/apps/animaltown/");}catch(InvalidOperationException){reselected=true;}
        check(reselected,"content root cannot change after initialization");
    }
}
