#include "screen_capture.hpp"

#include <atomic>
#include <chrono>
#include <iostream>
#include <thread>

int main(){
    winrt::init_apartment(winrt::apartment_type::multi_threaded);

    if (!ScreenCapture::IsSupported()){
        std::cerr << "Windows.Graphics.Capture not supported on this system.\n";
        return 1;
    }

    HMONITOR monitor = MonitorFromPoint({0,0}, MONITOR_DEFAULTTOPRIMARY);

    std::atomic<int> frames{0};
    ScreenCapture capture;

    bool test_capture = capture.Start(monitor, [&](ID3D11Texture2D*, int w, int h, int64_t ts){
        int n = ++frames;
        if (n % 60 == 0)
            std::cout << "frame " << n <<  " (" << w << "x" << h << ") t=" << ts << "\n";
    });

    if (!test_capture){
        std::cerr << "Failed to start capture.\n";
        return 1;
    }

    std::this_thread::sleep_for(std::chrono::seconds(5));
    capture.Stop();
    std::cout << "Captured" << frames << " frames in 5s\n";

    return 0;
}