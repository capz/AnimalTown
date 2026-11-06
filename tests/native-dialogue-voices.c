/* Reuse the SDK's deterministic storage/DSP fake with its actual native audio
 * implementation. This fake never ends a voice until explicitly stopped. */
#define main sdk_audio_acceptance_main
#include "fixtures/sdk-audio-fake.c"
#undef main
int main(void) {
    assert(dnd_audio_init()==1);
    valid_wav();
    int clip=dnd_audio_begin((DndString*)1);
    assert(clip>0);
    while(dnd_audio_step(clip)==0){}
    int held[32];
    for(int i=0;i<32;i++){held[i]=dnd_audio_play(clip,0,.12f,1);assert(held[i]>0);}
    assert(dnd_audio_play(clip,0,.12f,1)==0);
    for(int i=0;i<32;i++)assert(dnd_audio_voice_stop(held[i])==0);
    int current=0;
    for(int i=0;i<1024;i++){
        if(current)assert(dnd_audio_voice_stop(current)==0);
        current=dnd_audio_play(clip,0,.12f,1);assert(current>0);
        int playing=0;for(int j=0;j<32;j++)playing+=active[j]!=0;
        assert(playing==1);
    }
    assert(dnd_audio_voice_stop(current)==0);
    assert(dnd_audio_dispose(clip)==0);
    dnd_audio_shutdown();
    puts("dialogue native voice regression passed: 32-voice exhaustion reproduced; 1024 owned syllables stay bounded");
    return 0;
}
