// Records desktop audio for a few seconds, compresses it with AudioProcessor, then decodes it again
// so the result can be listened to. Open desktop_audio_opus.pcm in Audacity with
// File > Import > Raw Data: signed 16-bit PCM, little-endian, 2 channels, 48000 Hz.
#include <chrono>
#include <cstdio>
#include <thread>
#include <vector>

#include <opus.h>

#include "../../audio_capture.hpp"
#include "../../audio_processor.hpp"

int main()
{
    const int seconds = 10;
    const uint32_t bitrate = 64000;

    int err = OPUS_OK;
    OpusDecoder *decoder = opus_decoder_create(AudioProcessor::SAMPLE_RATE, AudioProcessor::CHANNELS, &err);
    if (err != OPUS_OK)
    {
        fprintf(stderr, "Failed to create decoder: %s\n", opus_strerror(err));
        return 1;
    }

    FILE *file = fopen("desktop_audio_opus.pcm", "wb");
    std::vector<int16_t> decoded(AudioProcessor::FRAME_SAMPLES * AudioProcessor::CHANNELS);
    size_t packets = 0, bytes = 0;

    AudioProcessorConfig config;
    config.bitrate = bitrate;
    AudioProcessor processor(config, [&](const uint8_t *data, size_t size)
                             {
                                 packets++;
                                 bytes += size;
                                 int n = opus_decode(decoder, data, (opus_int32)size, decoded.data(),
                                                     (int)AudioProcessor::FRAME_SAMPLES, 0);
                                 if (n > 0)
                                     fwrite(decoded.data(), sizeof(int16_t) * AudioProcessor::CHANNELS, n, file); });

    // 48 kHz, stereo, 16-bit is the format the encoder expects
    ProcessAudioReader audioReader((uint32_t)0, AudioFormat{true, AudioProcessor::SAMPLE_RATE, 16});

    // Callbacks run on the capture thread. Frames can wrap around the ring buffer, so feed them one at a time.
    audioReader.start(200 * 10000, [&](uint32_t ind, uint16_t fCount, size_t fSize)
                      {
                          for (int i = 0; i < fCount; i++)
                              processor.push_pcm((const int16_t *)audioReader.get_buffered_frame(ind, i), 1); });

    std::this_thread::sleep_for(std::chrono::seconds(seconds));
    audioReader.stop();

    printf("Encoded %zu packets (%zu bytes) at %u bps\n", packets, bytes, bitrate);
    if (packets > 0)
        printf("Average packet: %zu bytes, expected %u\n", bytes / packets, bitrate / 8 / 100);

    fclose(file);
    opus_decoder_destroy(decoder);
    return 0;
}
