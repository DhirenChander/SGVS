#include "screen_capture.hpp"

// Gets stuff from the GPU and makes it workable in C++
#include <windows.graphics.capture.interop.h>
// GPU API
#include <windows.graphics.directx.direct3d11.interop.h>
// For stuff on a lower level than D3D offers in its header
#include <dxgi1_2.h>
// Error handling
#include <stdexcept>

namespace winrt {
    using namespace Windows::Graphics;
    using namespace Windows::Graphics::Capture;
    using namespace Windows::Graphics::DirectX;
    using namespace Windows::Graphics::DirectX::Direct3D11;
}

using Microsoft::WRL::ComPtr;

// Stop running when ScreenCapture object is destroyed.
ScreenCapture::~ScreenCapture() { Stop(); }

// Determines if the runtime env supports Windows.Graphics.Capture
bool ScreenCapture::IsSupported(){
    return winrt::GraphicsCaptureSession::IsSupported();
}

bool ScreenCapture::Start(HMONITOR monitor, FrameCallback callback){
    m_callback = std::move(callback);

    // Create a Direct3D11 Device
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 
                                    flags, nullptr, 0, D3D11_SDK_VERSION, 
                                    &m_d3dDevice, nullptr, &m_d3dContext);
    if (FAILED(hr)) return false;

    // Wrap the device as a WinRT IDirect3DDevice.
    ComPtr<IDXGIDevice> dxgiDevice;
    if (FAILED(m_d3dDevice.As(&dxgiDevice))) return false;
 
    winrt::com_ptr<::IInspectable> inspectable;
    hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.Get(), inspectable.put());
    if (FAILED(hr)) return false;
    m_winrtDevice = inspectable.as<winrt::IDirect3DDevice>();
 
    // 3. Create a capture item for the monitor with interop
    auto interop = winrt::get_activation_factory<winrt::GraphicsCaptureItem,
                                                 IGraphicsCaptureItemInterop>();
    // Intellisence doesn't like this, but guid and IID are the exact same thing so it'll work anyways.                                  
    hr = interop->CreateForMonitor(monitor,
                                   winrt::guid_of<winrt::GraphicsCaptureItem>(),
                                   winrt::put_abi(m_item));
    if (FAILED(hr)) return false;
 
    m_lastSize = m_item.Size();
 
    // Frame pool. FrameArrived fires on a worker thread.
    m_framePool = winrt::Direct3D11CaptureFramePool::CreateFreeThreaded(
        m_winrtDevice,
        winrt::DirectXPixelFormat::B8G8R8A8UIntNormalized,
        2,  // buffer count
        m_lastSize);
 
    m_frameArrived = m_framePool.FrameArrived(
        winrt::auto_revoke,
        [this](const winrt::Direct3D11CaptureFramePool& pool,
               const winrt::Windows::Foundation::IInspectable&) {
            OnFrameArrived(pool);
        });
 
    // Start capturing.
    m_session = m_framePool.CreateCaptureSession(m_item);
    m_session.IsCursorCaptureEnabled(true);
    m_session.StartCapture();
    return true;

}

void ScreenCapture::OnFrameArrived(const winrt::Direct3D11CaptureFramePool& pool) {
    auto frame = pool.TryGetNextFrame();
    if (!frame) return;
 
    // If the monitor resolution changed, rebuild the pool at the new size.
    auto contentSize = frame.ContentSize();
    bool resized = contentSize.Width != m_lastSize.Width ||
                   contentSize.Height != m_lastSize.Height;
 
    // Pull the underlying ID3D11Texture2D out of the WinRT surface.
    auto access = frame.Surface().as<Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    ComPtr<ID3D11Texture2D> texture;
    if (SUCCEEDED(access->GetInterface(IID_PPV_ARGS(&texture))) && m_callback) {
        m_callback(texture.Get(), contentSize.Width, contentSize.Height,
                   frame.SystemRelativeTime().count());
    }
 
    if (resized) {
        m_lastSize = contentSize;
        pool.Recreate(m_winrtDevice,
                      winrt::DirectXPixelFormat::B8G8R8A8UIntNormalized,
                      2, m_lastSize);
    }
}


void ScreenCapture::Stop() {
    m_frameArrived.revoke();
    if (m_session)   { m_session.Close();   m_session = nullptr; }
    if (m_framePool) { m_framePool.Close(); m_framePool = nullptr; }
    m_item = nullptr;
    m_winrtDevice = nullptr;
    m_d3dContext.Reset();
    m_d3dDevice.Reset();
}
