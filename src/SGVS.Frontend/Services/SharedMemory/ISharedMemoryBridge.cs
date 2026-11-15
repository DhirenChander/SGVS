using SharedMemLibrary;

namespace SGVS.Frontend.Services.SharedMemory;

// the frontend's end of the shared memory the processing and networking modules also use.
// right now that is just the "I am sharing my screen" flag in the shared config
public interface ISharedMemoryBridge : IDisposable
{
    bool IsSharingVideo { get; }

    // throws InvalidOperationException if the shared memory can't be opened
    void SetSharingVideo(bool sharing);
}

// the real thing, Windows only (the library uses named file mappings and events)
public sealed class SharedMemoryBridge : ISharedMemoryBridge
{
    private readonly IFrameBuffer _buffer;

    // opens the shared frame buffer. injectable for tests, normally this is the native one
    public SharedMemoryBridge(IFrameBuffer? buffer = null)
    {
        _buffer = buffer ?? new GCFrameBuffer();
    }

    public bool IsSharingVideo => _buffer.GetSharingVideo();

    public void SetSharingVideo(bool sharing) => _buffer.SetShareVideo(sharing);

    public void Dispose() => (_buffer as IDisposable)?.Dispose();

    // what the app uses: the real bridge on Windows, a stand-in elsewhere
    public static ISharedMemoryBridge Create() =>
        OperatingSystem.IsWindows() ? new SharedMemoryBridge() : new NullSharedMemoryBridge();
}

// keeps the flag in memory only, for platforms where the shared memory doesn't exist.
// lets the UI run on macOS/Linux while developing
public sealed class NullSharedMemoryBridge : ISharedMemoryBridge
{
    public bool IsSharingVideo { get; private set; }

    public void SetSharingVideo(bool sharing) => IsSharingVideo = sharing;

    public void Dispose()
    {
    }
}
