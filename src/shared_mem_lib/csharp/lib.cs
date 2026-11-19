// Generated Using Claude
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

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

public enum EncodingDir : int
{
    Incoming,
    Outgoing
}

public enum Module : int
{
    Processor = 0,
    Network = 1,
    Application = 2,
}

public enum Operation : int
{
    StartSharing = 0,
    StopSharing = 1,
    HostCode = 2,
    AnswerCode = 3,
    StartHost = 4,
    JoinCall = 5,
    ParticipantJoined = 6,
    ParticipantLeft = 7,
    LeaveCall = 8,
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Message
{
    fixed byte data[256];
    Module src, dest;
    Operation opcode;

    public readonly Module Src => src;
    public readonly Module Dest => dest;
    public readonly Operation Opcode => opcode;

    public Message(byte[] data, Module src, Module dest, Operation opcode)
    {
        this = default;
        this.src = src;
        this.dest = dest;
        this.opcode = opcode;

        if (data == null)
        {
            return;
        }

        if (data.Length > 255)
        {
            throw new ArgumentException("Data is too long to be converted to a message");
        }

        fixed (byte* p = this.data)
        {
            data.AsSpan().CopyTo(new Span<byte>(p, 256));
        }
    }

    public readonly byte[] GetData()
    {
        Message copy = this; // local copy so the fixed buffer can be addressed
        var span = new ReadOnlySpan<byte>(copy.data, 256);
        int len = span.IndexOf((byte)0);
        return span[..(len < 0 ? span.Length : len)].ToArray();
    }
};

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
    public uint256 id;
    public EncodingType type;
    public EncodingDir dir;

