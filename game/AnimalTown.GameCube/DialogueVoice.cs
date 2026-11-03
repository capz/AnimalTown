using System;
using Dolphin.Audio;

// Speech is one short voice, not one retained native voice per syllable.
// Explicit retirement also works when a backend misses its end callback.
internal sealed class DialogueVoice
{
    private readonly Sound clip;
    private SoundChannel? active;
    internal bool Available {get;private set;}=true;
    internal DialogueVoice(Sound clip){this.clip=clip;}
    internal void Begin(){Stop();Available=true;}
    internal bool Pulse()
    {
        if(!Available)return false;
        Stop();
        try{active=clip.Play();return true;}
        catch(InvalidOperationException){Available=false;return false;}
    }
    internal void Stop(){active?.Dispose();active=null;}
}
