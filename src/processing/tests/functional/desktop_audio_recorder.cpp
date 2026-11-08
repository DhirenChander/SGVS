#include <fstream>
#include "../../audio_capture.hpp"

int main()
{
    ProcessAudioReader audioReader((uint32_t)0);
    FILE *file = fopen("desktop_audio.pcm", "wb");

    audioReader.start(200 * 10000, AudioFormat{true, 44100, 16}, [&](uint32_t ind, uint16_t fCount, size_t fSize)
                      { 
                        for(int i = 0; i < fCount; i++) 
                        {
                            fwrite(audioReader.get_buffered_frame(ind, i), fSize, 1, file); 
                        } });

    while (true)
        ;
}