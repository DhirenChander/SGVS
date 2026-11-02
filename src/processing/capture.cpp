#include "capture.hpp"

#ifdef _WIN32
#include <windows.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <wrl/implements.h>
#endif

#ifdef _WIN32
WAVEFORMATEX windows_format(AudioFormat format) {
    WAVEFORMATEX fmt;
    fmt.wFormatTag      = WAVE_FORMAT_PCM;
    fmt.nChannels       = format.multiChannel ? 2 : 1;
    fmt.nSamplesPerSec  = format.samplesPerSec;
    fmt.wBitsPerSample  = format.bitsPerSamp;
    fmt.nBlockAlign     = fmt.nChannels * fmt.wBitsPerSample / 8;
    fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;
    return fmt;
}

// Generated with Claude
IAudioClient* ActivateProcessLoopback(DWORD pid) {
    AUDIOCLIENT_ACTIVATION_PARAMS params = {};
    params.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    params.ProcessLoopbackParams.TargetProcessId = pid;
    params.ProcessLoopbackParams.ProcessLoopbackMode =
        PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;

    PROPVARIANT pv = {};
    pv.vt = VT_BLOB;
    pv.blob.cbSize = sizeof(params);
    pv.blob.pBlobData = reinterpret_cast<BYTE*>(&params);

    auto handler = Make<ActivationHandler>();
    ComPtr<IActivateAudioInterfaceAsyncOperation> op;

    HRESULT hr = ActivateAudioInterfaceAsync(
        VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
        __uuidof(IAudioClient), &pv, handler.Get(), &op);
    if (FAILED(hr)) return nullptr;

    WaitForSingleObject(handler->doneEvent, INFINITE);
    if (FAILED(handler->activateResult)) return nullptr;

    IAudioClient* client = handler->client.Detach();   // caller owns it
    return client;
}
#endif

CaptureProcessAudio::CaptureProcessAudio(uint32_t pid) {
    #ifdef _WIN32
    this->m_client = ActivateProcessLoopback((DWORD)pid);
    #endif
}

bool CaptureProcessAudio::start(uint8_t *buffer, uint32_t buffer_time, AudioFormat format) {
    #ifdef _WIN32
    return this->start_capture_windows(uint8_t *buffer, uint32_t buffer_time, AudioFormat format);
    #endif

    return false;
}

bool CaptureProcessAudio::stop() {
    #ifdef _WIN32
    return this->stop_capture_windows();
    #endif

    return false;
}

#ifdef _WIN32
// Generated with Claude
bool CaptureProcessAudio::start_capture_windows(uint8_t *buffer, uint32_t buffer_time_nanos, AudioFormat format) {
    m_client = audioClient;
    m_fmt = windows_format(format);

    m_audioEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);  // auto-reset
    m_stopEvent  = CreateEventW(nullptr, TRUE,  FALSE, nullptr);  // manual-reset

    HRESULT hr = m_client->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK |
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
        AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
        AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
        buffer_time_nanos, 0, &m_fmt, nullptr);
    if (FAILED(hr)) return false;

    m_client->SetEventHandle(m_audioEvent);
    m_client->GetService(IID_PPV_ARGS(&m_capture));

    m_thread = std::thread(&CaptureProcessAudio::capture_process_audio_windows, this);
    return true;
}

// Generated with Claude
void CaptureProcessAudio::capture_process_audio_windows() {
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);

    // Optional: raise thread priority for glitch-free audio
    DWORD taskIndex = 0;
    HANDLE mmcss = AvSetMmThreadCharacteristicsW(L"Audio", &taskIndex);  // link avrt.lib, include <avrt.h>

    m_client->Start();
    HANDLE handles[2] = { m_stopEvent, m_audioEvent };
    const UINT32 frameSize = m_fmt.nBlockAlign;
    std::vector<BYTE> silence;

    for (;;) {
        DWORD w = WaitForMultipleObjects(2, handles, FALSE, 2000);
        if (w == WAIT_OBJECT_0) break;                 // stop requested
        if (w == WAIT_TIMEOUT) continue;               // nothing playing; keep waiting
        if (w != WAIT_OBJECT_0 + 1) break;             // error

        // Drain every queued packet, not just one
        UINT32 packetFrames = 0;
        while (SUCCEEDED(m_capture->GetNextPacketSize(&packetFrames)) && packetFrames > 0) {
            BYTE* data = nullptr;
            UINT32 frames = 0;
            DWORD flags = 0;

            HRESULT hr = m_capture->GetBuffer(&data, &frames, &flags, nullptr, nullptr);
            if (FAILED(hr)) goto done;                 // e.g. AUDCLNT_E_DEVICE_INVALIDATED

            if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
                // Buffer contents are undefined; substitute zeros to keep timing intact
                silence.assign(size_t(frames) * frameSize, 0);
                m_cb(silence.data(), frames);
            } else {
                m_cb(data, frames);
            }
            m_capture->ReleaseBuffer(frames);
        }
    }
    done:
        if (mmcss) AvRevertMmThreadCharacteristics(mmcss);
        CoUninitialize();
}
#endif