#include "../../../../cpp/shared_mem_client.hpp"
#include <thread>
#include <iostream>
#include <unordered_set>

void mock_processor(Frame *f, shared_mem_client::SharedMemoryFrameBuffer *fb)
{
    while (true)
    {
        if (!fb->push_frames(f, 1))
        {
            std::cout << "Buffer full, cannot push until data is pulled" << std::endl;
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(16));
    }
}

void mock_reciever(Encoding enc, shared_mem_client::SharedMemoryFrameBuffer *fb)
{
    std::cout << "Recieving: " << std::hex << enc.id << std::dec << std::endl;
    Frame out[120]{};

    for (int i = 0; i < 120; i++)
    {
        size_t size = 0;
        out[i] = frame_sized_by_encoding(enc, &size);
        out[i].data = (uint8_t *)malloc(size);
    }

    uint32_t out_count = 0;
    while (true)
    {
        std::cout << "In Loop" << std::endl;

        if (fb->blocking_pop_frames(enc, out, &out_count, 120, 2000))
        {
            std::cout << "Recieved " << out_count << " frames!" << std::endl;
            std::cout << "Leading Byte:" << std::hex << (int)out[0].data[0] << std::dec << std::endl;
        }
        else
        {
            std::cout << "Did not recieve frames." << std::endl;
        }
    }
}

int main()
{
    std::cout << "Running..." << std::endl;
    shared_mem_client::SharedMemoryFrameBuffer fb{};

    shared_mem_client::Encoding enc = shared_mem_client::new_video_encoding(1000, 1000, 1080, 1920, 60);

    size_t size = 0;
    shared_mem_client::Frame f = shared_mem_client::frame_sized_by_encoding(enc, &size);

    f.data = (uint8_t *)malloc(size);
    memset(f.data, 0xF1, size);

    std::cout << "Requesting encoding..." << std::endl;

    fb.request_encoding(enc);

    fb.align_rings_to_config();

    std::thread t(mock_processor, &f, &fb);

    std::unordered_set<uint64_t> recs;

    recs.insert(enc.id);
    Encoding encs[10];
    uint8_t out_len;

    std::cout << "Starting loop..." << std::endl;
    std::cout << std::hex << enc.id << std::dec << std::endl;

    while (true)
    {
        if (fb.blocking_read_encodings(encs, &out_len, 10, 2000))
        {
            // Align immediately if an update is seen
            fb.align_rings_to_config();

            std::cout << "Got encodings: " << (int)out_len << std::endl;
            for (uint8_t i = 0; i < out_len; i++)
            {
                if (recs.find(encs[i].id) == recs.end())
                {
                    recs.insert(encs[i].id);
                    auto t = std::thread(mock_reciever, encs[i], &fb);
                    t.detach();
                }
            }
        }
        else
        {
            std::cout << "Did not find any new encodings" << std::endl;
        }
    }

    return 0;
}