#ifdef _WIN32
#include <windows.h>
#include <audioclient.h>
#include <wrl/implements.h>
#include <wrl/client.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#endif

#include <thread>
#include <atomic>
#include <functional>
#include <vector>
#include <cstdint>

#define AUDIO_WAIT_MS 2000

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
    ProcessAudioReader()
    {
    }
    // PID 0 => Read all audio (except for the SGVS process)
    ProcessAudioReader(uint32_t pid);
    // Starts reading audio under the given format, and calls the callback with the starting buffer index and the number of frames
    // This automatically starts running on the capture function on a separate thread
    bool start(uint32_t buffer_time, AudioFormat format, Callback callback);
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
#endif
    // Constant ring buffer is allocated once and shared between callback calls
    uint8_t *m_buffer;
    uint32_t m_buffHead;
    size_t m_buffSize;

    // Runner automatically runs on a separate thread
    std::thread m_thread;
    Callback m_cb;

    void setup_buffer(AudioFormat format);
    void handle_audio_buffer(uint8_t *data, uint32_t frames, const size_t frameSize, uint32_t flags);
#ifdef _WIN32
    bool start_capture_windows(uint32_t buffer_time, AudioFormat format);
    void stop_capture_windows();
    void capture_process_audio_windows();
#endif
};

#ifdef _WIN32
using namespace Microsoft::WRL;

// Generated with Claude
// Boiler plate needed to initialize the audio client
class ActivationHandler : public RuntimeClass<RuntimeClassFlags<ClassicCom>,
                                              FtmBase, // makes it agile
                                              IActivateAudioInterfaceCompletionHandler>
{
public:
    HANDLE doneEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HRESULT activateResult = E_FAIL;
    ComPtr<IAudioClient> client;

    ~ActivationHandler() { CloseHandle(doneEvent); }

    STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation *op) override
    {
        HRESULT hrActivate = E_FAIL;
        ComPtr<IUnknown> unk;
        HRESULT hr = op->GetActivateResult(&hrActivate, &unk);
        if (SUCCEEDED(hr) && SUCCEEDED(hrActivate))
            unk.As(&client);
        activateResult = SUCCEEDED(hr) ? hrActivate : hr;
        SetEvent(doneEvent);
        return S_OK;
    }
};
#endif