    public bool IsAudio() => SharedMemLib.IsAudioEncoding(this);
    public bool IsVideo() => SharedMemLib.IsVideoEncoding(this);
    public bool IsIncoming() => SharedMemLib.IsIncomingEncoding(this);
    public bool IsOutgoing() => SharedMemLib.IsOutgoingEncoding(this);
    public bool Equals(Encoding enc) => SharedMemLib.EncEq(this, enc);
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Config
{
    public const int MaxEncodings = 10;
    public Encoding* encodings;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Frame
{
    public Encoding encoding;
    public nuint size;
    public byte* data;
}

// Safe version of unsafe Frame
public struct GCFrame
{
    public Frame raw;
    public required byte[] Data;

    [SetsRequiredMembers]
    public unsafe GCFrame(Encoding enc)
    {
        ulong size = 0;
        raw = SharedMemLib.FrameSizedByEncoding(enc, &size);
        Data = new byte[size];
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct CriticalSection
{
    private IntPtr DebugInfo;      // PRTL_CRITICAL_SECTION_DEBUG
    private int LockCount;         // LONG
    private int RecursionCount;    // LONG
    private IntPtr OwningThread;   // HANDLE
    private IntPtr LockSemaphore;  // HANDLE
    private UIntPtr SpinCount;     // ULONG_PTR
}

[InlineArray(4)]
public struct uint256
{
    private ulong first;
}

unsafe struct RingBuffer
{
    void* ring, lens, data;
    uint256 id;
#if WINDOWS
    IntPtr map, ev;
#endif
};

[InlineArray(9)]
public struct MsgChans
{
    private RingBuffer buff;
}

// Define WINDOWS in your project (<DefineConstants>) when targeting Windows,
// so the layout matches the C struct's #ifdef _WIN32 fields.
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FrameBuffer
{
    public MsgChans msg_chns;
    public void* rings;  // RingBuffer*
    public void* config; // Config*
#if WINDOWS
    public IntPtr encs_ev, conf_map, conf_mu;
    public CriticalSection cs;
#endif
    public byte ring_count;
}

// Compare these against sizeof(...) printed from the C side. They must match exactly.
public static class LayoutCheck
{
    public static void Print()
    {
        Console.WriteLine($"Message:     {Unsafe.SizeOf<Message>()}");
        Console.WriteLine($"Encoding:    {Unsafe.SizeOf<Encoding>()}");
        Console.WriteLine($"Frame:       {Unsafe.SizeOf<Frame>()}");
        Console.WriteLine($"RingBuffer:  {Unsafe.SizeOf<RingBuffer>()}");
        Console.WriteLine($"FrameBuffer: {Unsafe.SizeOf<FrameBuffer>()}");
    }
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
bool push_message(FrameBuffer *fb, Message *msg);
bool blocking_pop_message(FrameBuffer *fb, Module src, Module dst, Message *out_msg);
*/
public interface IFrameBuffer
{
    bool PushFrames(Frame[] f);
    (GCFrame[] frames, bool success) TryPopFrames(Encoding encoding, uint max_frame_count);
    (GCFrame[] frames, bool success) BlockingPopFrames(Encoding encoding, uint max_frame_count, uint max_timeout);
    bool RequestEncoding(Encoding encoding);
    bool RemoveEncoding(Encoding encoding);
    (Encoding[] encodings, bool success) BlockingReadEncodings(uint max_len, uint max_timeout);
    bool AlignRingsToConfig();
    (Encoding[] encodings, bool success) ReadEncodings(uint max_len);
    bool PushMessage(Message msg);
    (Message msg, bool success) BlockingPopMessage(Module src, Module dst);
}

public unsafe class GCFrameBuffer : IFrameBuffer, IDisposable
{
    // The FrameBuffer lives in unmanaged memory: its address never changes and it is
    // never copied, which matters because it contains a CRITICAL_SECTION and ring pointers.
    private FrameBuffer* fbp;
    private bool disposed;

    public GCFrameBuffer()
    {
        fbp = (FrameBuffer*)NativeMemory.AllocZeroed((nuint)sizeof(FrameBuffer));

        if (!SharedMemLib.NewFramebuffer(fbp))
        {
            NativeMemory.Free(fbp);
            fbp = null;
            disposed = true; // so the finalizer doesn't call free_framebuffer on an uninitialized buffer
            throw new InvalidOperationException("Failed to create shared frame buffer.");
        }
    }

    public bool PushFrames(Frame[] f)
    {
        ThrowIfDisposed();
        if (f == null) throw new ArgumentNullException(nameof(f));
        if (f.Length == 0) return true;

        fixed (Frame* ptr = f)
        {
            return SharedMemLib.PushFrames(fbp, ptr, (uint)f.Length);
        }
    }

    public (GCFrame[] frames, bool success) TryPopFrames(Encoding encoding, uint max_frame_count)
        => PopCore(encoding, max_frame_count, blocking: false, timeout: 0);

    public (GCFrame[] frames, bool success) BlockingPopFrames(Encoding encoding, uint max_frame_count, uint max_timeout)
        => PopCore(encoding, max_frame_count, blocking: true, timeout: max_timeout);

    private (GCFrame[] frames, bool success) PopCore(Encoding encoding, uint max, bool blocking, uint timeout)
    {
        ThrowIfDisposed();
        if (max == 0) return (Array.Empty<GCFrame>(), true);

        var gcFrames = new GCFrame[max];
        var raws = new Frame[max];
        var handles = new GCHandle[max];
        int pinned = 0;

        try
        {
            // Build each frame and pin its managed buffer so native code can write into it.
            for (int i = 0; i < max; i++)
            {
                gcFrames[i] = new GCFrame(encoding);
                handles[i] = GCHandle.Alloc(gcFrames[i].Data, GCHandleType.Pinned);
                pinned++;

                raws[i] = gcFrames[i].raw;
                fixed (Frame* pf = &raws[i])
                {
                    SharedMemLib.SetFrameData(pf, (byte*)handles[i].AddrOfPinnedObject());
                }
            }

            bool ok;
            uint count = 0;
            fixed (Frame* ptr = raws)
            {
                ok = blocking
                    ? SharedMemLib.BlockingPopFrames(fbp, encoding, ptr, &count, max, timeout)
                    : SharedMemLib.TryPopFrames(fbp, encoding, ptr, &count, max);
            }

            if (!ok) return (Array.Empty<GCFrame>(), false);
            if (count > max) count = max; // defensive

            // Copy native-updated frame metadata (size, timestamp, ...) back into the GCFrames.
            // The data pointer refers to a buffer that is unpinned in the finally block below,
            // so null it rather than leave a dangling pointer. Use Data instead.
            for (int i = 0; i < count; i++)
            {
                gcFrames[i].raw = raws[i];
                gcFrames[i].raw.data = null;
            }

            if (count == max) return (gcFrames, true);

            var result = new GCFrame[count];
            Array.Copy(gcFrames, result, count);
            return (result, true);
        }
        finally
        {
            for (int i = 0; i < pinned; i++)
                handles[i].Free();
        }
    }

    public bool RequestEncoding(Encoding encoding)
    {
        ThrowIfDisposed();
        return SharedMemLib.RequestEncoding(fbp, encoding);
    }

    public bool RemoveEncoding(Encoding encoding)
    {
        ThrowIfDisposed();
        return SharedMemLib.RemoveEncoding(fbp, encoding);
    }

    public (Encoding[] encodings, bool success) BlockingReadEncodings(uint max_len, uint max_timeout)
    {
        ThrowIfDisposed();
        byte maxLen = ClampToByte(max_len);
        if (maxLen == 0) return (Array.Empty<Encoding>(), true);

        var buffer = new Encoding[maxLen];
        bool ok;
        byte outLen = 0;
        fixed (Encoding* ptr = buffer)
        {
            ok = SharedMemLib.BlockingReadEncodings(fbp, ptr, &outLen, maxLen, max_timeout);
        }

        return FinishEncodings(buffer, ok, outLen);
    }

    public bool AlignRingsToConfig()
    {
        ThrowIfDisposed();
        return SharedMemLib.AlignRingsToConfig(fbp);
    }

    public (Encoding[] encodings, bool success) ReadEncodings(uint max_len)
    {
        ThrowIfDisposed();
        byte maxLen = ClampToByte(max_len);
        if (maxLen == 0) return (Array.Empty<Encoding>(), true);

        var buffer = new Encoding[maxLen];
        bool ok;
        byte outLen = 0;
        fixed (Encoding* ptr = buffer)
        {
            ok = SharedMemLib.ReadEncodings(fbp, ptr, &outLen, maxLen);
        }

        return FinishEncodings(buffer, ok, outLen);
    }

    public bool PushMessage(Message msg)
    {
        ThrowIfDisposed();
        // msg is a local copy of the struct so taking its address is safe for the duration of the call
        return SharedMemLib.PushMessage(fbp, &msg);
    }

    public (Message msg, bool success) BlockingPopMessage(Module src, Module dst)
    {
        ThrowIfDisposed();
        Message outMsg = default;
        bool ok = SharedMemLib.BlockingPopMessage(fbp, src, dst, &outMsg);
        return (outMsg, ok);
    }

    // ---- Helpers ----

    // Pre-size each output frame for the requested encoding so the native
    // side has somewhere to write the frame data.
    private static Frame[] AllocateFrames(Encoding encoding, uint count)
    {
        ulong size = 0;
        var frames = new Frame[count];
        for (uint i = 0; i < count; i++)
            frames[i] = SharedMemLib.FrameSizedByEncoding(encoding, &size);
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

        if (fbp != null)
        {
            SharedMemLib.FreeFramebuffer(fbp);
            NativeMemory.Free(fbp);
            fbp = null;
        }
    }

    ~GCFrameBuffer() => Dispose(false);
}

public static unsafe class SharedMemLib
{
    const string Lib = "lib/shared_mem_client";
    const CallingConvention CC = CallingConvention.Cdecl;

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "frame_sized_by_encoding")]
    internal static extern Frame FrameSizedByEncoding(Encoding enc, ulong* out_size);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_frame_data")]
    internal static extern void SetFrameData(Frame* f, byte* data);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "get_frame_size")]
    internal static extern ulong GetFrameSize(Frame* f);

    // ---- FrameBuffer functions ----

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_framebuffer")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool NewFramebuffer(FrameBuffer* fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "free_framebuffer")]
    internal static extern void FreeFramebuffer(FrameBuffer* fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "push_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool PushFrames(FrameBuffer* fb, Frame* f, uint frameCount);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "try_pop_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool TryPopFrames(FrameBuffer* fb, Encoding encoding, Frame* output, uint* outCount, uint maxFrameCount);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "blocking_pop_frames")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool BlockingPopFrames(FrameBuffer* fb, Encoding encoding, Frame* output, uint* outCount, uint maxFrameCount, uint maxTimeout);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "request_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool RequestEncoding(FrameBuffer* fb, Encoding encoding);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "remove_encoding")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool RemoveEncoding(FrameBuffer* fb, Encoding encoding);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "blocking_read_encodings")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool BlockingReadEncodings(FrameBuffer* fb, Encoding* output, byte* outLen, byte maxLen, uint maxTimeout);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "align_rings_to_config")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool AlignRingsToConfig(FrameBuffer* fb);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "read_encodings")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool ReadEncodings(FrameBuffer* fb, Encoding* output, byte* outLen, byte maxLen);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "push_message")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool PushMessage(FrameBuffer* fb, Message* msg);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "blocking_pop_message")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool BlockingPopMessage(FrameBuffer* fb, Module src, Module dst, Message* out_msg);

    // ---- Encoding functions ----

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_video_encoding")]
    public static extern Encoding NewVideoEncoding(ushort maxBitRate, ushort minBitRate, ushort frameHeight, ushort frameWidth, byte frameRate);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "new_audio_encoding")]
    public static extern Encoding NewAudioEncoding([MarshalAs(UnmanagedType.U1)] bool isInput);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_encoding_outgoing")]
    public static extern void SetEncodingOutgoing(Encoding* enc);

    [DllImport(Lib, CallingConvention = CC, EntryPoint = "set_encoding_incoming")]
    public static extern void SetEncodingIncoming(Encoding* enc);

    // Convenience overloads so callers don't need an unsafe context.
    public static void SetEncodingOutgoing(ref Encoding enc)
    {
        fixed (Encoding* p = &enc) SetEncodingOutgoing(p);
    }

    public static void SetEncodingIncoming(ref Encoding enc)
    {
        fixed (Encoding* p = &enc) SetEncodingIncoming(p);
    }

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