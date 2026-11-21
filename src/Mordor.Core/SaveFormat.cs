using System.Text.Json;

namespace Mordor.Core;

/// <summary>
/// Which version of the save layout a saves folder holds, so the same saves can move between a PC, a
/// Linux machine and a browser without an older game spoiling what a newer one wrote. A port
/// necessity: the original's MDATA files never changed shape.
///
/// The folder carries <c>saves-format.json</c>: the layout's number (<see cref="Current"/>) and the
/// version that last wrote it. A game whose own number is lower refuses the folder at the menu, with
/// nothing changed - an old game reading a new save would drop what it does not know of the next time
/// it saved, and the save is the player's. A folder with no marker, or an older number, is this
/// layout or an earlier one this game reads, and is stamped with <see cref="Current"/> when played.
///
/// <see cref="Current"/> goes up whenever a saved type gains, loses or renames a field. The self-check
/// keeps a fingerprint of every saved type's fields and fails until it is raised (SelfCheckProbe,
/// SafeSaveChecks), so the number cannot be forgotten.
/// </summary>
public static class SaveFormat
{
    /// <summary>The save layout this game writes. Raise it with any change to a saved type's fields.</summary>
    public const int Current = 1;

    public const string FileName = "saves-format.json";

    public static string PathFor(string savesDir) => Path.Combine(savesDir, FileName);

    private sealed class Marker
    {
        public int Format { get; set; }
        public string WrittenBy { get; set; } = "";
    }

    /// <summary>The folder's layout number and the version that wrote it; (0, "") with no marker or one that will not read.</summary>
    public static (int Format, string WrittenBy) Read(string savesDir)
    {
        try
        {
            string p = PathFor(savesDir);
            if (!File.Exists(p)) return (0, "");
            var m = JsonSerializer.Deserialize<Marker>(File.ReadAllText(p));
            return m == null ? (0, "") : (m.Format, m.WrittenBy ?? "");
        }
        catch { return (0, ""); }
    }

    /// <summary>The folder was written by a newer game than this one, which must leave it alone.</summary>
    public static bool TooNew(string savesDir, out string writtenBy)
    {
        var (format, by) = Read(savesDir);
        writtenBy = by;
        return format > Current;
    }

    /// <summary>
    /// The folder marked as this layout, written by <paramref name="version"/> - not when a newer game
    /// wrote it, and not when the marker already says so. Never throws.
    /// </summary>
    public static void Stamp(string savesDir, string version)
    {
        try
        {
            var (format, by) = Read(savesDir);
            if (format > Current || (format == Current && by == version)) return;
            if (!Directory.Exists(savesDir)) return;   // nothing saved yet: the first save makes the folder
            SafeFile.WriteAllText(PathFor(savesDir),
                JsonSerializer.Serialize(new Marker { Format = Current, WrittenBy = version }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
