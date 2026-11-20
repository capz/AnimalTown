using AnimalTown.Jobs;
using System.Text;

internal static class JobLetterChecks
{
    private static string Body(int length){var c=new char[length];for(int i=0;i<length;i++)c[i]=(char)('a'+i%26);return new string(c);}
    internal static void Run(Action<bool,string> check)
    {
        JobTextBodyChecks.Run(check);
        check(JobLetterGrader.UnicodeVersion=="14.0.0"&&JobLetterGrader.UnicodeTableBytes==46344,"Unicode version and retained table bytes are explicit");
        foreach(int length in new[]{16,17,48,49})foreach(bool gift in new[]{false,true}){
            check(JobLetterGrader.TryGrade(Body(length),gift,out var grade)&&grade.Quality&&grade.FilteredLength==length&&grade.Rank==(length<17?3:length<49?4:5)+(gift?6:0)&&grade.Friendship==(gift?6:3),"length, quality and present produce exact reply rank and mail relationship effect");
        }
        check(JobLetterGrader.TryGrade("",true,out var empty)&&empty.Rank==6&&empty.Friendship==1&&!empty.Quality,"empty gift letter has the specified native-fruit rank and relationship effect");
        check(JobLetterGrader.TryGrade(new string('!',49),true,out var failed)&&!failed.Quality&&failed.Rank==8&&failed.Friendship==1,"long punctuation-only letter fails quality but retains length/gift rank");
        foreach(string bad in new[]{"a a a a z","a\u200da\u200ca\u2060aZ","1111z"})check(JobLetterGrader.TryGrade(bad,false,out var g)&&!g.Quality,"ignored whitespace/control/format characters cannot evade four-letter repetition");
        foreach(string good in new[]{"AaaaZ","a.a.a.a","\U0001f600\U0001f600\U0001f600\U0001f600a","\u0661\u0662\u0663\u0664\u0665","\U00010400\U00010401\U00010402\U00010403\U00010404"})check(JobLetterGrader.TryGrade(good,false,out var g)&&g.Quality,"case, punctuation, emoji and non-BMP letters use scalar/category rules");
        check(JobLetterGrader.TryGrade("\u2460\u2461\u2462\u2463\u2464",false,out var circles)&&!circles.Quality,"Unicode other-number symbols are not decimal digits");
        check(JobLetterGrader.TryGrade(Body(1000),false,out var maximum)&&maximum.ScalarCount==1000&&!JobLetterGrader.TryGrade(Body(1001),false,out _),"composition enforces the normalized scalar ceiling");
        check(!JobLetterGrader.TryGrade("a\ud800b",false,out _)&&!JobLetterGrader.TryGrade("a\udc00b",false,out _),"ill-formed surrogate sequences are rejected");
        string[] fixtures={"Cafe\u0301!","\u212babc!","\u03b1\u0313\u0300\u0345","\u1100\u1161\u11a8","\u0344","\u0958","\U0001d15e","a\u0315\u0300\u05ae","\u0301\u0323a"};
        foreach(string input in fixtures)check(JobLetterGrader.TryNormalize(input,out string actual,out _)&&actual==input.Normalize(NormalizationForm.FormC),"NFC matches the independent BCL oracle for canonical exclusions, ordering, Hangul and supplementary mappings");
        for(int cp=0xc0;cp<=0x1ff;cp++)Compare(((char)cp)+"\u0301\u0323",check);
        for(int cp=0x1e00;cp<=0x1fff;cp++)Compare(((char)cp).ToString(),check);
        for(int cp=0xac00;cp<=0xd7a3;cp+=53)Compare(((char)cp).ToString(),check);
        var longMarks=new StringBuilder("a");for(int i=0;i<150;i++)longMarks.Append("\u0315\u0323");Compare(longMarks.ToString(),check);
        check(JobLetterGrader.TryNormalize(new string('\u1f82',1000),out var composed,out int scalars)&&scalars==1000&&composed.Length==1000,"maximum canonical expansion can recombine to an accepted 1000-scalar body");
        Console.WriteLine("Letter grading: Unicode NFC/scalars/categories, all rank boundaries, repetition, limits and independent normalization comparisons passed; 46,344 table bytes");
    }
    private static void Compare(string input,Action<bool,string> check)
    {check(JobLetterGrader.TryNormalize(input,out string actual,out _)&&actual==input.Normalize(NormalizationForm.FormC),"console normalization agrees with the independent runtime oracle");}
}
