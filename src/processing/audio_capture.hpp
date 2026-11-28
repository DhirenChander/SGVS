#pragma once

#ifdef _WIN32
#include <windows.h>
#include <audioclient.h>
#endif

#include <cstddef>
#include <cstdint>
#include <functional>
#include <thread>

#define AUDIO_WAIT_MS 2000

// 200 ms, in the 100 ns units start() takes
constexpr uint32_t DEFAULT_BUFFER_TIME = 200 * 10000;

using Callback = std::function<void(uint32_t ind, uint16_t fCount, size_t fSize)>;

struct AudioFormat
{
    bool multiChannel;
    uint32_t samplesPerSec;
    uint8_t bitsPerSamp;
};

#ifdef _WIN32
WAVEFORMATEX windows_format(AudioFormat format);
#endif

class ProcessAudioReader
{
public:
    // Blank constructor for tests
    ProcessAudioReader()
    {
    }
    // PID 0 => Read all audio (except for the SGVS process)
    ProcessAudioReader(uint32_t pid, AudioFormat format);
    ProcessAudioReader(const wchar_t *device_id);
    // Stops capturing and frees the ring buffer
    ~ProcessAudioReader();
    // Owns a capture thread and a malloc'd buffer, so a copy would free them twice
    ProcessAudioReader(const ProcessAudioReader &) = delete;
    ProcessAudioReader &operator=(const ProcessAudioReader &) = delete;
    // Starts reading audio under the given format, and calls the callback with the starting buffer index and the number of frames
    // This automatically starts running on the capture function on a separate thread. buffer_time is in 100 ns units
    bool start(uint32_t buffer_time, Callback callback);
    void stop();
    // Returns a pointer to the first byte of the frame at buffer[(head + frameSize * ind) % buffSize]
    uint8_t *get_buffered_frame(uint32_t head, uint16_t ind);

private:
    // Needed for iternal access during tests
    friend class ProcessAudioReaderTest;
#ifdef _WIN32
    IAudioClient *m_client = nullptr;
    IAudioCaptureClient *m_capture = nullptr;
    WAVEFORMATEX m_fmt{};
    HANDLE m_audioEvent = nullptr, m_stopEvent = nullptr;
    DWORD m_stream_flags = 0;
#endif
    // Constant ring buffer is allocated once and shared between callback calls
    uint8_t *m_buffer = nullptr;
    uint32_t m_buffHead = 0;
    size_t m_buffSize = 0;

    // Runner automatically runs on a separate thread
    std::thread m_thread;

    // Used to pass data in buffer to the next function
    Callback m_cb;

    void setup_buffer(AudioFormat format);
    void handle_audio_buffer(uint8_t *data, uint32_t frames, const size_t frameSize, uint32_t flags);
#ifdef _WIN32
    bool start_capture_windows(uint32_t buffer_time);
    void stop_capture_windows();
    void capture_process_audio_windows();
#endif
};