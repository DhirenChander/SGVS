#pragma once

// I have no idea what this is but WinRT needs it to work properly. It also needs to be included before any other Windows headers.
#include <unknwn.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>

#include <d3d11.h>
#include <wrl/client.h>

#include <cstdint>
#include <functional>

class ScreenCapture {
    public:
        using FrameCallback = std::function<void(ID3D11Texture2D* texture, int width, int height, int64_t timestamp100ns)>;
        // Explicitly defualting constructor can avoid weird issues later
        ScreenCapture() = default;
        ~ScreenCapture();

        // Fixes possible memory leaks by deleting rvalues after assignment.
        ScreenCapture(const ScreenCapture&) = delete;
        ScreenCapture& operator=(const ScreenCapture&) = delete;

        static bool IsSupported();

        bool Start(HMONITOR monitor, FrameCallback callback);
        void Stop();

        // GPU device.
        // IMPORTANT: encoder should also use this device. 
        ID3D11Device* Device() const { return m_d3dDevice.Get(); }

    private:
        void OnFrameArrived(const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& pool);
 
        Microsoft::WRL::ComPtr<ID3D11Device> m_d3dDevice;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> m_d3dContext;
        winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice m_winrtDevice{ nullptr };
 
        winrt::Windows::Graphics::Capture::GraphicsCaptureItem m_item{ nullptr };
        winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool m_framePool{ nullptr };
        winrt::Windows::Graphics::Capture::GraphicsCaptureSession m_session{ nullptr };
        winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool::FrameArrived_revoker m_frameArrived;
 
        winrt::Windows::Graphics::SizeInt32 m_lastSize{};
        FrameCallback m_callback;
};