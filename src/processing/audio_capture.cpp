#include "audio_capture.hpp"
#include <cstdio>
#include <cstdlib>
#include <cstring>

#ifdef _WIN32
#include <avrt.h>
#include <windows.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#endif

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

#define BUFFER_SECONDS 4

#ifdef _WIN32
// Generic audio format to windows specific format
WAVEFORMATEX windows_format(AudioFormat format)
{
    WAVEFORMATEX fmt = WAVEFORMATEX{};
    fmt.wFormatTag = WAVE_FORMAT_PCM;
    fmt.nChannels = format.multiChannel ? 2 : 1;
    fmt.nSamplesPerSec = format.samplesPerSec;
    fmt.wBitsPerSample = format.bitsPerSamp;
    fmt.nBlockAlign = fmt.nChannels * fmt.wBitsPerSample / 8;
    fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;
    return fmt;
}

static AudioFormat from_windows_format(WAVEFORMATEX wformat)
{
    AudioFormat fmt;
    fmt.bitsPerSamp = wformat.wBitsPerSample;
    fmt.multiChannel = wformat.nChannels > 1;
    fmt.samplesPerSec = wformat.nSamplesPerSec;
    return fmt;
}

// Generated with Claude
// Creates the audio client
static IAudioClient *ActivateProcessLoopback(DWORD pid)
{
    AUDIOCLIENT_ACTIVATION_PARAMS params = {};
    params.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;

    if (pid == 0)
    {
        // TODO: Ensure this excludes all audio produced by SGVS
        params.ProcessLoopbackParams.TargetProcessId = GetCurrentProcessId();
        params.ProcessLoopbackParams.ProcessLoopbackMode =
            PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE;
    }
    else
    {
        params.ProcessLoopbackParams.TargetProcessId = pid;
        params.ProcessLoopbackParams.ProcessLoopbackMode =
            PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
    }

    PROPVARIANT pv = {};
    pv.vt = VT_BLOB;
    pv.blob.cbSize = sizeof(params);
    pv.blob.pBlobData = reinterpret_cast<uint8_t *>(&params);

    auto handler = Make<ActivationHandler>();
    Microsoft::WRL::ComPtr<IActivateAudioInterfaceAsyncOperation> op;

    HRESULT hr = ActivateAudioInterfaceAsync(
        VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
        __uuidof(IAudioClient), &pv, handler.Get(), &op);
    if (FAILED(hr))
    {
        fprintf(stderr, "ActivateAudioInterfaceAsync failed: 0x%08lX\n", (unsigned long)hr);
        return nullptr;
    }

    WaitForSingleObject(handler->doneEvent, INFINITE);
    if (FAILED(handler->activateResult))
    {
        fprintf(stderr, "Activate handler failed: 0x%08lX\n", (unsigned long)handler->activateResult);
        return nullptr;
    }

    IAudioClient *client = handler->client.Detach(); // caller owns it
    return client;
}

static IAudioClient *ActivateDeviceLoopback(LPWSTR device_id)
{
    // TODO: Implement device selection beyond default device
    ComPtr<IMMDeviceEnumerator> enumerator;
    ComPtr<IMMDevice> device;
    IAudioClient *client = nullptr;
    if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator))) ||
        FAILED(enumerator->GetDefaultAudioEndpoint(eCapture, eConsole, &device)) ||
        FAILED(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void **)&client)))
    {
        fprintf(stderr, "Could not open the default microphone\n");
        return nullptr;
    }
    return client; // caller owns it
}
#endif

ProcessAudioReader::ProcessAudioReader(uint32_t pid, AudioFormat format)
{
#ifdef _WIN32
    // Need to initialize before creating the audio client
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    m_client = ActivateProcessLoopback((DWORD)pid);
    m_stream_flags = AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
    m_fmt = windows_format(format);
#endif
}

ProcessAudioReader::ProcessAudioReader(const wchar_t *device_id)
{
#ifdef _WIN32
    // Need to initialize before creating the audio client
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    m_client = ActivateDeviceLoopback(const_cast<LPWSTR>(device_id));
    if (m_client == nullptr)
        return;
    m_stream_flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK;

    // Get default device format instead of taking format as input
    WAVEFORMATEX *temp_fmt = nullptr;
    HRESULT hr = m_client->GetMixFormat(&temp_fmt);
    if (FAILED(hr))
    {
        fprintf(stderr, "Failed to get mix format: 0x%08lX\n", (unsigned long)hr);
        m_client->Release();
        m_client = nullptr;
        CoUninitialize();
        return;
    }
    // TODO: See if we want to enable higher bitrate mic audio
    // Force audio into 16 bit format
    AudioFormat temp_afmt = from_windows_format(*temp_fmt);
    temp_afmt.bitsPerSamp = 16;
    m_fmt = windows_format(temp_afmt);
    CoTaskMemFree(temp_fmt);
#endif
}

ProcessAudioReader::~ProcessAudioReader()
{
    stop();
    free(m_buffer);
}

void ProcessAudioReader::setup_buffer(AudioFormat format)
{
    free(m_buffer);
    size_t buffSize = BUFFER_SECONDS * format.samplesPerSec * (format.multiChannel ? 2 : 1) * format.bitsPerSamp / 8;
    m_buffer = (uint8_t *)malloc(buffSize);
    m_buffHead = 0;
    m_buffSize = buffSize;
}

