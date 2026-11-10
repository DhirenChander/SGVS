// Generated Using Claude
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FrameBufferInterop;

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
}

// Encoding encodings[MAX_ENCODINGS]  (requires .NET 8+)
[InlineArray(Config.MaxEncodings)]
public struct EncodingArray
{
    private Encoding _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct Config
{
    public const int MaxEncodings = 10;
    public EncodingArray encodings;
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

public struct GCFrame {
    public Frame f;
    public byte[] data;
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

public static unsafe class Native
{
    const string Lib = "framebuff";
    const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_framebuffer")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool NewFramebuffer(out FrameBuffer fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "free_framebuffer")]
    public static extern void FreeFramebuffer(ref FrameBuffer fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "push_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool PushFrames(ref FrameBuffer fb, Frame[] f, uint frameCount);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "try_pop_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TryPopFrames(ref FrameBuffer fb, Encoding encoding, [Out] Frame[] output, uint maxFrameCount);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "blocking_pop_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool BlockingPopFrames(ref FrameBuffer fb, Encoding encoding, [Out] Frame[] output, uint maxFrameCount, uint maxTimeout);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "request_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool RequestEncoding(ref FrameBuffer fb, Encoding encoding);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "remove_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool RemoveEncoding(ref FrameBuffer fb, Encoding encoding);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "read_encodings")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool ReadEncodings(ref FrameBuffer fb, [Out] Encoding[] output, byte len);

#if WINDOWS
    [DllImport(Lib, CallingConvention = CC, EntryPoint = "poll_encodings")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool PollEncodings(ref FrameBuffer fb, [Out] Encoding[] output, byte len, out uint outErr, uint maxTimeout);
#endif

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "frame_sized_by_encoding")]
    public static extern Frame FrameSizedByEncoding(Encoding enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_frame_data")]
    public static extern void SetFrameData(ref Frame f, ref byte[] data);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "get_frame_size")]
    public static extern ulong GetFrameSize(ref Frame f);

    public static extern GCFrame(Encoding enc) {
        Frame f = FrameSizedByEncoding(enc);
        ulong size = GetFrameSize(ref f);
        byte[] data = new byte[size];
        SetFrameData(ref f, ref data);
        
        GCFrame gcF;
        gcF.f = f;
        gcF.data = data;
        return gcF;
    }

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_share_video")]
    public static extern void SetShareVideo(ref FrameBuffer fb, [MarshalAs(UnmanagedType.U1)] bool v);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "get_sharing_video")]
    public static extern bool GetSharingVideo(ref FrameBuffer fb);
    
    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_front_end_encoding")]
    void SetFrontEndEncoding(ref FrameBuffer fb, Encoding enc);
    
    [DllImport(Lib, CallingConvention = CC, EntryPoint = "get_front_end_encoding")]
    Encoding GetFrontEndEncoding(ref FrameBuffer fb);
}