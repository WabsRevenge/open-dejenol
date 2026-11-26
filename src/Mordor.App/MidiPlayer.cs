using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MeltySynth;

namespace Mordor.App;

/// <summary>
/// The MIDI player the original's <c>mciMusic</c> control was, as a software synthesizer
/// (MeltySynth, playing a SoundFont) out through <see cref="AudioOut"/>: the music where there is no
/// MIDI player to ask (Linux, Wine), and on Windows when the settings choose it. Open, play, stop
/// and "is it playing" are all the game asks of it.
/// </summary>
internal sealed class MidiPlayer : IMusicPlayer, IDisposable
{
    public const int Rate = 44100;

    private readonly object _gate = new();
    private readonly MidiFileSequencer _sequencer;
    private AudioOut? _out;
    private bool _playing;
    private float[] _left = new float[2048], _right = new float[2048];
    private long _rendered, _nextReport;

    private MidiPlayer(Synthesizer synth) => _sequencer = new MidiFileSequencer(synth);

    /// <summary>
    /// The SoundFont to play with: <c>MORDOR_SOUNDFONT</c>, else the first <c>.sf2</c> in a
    /// <c>soundfont</c> folder beside the game's own files; null when there is none.
    /// </summary>
    public static string? FindSoundFont()
    {
        string? env = Environment.GetEnvironmentVariable("MORDOR_SOUNDFONT");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        string dir = Path.Combine(AppContext.BaseDirectory, "soundfont");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.sf2").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>The SoundFont read in the background, from start-up, so the first tune need not wait for it.</summary>
    public static Task<MidiPlayer?> LoadAsync() => Task.Run(() =>
    {
        string? path = FindSoundFont();
        if (path == null) return null;
        try
        {
            // reverb and chorus off: the SoundFont was chosen by ear against Windows' player that way
            var settings = new SynthesizerSettings(Rate) { EnableReverbAndChorus = false };
            return new MidiPlayer(new Synthesizer(path, settings));
        }
        catch { return (MidiPlayer?)null; }
    });

    public bool Play(string path, byte[] bytes) => Play(bytes);

    /// <summary><c>Open</c> then <c>Play</c>: the file from its start, once.</summary>
    public bool Play(byte[] midi)
    {
        MidiFile file;
        try { file = new MidiFile(new MemoryStream(midi)); } catch (Exception e) { Audio.Trace("MIDI file refused: " + e.Message); return false; }
        lock (_gate)
        {
            _sequencer.Play(file, false);
            _playing = true;
        }
        _out ??= AudioOut.Open(AudioOut.F32, 2, Rate, Fill);
        Audio.Trace($"synth Play: {file.Length.TotalSeconds:F0}s tune, output {(_out != null ? "open" : "FAILED " + AudioOut.LastError)}");
        return _out != null;
    }

    /// <summary><c>Stop</c>: silent at once, every note cut.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _sequencer.Stop();
            _playing = false;
        }
    }

    /// <summary><c>Close</c>: stopped, and the device given back until the next <see cref="Play"/>.</summary>
    public void Close()
    {
        Audio.Trace("synth Close");
        Stop();
        _out?.Dispose();
        _out = null;
    }

    /// <summary>MCI_MODE_PLAY: started and not yet at the end of the file.</summary>
    public bool Playing
    {
        get { lock (_gate) return _playing && !_sequencer.EndOfSequence; }
    }

    private void Fill(Span<byte> bytes)
    {
        var output = MemoryMarshal.Cast<byte, float>(bytes);
        int frames = output.Length / 2;
        if (_left.Length < frames) { _left = new float[frames]; _right = new float[frames]; }
        var l = _left.AsSpan(0, frames);
        var r = _right.AsSpan(0, frames);
        lock (_gate)
        {
            _sequencer.Render(l, r);
            _rendered += frames;
            if (_rendered >= _nextReport)
            {
                _nextReport += Rate * 5;
                Audio.Trace($"synth: {_rendered / (double)Rate:F0}s out, at {_sequencer.Position.TotalSeconds:F0}s, playing={_playing} end={_sequencer.EndOfSequence} voices={_sequencer.Synthesizer.ActiveVoiceCount}");
            }
        }
        for (int i = 0; i < frames; i++)
        {
            output[2 * i] = l[i];
            output[2 * i + 1] = r[i];
        }
    }

    public void Dispose() => Close();
}
