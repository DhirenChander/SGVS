// Generated Using Claude
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharedMemLibrary;

[StructLayout(LayoutKind.Sequential)]
public struct VideoEncoding
{
    public ushort max_bit_rate;
    public ushort min_bit_rate;
    public ushort frame_height;
    public ushort frame_width;
    public byte frame_rate;
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioEncoding
{
    public byte is_input; // C bool (byte keeps the struct blittable)
}

public enum EncodingType : int
{
    Video,
    Audio
}

public enum EncodingDir : int {
    Incoming,
    Outgoing
}

[StructLayout(LayoutKind.Explicit)]
public struct EncodingData
{
    [FieldOffset(0)] public VideoEncoding video_enc;
    [FieldOffset(0)] public AudioEncoding audio_enc;
}

[StructLayout(LayoutKind.Sequential)]
public struct Encoding
{
    public EncodingData data;
    public ulong id;
    public EncodingType type;
    public EncodingDir dir;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Config
{
    public const int MaxEncodings = 10;
    public Encoding* encodings;
    public Encoding front_end_video;
    public uint video_selector; // PID when is_process, Screen ID otherwise
    public byte is_process;     // C bool
    public byte sharing_screen; // C bool
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Frame
{
    public Encoding encoding;
    public nuint size;
    public byte* data;
}

// Safe Verion of Unsafe Frame
public struct GCFrame {
    public Frame raw;
    public required byte[] data;

    public GCFrame(Encoding enc) {
        GCFrame gcF;

        unsafe {
            Frame f = SharedMemLib.FrameSizedByEncoding(enc);
            ulong size = SharedMemLib.GetFrameSize(ref f);
            byte[] data = new byte[size];
            SharedMemLib.SetFrameData(ref f, ref data);

            gcF.raw = f;
            gcF.data = data;
        }
    }
}

// Define WINDOWS in your project (<DefineConstants>) when targeting Windows,
// so the layout matches the C struct's #ifdef _WIN32 fields.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FrameBuffer
{
    public byte ring_count;
    public void* rings; // RingBuffer*
    public Config config;
#if WINDOWS
    public IntPtr encs_ev, conf_ev, conf_map;
#endif
}

/*
bool new_framebuffer(FrameBuffer *out);
void free_framebuffer(FrameBuffer *fb);
bool push_frames(FrameBuffer *fb, Frame *f, uint32_t frame_count);
bool try_pop_frames(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t *out_count, uint32_t max_frame_count);
bool blocking_pop_frames(FrameBuffer *fb, Encoding encoding, Frame *out, uint32_t *out_count, uint32_t max_frame_count, uint32_t max_timeout);
bool request_encoding(FrameBuffer *fb, Encoding encoding);
bool remove_encoding(FrameBuffer *fb, Encoding encoding);
bool blocking_read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len, uint32_t max_timeout);
bool align_rings_to_config(FrameBuffer *fb);
bool read_encodings(FrameBuffer *fb, Encoding *out, uint8_t *out_len, uint8_t max_len);
*/
public interface IFrameBuffer {
    bool PushFrames(Frame[] f);
    (Frame[] frames, bool success) TryPopFrames(Encoding encoding, uint max_frame_count);
    (Frame[] frames, bool success) BlockingPopFrames(Encoding encoding, uint max_frame_count, uint max_timeout);
    bool RequestEncoding(Encoding encoding);
    bool RemoveEncoding(Encoding encoding);
    (Encoding[] encodings, bool success) BlockingReadEncodings(uint max_len, uint max_timeout);
    bool AlignRingsToConfig();
    (Encoding[] encodings, bool success) ReadEncodings(uint max_len);
}

// Generated with Claude
public class GCFrameBuffer : IFrameBuffer, IDisposable
{
    // Must be a field, not a property: `ref` can't be taken on a property.
    private FrameBuffer fb;
    private bool disposed;

    public GCFrameBuffer()
    {
        if (!SharedMemLib.NewFramebuffer(out fb))
            throw new InvalidOperationException("Failed to create shared frame buffer.");
    }

    public bool PushFrames(Frame[] f)
    {
        ThrowIfDisposed();
        if (f == null) throw new ArgumentNullException(nameof(f));
        if (f.Length == 0) return true;

        unsafe 
        {
            fixed (Frame* ptr = f)
            {
                return SharedMemLib.PushFrames(ref fb, ptr, (uint)f.Length);
            }
        }

    }

    public (Frame[] frames, bool success) TryPopFrames(Encoding encoding, uint max_frame_count)
    {
        ThrowIfDisposed();
        if (max_frame_count == 0) return (Array.Empty<Frame>(), true);

        Frame[] buffer = AllocateFrames(encoding, max_frame_count);

        bool ok;
        uint count;
        unsafe 
        {
            fixed (Frame* ptr = buffer)
            {
                ok = SharedMemLib.TryPopFrames(ref fb, encoding, ptr, out count, max_frame_count);
            }
        }

        return Finish(buffer, ok, count);
    }

    public (Frame[] frames, bool success) BlockingPopFrames(Encoding encoding, uint max_frame_count, uint max_timeout)
    {
        ThrowIfDisposed();
        if (max_frame_count == 0) return (Array.Empty<Frame>(), true);

        Frame[] buffer = AllocateFrames(encoding, max_frame_count);

        bool ok;
        uint count;
        unsafe 
        {
            fixed (Frame* ptr = buffer)
            {
                ok = SharedMemLib.BlockingPopFrames(ref fb, encoding, ptr, out count, max_frame_count, max_timeout);
            }
        }

        return Finish(buffer, ok, count);
    }

    public bool RequestEncoding(Encoding encoding)
    {
        ThrowIfDisposed();
        return SharedMemLib.RequestEncoding(ref fb, encoding);
    }

    public bool RemoveEncoding(Encoding encoding)
    {
        ThrowIfDisposed();
        return SharedMemLib.RemoveEncoding(ref fb, encoding);
    }

    public (Encoding[] encodings, bool success) BlockingReadEncodings(uint max_len, uint max_timeout)
    {
        ThrowIfDisposed();
        byte maxLen = ClampToByte(max_len);
        if (maxLen == 0) return (Array.Empty<Encoding>(), true);

        var buffer = new Encoding[maxLen];
        bool ok;
        byte outLen;
        unsafe 
        {
            fixed (Encoding* ptr = buffer)
            {
                ok = SharedMemLib.BlockingReadEncodings(ref fb, ptr, out outLen, maxLen, max_timeout);
            }
        }

        return FinishEncodings(buffer, ok, outLen);
    }

    public bool AlignRingsToConfig()
    {
        ThrowIfDisposed();
        return SharedMemLib.AlignRingsToConfig(ref fb);
    }

    public (Encoding[] encodings, bool success) ReadEncodings(uint max_len)
    {
        ThrowIfDisposed();
        byte maxLen = ClampToByte(max_len);
        if (maxLen == 0) return (Array.Empty<Encoding>(), true);

        var buffer = new Encoding[maxLen];
        bool ok;
        byte outLen;
        unsafe
        {
            fixed (Encoding* ptr = buffer)
            {
                ok = SharedMemLib.ReadEncodings(ref fb, ptr, out outLen, maxLen);
            }
        }

        return FinishEncodings(buffer, ok, outLen);
    }

    // ---- Helpers ----

    // Pre-size each output frame for the requested encoding so the native
    // side has somewhere to write the frame data.
    private static Frame[] AllocateFrames(Encoding encoding, uint count)
    {
        var frames = new Frame[count];
        for (uint i = 0; i < count; i++)
            frames[i] = SharedMemLib.FrameSizedByEncoding(encoding);
        return frames;
    }

    private static (Frame[], bool) Finish(Frame[] buffer, bool ok, uint count)
    {
        if (!ok) return (Array.Empty<Frame>(), false);
        if (count > buffer.Length) count = (uint)buffer.Length; // defensive

        if (count == buffer.Length) return (buffer, true);

        var result = new Frame[count];
        Array.Copy(buffer, result, count);
        return (result, true);
    }

    private static (Encoding[], bool) FinishEncodings(Encoding[] buffer, bool ok, byte len)
    {
        if (!ok) return (Array.Empty<Encoding>(), false);
        if (len > buffer.Length) len = (byte)buffer.Length; // defensive

        var result = new Encoding[len];
        Array.Copy(buffer, result, len);
        return (result, true);
    }

    private static byte ClampToByte(uint v) => v > byte.MaxValue ? byte.MaxValue : (byte)v;

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(GCFrameBuffer));
    }