bool ProcessAudioReader::start(uint32_t buffer_time, Callback callback)
{
    // already capturing: setup_buffer would free the ring under the capture thread
    if (m_thread.joinable())
        return false;
    m_cb = std::move(callback);
#ifdef _WIN32
    setup_buffer(from_windows_format(m_fmt));
    return this->start_capture_windows(buffer_time);
#endif

    return false;
}

void ProcessAudioReader::stop()
{
#ifdef _WIN32
    this->stop_capture_windows();
#endif
}

#ifdef _WIN32

// Generated with Claude
bool ProcessAudioReader::start_capture_windows(uint32_t buffer_time_100ns)
{
    if (m_client == nullptr)
        return false; // activation failed in the constructor

    m_audioEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr); // auto-reset
    m_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);   // manual-reset

    HRESULT hr = m_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        m_stream_flags,
        buffer_time_100ns,
        0, &m_fmt, nullptr);

    if (FAILED(hr))
    {
        fprintf(stderr, "Client initialization failed: 0x%08lX\n", (unsigned long)hr);
        fprintf(stderr, "tag=%u ch=%u rate=%lu bits=%u align=%u avg=%lu cb=%u\n",
                m_fmt.wFormatTag, m_fmt.nChannels, m_fmt.nSamplesPerSec,
                m_fmt.wBitsPerSample, m_fmt.nBlockAlign,
                m_fmt.nAvgBytesPerSec, m_fmt.cbSize);
        return false;
    }

    m_client->SetEventHandle(m_audioEvent);

    // Creates the audio capture interface
    m_client->GetService(IID_PPV_ARGS(&m_capture));

    m_thread = std::thread(&ProcessAudioReader::capture_process_audio_windows, this);
    return true;
}

// Generated with Claude
void ProcessAudioReader::stop_capture_windows()
{
    if (!m_thread.joinable())
        return;
    SetEvent(m_stopEvent);
    m_thread.join();
    m_client->Stop();
    m_capture->Release();
    CloseHandle(m_audioEvent);
    CloseHandle(m_stopEvent);
}

// Generated with Claude
void ProcessAudioReader::capture_process_audio_windows()
{
    // TODO: See if this is needed
    // Optional: raise thread priority for glitch-free audio
    DWORD taskIndex = 0;
    HANDLE mmcss = AvSetMmThreadCharacteristicsW(L"Audio", &taskIndex); // link avrt.lib, include <avrt.h>

    m_client->Start();
    HANDLE handles[2] = {m_stopEvent, m_audioEvent};
    const size_t frameSize = size_t(m_fmt.nBlockAlign);

    DWORD w = WaitForMultipleObjects(2, handles, FALSE, AUDIO_WAIT_MS);

    while (w != WAIT_OBJECT_0)
    {
        if (w == WAIT_TIMEOUT)
        {
            w = WaitForMultipleObjects(2, handles, FALSE, AUDIO_WAIT_MS);
            continue;
        }

        // Drain every queued packet, not just one
        uint32_t packetFrames = 0;
        bool failed = false;
        while (SUCCEEDED(m_capture->GetNextPacketSize(&packetFrames)) && packetFrames > 0)
        {
            uint8_t *data = nullptr;
            uint32_t frames = 0;
            DWORD flags = 0;

            HRESULT hr = m_capture->GetBuffer(&data, &frames, &flags, nullptr, nullptr);
            if (FAILED(hr)) // e.g. AUDCLNT_E_DEVICE_INVALIDATED
            {
                fprintf(stderr, "Failed to get buffer: 0x%08lX\n", (unsigned long)hr);
                failed = true;
                break;
            }

            handle_audio_buffer(data, frames, frameSize, flags);
            m_capture->ReleaseBuffer(frames);
        }

        if (failed)
            break;

        w = WaitForMultipleObjects(2, handles, FALSE, AUDIO_WAIT_MS);
    }

    if (mmcss)
        AvRevertMmThreadCharacteristics(mmcss);
    CoUninitialize();
}

#endif

static bool flags_signal_empty(uint32_t flags)
{
#ifdef _WIN32
    return (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0; // Silent flag means buffer contents are empty
#else
    (void)flags;
    return false;
#endif
}

void ProcessAudioReader::handle_audio_buffer(uint8_t *data, uint32_t frames, const size_t frameSize, uint32_t flags)
{
    size_t dataSize = size_t(frames) * frameSize;

    // a packet bigger than the whole ring can't be kept
    if (dataSize > m_buffSize)
        return;
    // a packet that would run past the end of the ring starts again at 0, so the callback always gets
    // contiguous frames (main reads fCount frames from get_buffered_frame(ind, 0) in one go)
    if (size_t(m_buffHead) + dataSize > m_buffSize)
        m_buffHead = 0;

    if (flags_signal_empty(flags) || data == nullptr)
    {
        std::memset(&m_buffer[m_buffHead], 0, dataSize);
    }
    else
    {
        std::memcpy(&m_buffer[m_buffHead], data, dataSize);
    }

    m_cb(m_buffHead, frames, frameSize);
    m_buffHead = uint32_t((size_t(m_buffHead) + dataSize) % m_buffSize);
}

uint8_t *ProcessAudioReader::get_buffered_frame(uint32_t headByte, uint16_t fInd)
{
    uint32_t ind = m_fmt.nBlockAlign * fInd;
    return &m_buffer[(headByte + ind) % m_buffSize];
}