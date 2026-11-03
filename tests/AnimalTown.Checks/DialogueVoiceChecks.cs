// Deliberately never delivers an automatic end callback, matching the stalled
// backend case. The production DialogueVoice is linked unchanged into checks.
namespace Dolphin.Audio
{
    internal sealed class Sound
    {
        internal int Active,Maximum,Plays,Stops;
        internal bool FailNext;
        internal SoundChannel Play(){if(FailNext||Active==32){FailNext=false;throw new InvalidOperationException("No free audio voice");}Active++;Maximum=Math.Max(Maximum,Active);Plays++;return new SoundChannel(this);}
    }
    internal sealed class SoundChannel : IDisposable
    {
        private readonly Sound owner;private bool stopped;
        internal SoundChannel(Sound owner){this.owner=owner;}
        public void Dispose(){if(stopped)return;stopped=true;owner.Active--;owner.Stops++;}
    }
}
internal static class DialogueVoiceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var unbounded=new Dolphin.Audio.Sound();for(int i=0;i<32;i++)unbounded.Play();
        bool exhausted=false;try{unbounded.Play();}catch(InvalidOperationException){exhausted=true;}
        check(exhausted,"old fire-and-forget syllables exhaust a backend without completion callbacks");
        var clip=new Dolphin.Audio.Sound();var voice=new DialogueVoice(clip);
        for(int conversation=0;conversation<8;conversation++){
            voice.Begin();for(int i=0;i<100;i++)check(voice.Pulse()&&clip.Active==1,"typing reuses one native voice even without end callbacks");
            voice.Stop();check(clip.Active==0,"completion or dismissal retires the final syllable");
        }
        check(clip.Maximum==1&&clip.Plays==800&&clip.Stops==800,"repeated conversations keep native voice usage bounded and balanced");
        voice.Begin();voice.Pulse();voice.Begin();check(clip.Active==0,"replacing a message stops the old voice");
        voice.Pulse();clip.FailNext=true;check(!voice.Pulse()&&!voice.Available&&clip.Active==0,"optional audio failure is contained instead of throwing through the game loop");
        int plays=clip.Plays;check(!voice.Pulse()&&clip.Plays==plays,"a failed conversation does not retry playback on every character");
        voice.Begin();check(voice.Available&&voice.Pulse(),"the next conversation can recover audio playback");
        voice.Stop();voice.Stop();check(clip.Active==0,"skip and close cleanup are idempotent");
    }
}
