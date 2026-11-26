using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mordor.App;

/// <summary>
/// What the game asks of its MIDI player, <c>mciMusic</c>: open and play a file, stop, close, and
/// whether it is playing. Two players answer: Windows' own (<see cref="MciMusic"/>) and the port's
/// synthesizer (<see cref="MidiPlayer"/>); <see cref="MusicPlayers"/> chooses.
/// </summary>
internal interface IMusicPlayer
{
    /// <summary>Open and play <paramref name="bytes"/>, the file at <paramref name="path"/> as it is to be played.</summary>
    bool Play(string path, byte[] bytes);
    void Stop();
    void Close();
    bool Playing { get; }
}

/// <summary>
/// Windows' own MIDI player through MCI (winmm.dll), as the original's <c>mciMusic</c> control
/// was - the music Windows players have always heard, through Windows' MIDI synth.
/// </summary>
internal sealed class MciMusic : IMusicPlayer
{
    public bool Play(string path, byte[] bytes)
    {
        Mci("stop bgm"); Mci("close bgm");
        Mci($"open \"{FileFor(path, bytes)}\" type sequencer alias bgm");
        Mci("play bgm");
        return true;
    }

    /// <summary>
    /// The file to hand the sequencer: the game's own, or when its bytes are to be played changed
    /// (THEME.MID's Expression, <see cref="Mordor.Core.MidiExpression"/>) a copy in the temp folder.
    /// The game folder is never written.
    /// </summary>
    private static string FileFor(string path, byte[] bytes)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return path;
            string dir = Path.Combine(Path.GetTempPath(), "OpenDejenol");
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, Path.GetFileName(path));
            File.WriteAllBytes(copy, bytes);
            return copy;
        }
        catch { return path; }
    }

    public void Stop() => Mci("stop bgm");

    public void Close() { Mci("stop bgm"); Mci("close bgm"); }

    /// <summary><c>mciMusic.Mode = 526</c>, MCI_MODE_PLAY.</summary>
    public bool Playing
    {
        get
        {
            var sb = new StringBuilder(64);
            try { mciSendString("status bgm mode", sb, sb.Capacity, IntPtr.Zero); } catch { return false; }
            return sb.ToString().Trim() == "playing";
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? ret, int retLen, IntPtr callback);

    private static void Mci(string cmd)
    {
        try { mciSendString(cmd, null, 0, IntPtr.Zero); } catch { }
    }
}

/// <summary>
/// Which MIDI player plays the music. On Windows it is Windows' own unless the port's settings say
/// <c>Music=Soundfont</c> (or <c>MORDOR_MUSIC=soundfont</c>); everywhere else - where there is no
/// MIDI player to ask - it is the port's synthesizer with the SoundFont shipped beside the game.
/// </summary>
internal static class MusicPlayers
{
    public const string SettingKey = "Music";
    public const string SoundfontValue = "Soundfont";

    /// <summary>The port's settings folder, set at start-up, where settings.ini says which player.</summary>
    public static string? SavesDir { get; set; }

    /// <summary>The Options window's Use built-in soundfont box, once it has been changed this run.</summary>
    public static bool? Chosen { get; set; }

    public static bool UseSynth()
    {
        if (!OperatingSystem.IsWindows()) return true;
        if (Chosen is bool chosen) return chosen;
        string? env = Environment.GetEnvironmentVariable("MORDOR_MUSIC");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim().Equals("soundfont", StringComparison.OrdinalIgnoreCase);
        if (SavesDir is not string dir) return false;
        try
        {
            return Mordor.Core.Ini.Read(Mordor.Core.PortSettings.PathFor(dir))
                .Text("Loading Information", SettingKey).Trim().Equals(SoundfontValue, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
