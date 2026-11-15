#include "../shared_mem_lib/cpp/shared_mem_client.hpp"
#include "audio_processor.hpp"
#include "audio_capture.hpp"

#include <iostream>

AudioProcessor *setup_processor(shared_mem_client::SharedMemoryFrameBuffer *fb, shared_mem_client::Encoding enc)
{
    // TODO: Make these variable
    AudioProcessorConfig config;
    config.bitrate = 48000;
    config.inputChannels = 2;
    config.inputSampleRate = 16;

    AudioProcessor *processor = new AudioProcessor(config, [&](const uint8_t *data, size_t size)
                                                   {
        // NOTE: Assumption, each callback corresponds to a single frame
        size_t fsize = 0;
        shared_mem_client::Frame frame = shared_mem_client::frame_sized_by_encoding(enc, &fsize);
        frame.size = size;
        frame.data = (uint8_t*)data;
        
        // TODO: Make this more resilient, this will work but might cause some skips when data is dropped
        if (!fb->push_frames(&frame, 1))
        {
            std::cout << "Buffer full, cannot push until data is pulled" << std::endl;
        } });

    return processor;
}

int main()
{
    std::cout << "Running Processor..." << std::endl;
    shared_mem_client::SharedMemoryFrameBuffer fb{};

    shared_mem_client::Encoding enc = shared_mem_client::new_audio_encoding(true);
    shared_mem_client::set_encoding_outgoing(&enc);

    fb.request_encoding(enc);
    fb.align_rings_to_config();

    ProcessAudioReader micReader(L"");
    AudioProcessor *processor = setup_processor(&fb, enc);

    // TODO: Parallelize push_pcm or this callback
    micReader.start(200 * 10000, [&](uint32_t ind, uint16_t fCount, size_t fSize)
                    { processor->push_pcm((int16_t *)micReader.get_buffered_frame(ind, 0), fCount); });

    // TODO: Add encoding watching to watch for screen share requests
    while (true)
    {
    }

    return 0;
}