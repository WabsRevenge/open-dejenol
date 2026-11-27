namespace Mordor.Core;

/// <summary>
/// The program's own per-user folders, named for the program, OpenDejenol:
/// <list type="bullet">
/// <item><c>%APPDATA%\OpenDejenol</c> (<see cref="Roaming"/>; on macOS <c>~/Library/Application
///   Support/OpenDejenol</c>, <see cref="RoamingParentFor"/>): the launch settings, the per-user
///   saves and, for those saves, the crash log and CHARSTAT.TXT (<see cref="GameFolder"/>,
///   <see cref="Saves"/>, <see cref="PortFiles"/>);</item>
/// <item><c>%LOCALAPPDATA%\OpenDejenol</c> (<see cref="Local"/>): the cache of pictures and help
///   read out of the player's copy (<see cref="Extraction.GameArt.CacheRoot"/>).</item>
/// </list>
/// Both were called "Mordor Port" before the program had its name. Nothing a player kept there is
/// lost: <see cref="MigrateAll"/>, at the program's start, renames an old folder to the new name
/// when only the old one is there, and until that has happened - or when it could not be done, a
/// file in it held open by another copy of the program, say - the names here answer the old
/// folder, so the launch settings and per-user saves in it are read where they are. The saves
/// beside the game folder (<c>&lt;game&gt;\..\saves</c>) are not in either and are never touched.
/// </summary>
public static class UserFolders
{
    /// <summary>The program's name, and its per-user folders'.</summary>
    public const string Name = "OpenDejenol";

    /// <summary>The per-user folders' name before the program had one.</summary>
    public const string OldName = "Mordor Port";

    /// <summary><c>%APPDATA%\OpenDejenol</c>, or the old <c>%APPDATA%\Mordor Port</c> while only that one is there.</summary>
    public static string Roaming => In(RoamingParent);

    /// <summary><c>%LOCALAPPDATA%\OpenDejenol</c>, or the old <c>%LOCALAPPDATA%\Mordor Port</c> while only that one is there.</summary>
    public static string Local => In(LocalParent);

    private static string RoamingParent => RoamingParentFor(OperatingSystem.IsMacOS(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Directory.Exists);

    /// <summary>
    /// The folder <see cref="Roaming"/> is made in. On Windows and Linux, ApplicationData as it always
    /// was: <c>%APPDATA%</c>, <c>~/.config</c>. On macOS .NET gives ApplicationData as <c>~/.config</c>
    /// too, a Linux habit; a Mac keeps an app's own files in <c>~/Library/Application Support</c>,
    /// and that is the one used there - unless a Mac that ran an earlier build (the first macOS
    /// builds) already has <c>~/.config/OpenDejenol</c> (or the old <c>Mordor Port</c>), with its
    /// launch settings and per-user saves in it: that one goes on being used where it is. Nothing is
    /// moved, so nothing can be lost on the way or split between two folders, and the chosen game
    /// folder and the saves are found as before. Only names; makes nothing. The inputs are given so
    /// the rule can be checked on any system.
    /// </summary>
    /// <param name="macOS">Whether this is macOS.</param>
    /// <param name="applicationData">Environment.SpecialFolder.ApplicationData.</param>
    /// <param name="home">Environment.SpecialFolder.UserProfile, the home folder.</param>
    /// <param name="exists">Whether a folder is there (Directory.Exists).</param>
    public static string RoamingParentFor(bool macOS, string applicationData, string home, Func<string, bool> exists)
    {
        if (!macOS || string.IsNullOrEmpty(home)) return applicationData;
        if (!string.IsNullOrEmpty(applicationData)
            && (exists(Path.Combine(applicationData, Name)) || exists(Path.Combine(applicationData, OldName))))
            return applicationData;
        return Path.Combine(home, "Library", "Application Support");
    }

    private static string LocalParent
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? Path.GetTempPath() : local;
        }
    }

    /// <summary>
    /// The program's folder in <paramref name="parent"/>: <c>OpenDejenol</c>, unless only the old
    /// <c>Mordor Port</c> is there, which is then the one in use. Only names; makes nothing.
    /// </summary>
    public static string In(string parent)
    {
        string now = Path.Combine(parent, Name), old = Path.Combine(parent, OldName);
        if (!Directory.Exists(now) && Directory.Exists(old)) return old;
        return now;
    }

    /// <summary>
    /// Renames <paramref name="parent"/>\Mordor Port to <paramref name="parent"/>\OpenDejenol when
    /// the old one is there and the new one is not, keeping everything in it; and returns the
    /// folder now in use (<see cref="In"/>). A rename that cannot be done leaves the old folder as
    /// it was, still in use. Never throws.
    /// </summary>
    public static string Migrate(string parent)
    {
        try
        {
            string now = Path.Combine(parent, Name), old = Path.Combine(parent, OldName);
            if (!Directory.Exists(now) && Directory.Exists(old)) Directory.Move(old, now);
        }
        catch { /* in use or not allowed: the old folder goes on being read where it is */ }
        return In(parent);
    }

    /// <summary>Both folders renamed from the old name if need be (<see cref="Migrate"/>). Never throws.</summary>
    public static void MigrateAll()
    {
        try { if (!string.IsNullOrEmpty(RoamingParent)) Migrate(RoamingParent); } catch { }
        try { Migrate(LocalParent); } catch { }
    }
}
