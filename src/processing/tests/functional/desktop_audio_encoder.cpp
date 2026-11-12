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
    if (file == nullptr)
    {
        fprintf(stderr, "Failed to open desktop_audio_opus.pcm for writing\n");
        opus_decoder_destroy(decoder);
        return 1;
    }
    std::vector<int16_t> decoded(AudioProcessor::FRAME_SAMPLES * AudioProcessor::CHANNELS);
    const size_t expectedSize = (bitrate + 400) / 800;
    // Only touched on the capture thread until stop() joins it
    size_t packets = 0, bytes = 0, wrongSize = 0, decodeErrors = 0, pushErrors = 0, wrongFrameSize = 0;

    // 48 kHz, stereo, 16-bit is the format the encoder expects
    AudioFormat format{true, AudioProcessor::SAMPLE_RATE, 16};

    AudioProcessorConfig config;
    config.bitrate = bitrate;
    // Taken from the capture format, so a mismatch fails here instead of sounding wrong
    config.inputChannels = format.multiChannel ? 2 : 1;
    config.inputSampleRate = format.samplesPerSec;
    AudioProcessor processor(config, [&](const uint8_t *data, size_t size)
                             {
                                 packets++;
                                 bytes += size;
                                 if (size != expectedSize)
                                     wrongSize++;
                                 int n = opus_decode(decoder, data, (opus_int32)size, decoded.data(),
                                                     (int)AudioProcessor::FRAME_SAMPLES, 0);
                                 if (n > 0)
                                     fwrite(decoded.data(), sizeof(int16_t) * AudioProcessor::CHANNELS, n, file);
                                 else
                                     decodeErrors++; });

    ProcessAudioReader audioReader((uint32_t)0, format);

    // Bytes in one frame of the PCM the encoder expects: one 16-bit sample per channel
    const size_t expectedFrameSize = config.inputChannels * sizeof(int16_t);

    // Callbacks run on the capture thread. Frames can wrap around the ring buffer, so feed them one at a time.
    bool started = audioReader.start(200 * 10000, [&](uint32_t ind, uint16_t fCount, size_t fSize)
                                     {
                                         // A device delivering another format (24-bit, float, more channels)
                                         // would come out as noise, so skip it and report it instead
                                         if (fSize != expectedFrameSize)
                                         {
                                             wrongFrameSize++;
                                             return;
                                         }
                                         for (int i = 0; i < fCount; i++)
                                             if (!processor.push_pcm((const int16_t *)audioReader.get_buffered_frame(ind, i), 1))
                                                 pushErrors++; });
    if (!started)
    {
        fprintf(stderr, "Failed to start desktop audio capture\n");
        fclose(file);
        opus_decoder_destroy(decoder);
        return 1;
    }

    std::this_thread::sleep_for(std::chrono::seconds(seconds));
    audioReader.stop();

    printf("Encoded %zu packets (%zu bytes) at %u bps, expected about %d\n", packets, bytes, bitrate, seconds * 100);
    printf("Packets not %zu bytes: %zu\n", expectedSize, wrongSize);
    printf("Packets that failed to decode: %zu\n", decodeErrors);
    printf("Frames the encoder refused: %zu\n", pushErrors);
    printf("Capture callbacks skipped for a frame size other than %zu bytes: %zu\n", expectedFrameSize, wrongFrameSize);

    fclose(file);
    opus_decoder_destroy(decoder);
    const bool ok = packets > 0 && wrongSize == 0 && decodeErrors == 0 && pushErrors == 0 && wrongFrameSize == 0;
    puts(ok ? "OK" : "FAILED");
    return ok ? 0 : 1;
}
