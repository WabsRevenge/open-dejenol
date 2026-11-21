using System;
using System.Runtime.InteropServices;

namespace Mordor.App;

/// <summary>
/// One playback stream on the default output device, through SDL3 - the same code on Windows
/// (x64 and ARM64) and Linux. SDL asks for more sound from its own audio thread, and the
/// <c>fill</c> handed to <see cref="Open"/> writes it; SDL converts the format and rate to the
/// device's and mixes every open stream, so the effects and the music each have one.
///
/// While open it plays all the time, silence when the fill has nothing, as WaveMix held the wave
/// device from <c>WaveMixActivate(True)</c> until <c>WaveMixActivate(False)</c>.
/// </summary>
internal sealed class AudioOut : IDisposable
{
    public const int S16 = 0x8010;   // SDL_AUDIO_S16LE
    public const int F32 = 0x8120;   // SDL_AUDIO_F32LE

    /// <summary>Writes the next <paramref name="bytes"/> of sound.</summary>
    public delegate void Filler(Span<byte> bytes);

    private readonly Filler _fill;
    private readonly int _frameBytes;
    private readonly StreamCallback _callback;   // kept here so the collector leaves it alone
    private IntPtr _stream;
    private byte[] _buf = new byte[8192];
    private IntPtr _native;
    private int _nativeLength;
    private bool _disposed;

    private AudioOut(Filler fill, int frameBytes)
    {
        _fill = fill;
        _frameBytes = frameBytes;
        _callback = OnMore;
    }

    /// <summary>
    /// A stream of <paramref name="channels"/> interleaved samples in <paramref name="format"/> at
    /// <paramref name="rate"/>, playing; null when there is no SDL or no device.
    /// </summary>
    public static AudioOut? Open(int format, int channels, int rate, Filler fill)
    {
        if (!EnsureInit()) return null;
        try
        {
            int sampleBytes = format == F32 ? 4 : 2;
            var o = new AudioOut(fill, sampleBytes * channels);
            var spec = new AudioSpec { format = format, channels = channels, freq = rate };
            o._stream = SDL_OpenAudioDeviceStream(DefaultPlayback, ref spec,
                Marshal.GetFunctionPointerForDelegate(o._callback), IntPtr.Zero);
            if (o._stream == IntPtr.Zero) { LastError = Error(); return null; }
            SDL_ResumeAudioStreamDevice(o._stream);
            return o;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            LastError = e.Message;
            return null;
        }
    }

    /// <summary>Why the last <see cref="Open"/> gave null, for the log.</summary>
    public static string? LastError { get; private set; }

    /// <summary>SDL's audio thread wants <paramref name="additional"/> more bytes.</summary>
    private void OnMore(IntPtr userdata, IntPtr stream, int additional, int total)
    {
        try
        {
            if (additional <= 0) return;
            int n = (additional + _frameBytes - 1) / _frameBytes * _frameBytes;
            if (_buf.Length < n) _buf = new byte[n];
            var span = _buf.AsSpan(0, n);
            _fill(span);
            if (_nativeLength < n)
            {
                if (_native != IntPtr.Zero) Marshal.FreeHGlobal(_native);
                _native = Marshal.AllocHGlobal(n);
                _nativeLength = n;
            }
            Marshal.Copy(_buf, 0, _native, n);
            SDL_PutAudioStreamData(stream, _native, n);
        }
        catch (Exception e) { Audio.Trace("audio callback threw: " + e); }   // an exception must not unwind into SDL's thread
    }

    /// <summary>The stream and its device closed; SDL's thread is out of the callback when this returns.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_stream != IntPtr.Zero) SDL_DestroyAudioStream(_stream); } catch { }
        _stream = IntPtr.Zero;
        if (_native != IntPtr.Zero) Marshal.FreeHGlobal(_native);
        _native = IntPtr.Zero;
    }

    // ---- SDL3 ----
    private static bool? _init;
    private static readonly object _initGate = new();

    private static bool EnsureInit()
    {
        lock (_initGate)
        {
            if (_init is bool done) return done;
            try
            {
                SDL_SetHint("SDL_APP_NAME", "OpenDejenol");
                _init = SDL_InitSubSystem(InitAudio);
                if (_init == false) LastError = Error();
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                LastError = e.Message;
                _init = false;
            }
            return _init.Value;
        }
    }

    private static string Error()
    {
        try { return Marshal.PtrToStringUTF8(SDL_GetError()) ?? ""; } catch { return ""; }
    }

    private const string Lib = "SDL3";
    private const uint InitAudio = 0x00000010;
    private const uint DefaultPlayback = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioSpec
    {
        public int format;
        public int channels;
        public int freq;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StreamCallback(IntPtr userdata, IntPtr stream, int additional, int total);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_OpenAudioDeviceStream(uint devid, ref AudioSpec spec, IntPtr callback, IntPtr userdata);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_ResumeAudioStreamDevice(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_PutAudioStreamData(IntPtr stream, IntPtr buf, int len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_DestroyAudioStream(IntPtr stream);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();
}