    // ---- Cleanup ----

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;
        disposed = true;
        SharedMemLib.FreeFramebuffer(ref fb);
    }

    ~GCFrameBuffer() => Dispose(false);
}

public static unsafe class SharedMemLib
{
    const string Lib = "lib/shared_mem_client";
    const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "frame_sized_by_encoding")]
    internal static extern Frame FrameSizedByEncoding(Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_frame_data")]
    internal static extern void SetFrameData(ref Frame f, ref byte[] data);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "get_frame_size")]
    internal static extern ulong GetFrameSize(ref Frame f);

    // ---- FrameBuffer functions ----

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_framebuffer")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool NewFramebuffer(out FrameBuffer fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "free_framebuffer")]
    internal static extern void FreeFramebuffer(ref FrameBuffer fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "push_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool PushFrames(ref FrameBuffer fb, Frame* f, uint frameCount);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "try_pop_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool TryPopFrames(ref FrameBuffer fb, Encoding encoding, Frame* output, out uint outCount, uint maxFrameCount);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "blocking_pop_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool BlockingPopFrames(ref FrameBuffer fb, Encoding encoding, Frame* output, out uint outCount, uint maxFrameCount, uint maxTimeout);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "request_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool RequestEncoding(ref FrameBuffer fb, Encoding encoding);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "remove_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool RemoveEncoding(ref FrameBuffer fb, Encoding encoding);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "blocking_read_encodings")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool BlockingReadEncodings(ref FrameBuffer fb, Encoding* output, out byte outLen, byte maxLen, uint maxTimeout);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "align_rings_to_config")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool AlignRingsToConfig(ref FrameBuffer fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "read_encodings")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool ReadEncodings(ref FrameBuffer fb, Encoding* output, out byte outLen, byte maxLen);

    // ---- Encoding functions ----
    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_video_encoding")]
    public static extern Encoding NewVideoEncoding(ushort maxBitRate, ushort minBitRate, ushort frameHeight, ushort frameWidth, byte frameRate);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_audio_encoding")]
    public static extern Encoding NewAudioEncoding(bool isInput);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_encoding_outgoing")]
    public static extern void SetEncodingOutgoing(ref Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_encoding_incoming")]
    public static extern void SetEncodingIncoming(ref Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "is_audio_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool IsAudioEncoding(Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "is_video_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool IsVideoEncoding(Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "is_incoming_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool IsIncomingEncoding(Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "is_outgoing_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool IsOutgoingEncoding(Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "enc_eq")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool EncEq(Encoding a, Encoding b);
}