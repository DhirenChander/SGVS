#ifdef _WIN32
#include <windows.h>
#include <audioclient.h>
#endif

#include <thread>
#include <atomic>
#include <functional>
#include <vector>
#include <cstdint>

using Callback = std::function<void(const uint8_t* data, uint32_t frame_count)>;

struct AudioFormat {
    bool multiChannel;
    uint32_t samplesPerSec;
    uint8_t bitsPerSamp;
};

#ifdef _WIN32
WAVEFORMATEX windows_format(AudioFormat format);
#endif

class CaptureProcessAudio {
public:
    CaptureProcessAudio(uint32_t pid);
    bool start(uint8_t *buffer, uint32_t buffer_time, AudioFormat format);
    bool stop();
private:
    #ifdef _WIN32
    IAudioClient*        m_client = nullptr;
    IAudioCaptureClient* m_capture = nullptr;
    WAVEFORMATEX         m_fmt{};
    HANDLE               m_audioEvent = nullptr, m_stopEvent = nullptr;
    #endif

    std::thread          m_thread;

    #ifdef _WIN32
    bool start_capture_windows(uint8_t *buffer, uint32_t buffer_time, AudioFormat format);
    bool stop_capture_windows();
    bool capture_process_audio_windows();
    #endif
};