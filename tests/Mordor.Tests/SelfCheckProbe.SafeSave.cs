using Mordor.Core;

public static partial class SelfCheckProbe
{
    /// <summary>
    /// Saves written so a crash part way through keeps the old file (SafeFile), and a character file
    /// that will not read put back from its last good copy - or at least said - instead of vanishing
    /// from the lists while its name stays taken (SaveRecovery, Roster.Saved). Only scratch folders.
    /// </summary>
    private static void SafeSaveChecks(string dir)
    {
        Console.WriteLine("\nsafe saving and damaged character files (SafeFile, SaveRecovery)");
        string scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mordor-safesave-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var log = new List<string>();
        var oldSafeLog = SafeFile.Log;
        var oldRecoveryLog = SaveRecovery.Log;
        SafeFile.Log = log.Add;
        SaveRecovery.Log = log.Add;
        SaveRecovery.TakeNotices();
        try
        {
            // ---- SafeFile ----
            string f = Path.Combine(scratch, "plain", "a.json");
            SafeFile.WriteAllText(f, "first");
            SafeFile.WriteAllText(f, "second");
            Check("SafeFile: the file holds the last text, its folder made", File.ReadAllText(f), "second");
            Check("SafeFile: no .tmp left behind", File.Exists(f + SafeFile.TempSuffix), false);
            Check("SafeFile: UTF-8 with no byte-order mark, as File.WriteAllText", File.ReadAllBytes(f)[0], (byte)'s');
            // something else holding the file for a moment: the swap waits and goes through
            using (var held = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var release = Task.Run(async () => { await Task.Delay(60); held.Dispose(); });
                SafeFile.WriteAllText(f, "third");
                release.Wait();
            }
            Check("SafeFile: a file held for a moment is still written (retries)", File.ReadAllText(f), "third");
            // held for good: the save gives up, says so, and the old file and the .tmp are as they were
            using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool threw = false;
                try { SafeFile.WriteAllText(f, "fourth"); } catch { threw = true; }
                Check("SafeFile: a file held for good - the save throws", threw, true);
            }
            Check("SafeFile: ...the old file is untouched", File.ReadAllText(f), "third");
            Check("SafeFile: ...and crash.log is told", log.Any(l => l.Contains(f) && l.Contains("could not be saved")), true);
            Check("backups leave an unfinished save's .tmp behind", Backups.IsGameFile(f + SafeFile.TempSuffix), false);
            File.Delete(f + SafeFile.TempSuffix);

            // ---- a character that reads keeps a last good copy ----
            string saves = Directory.CreateDirectory(Path.Combine(scratch, "saves")).FullName;
            Character Make(string name) => new() { Name = name, Guilds = { new GuildMembership { Guild = "Nomad", Level = 1 } } };
            Make("Hob").Save(Path.Combine(saves, "Hob.json"));
            Make("Ann").Save(Path.Combine(saves, "Ann.json"));
            File.WriteAllText(Path.Combine(saves, "store.json"), "{ \"not\": \"a character\" ");   // a damaged world file
            Roster.Added(saves, "Hob", Array.Empty<string>());
            Check("both characters listed", string.Join(",", Roster.Saved(saves).Select(p => p.Ch.Name).OrderBy(n => n)), "Ann,Hob");
            string goodHob = SaveRecovery.GoodPathFor(Path.Combine(saves, "Hob.json"));
            Check("a character that reads gets a last good copy, in last-good inside the saves folder",
                  File.Exists(goodHob) && Path.GetDirectoryName(goodHob)!.EndsWith(SaveRecovery.GoodDir), true);
            Check("a damaged world file is not taken for a character: nothing said", SaveRecovery.TakeNotices().Count, 0);

            // ---- a save cut short by a crash: put back from the last good copy ----
            File.WriteAllText(Path.Combine(saves, "Hob.json"), "{ \"Name\": \"Hob\", \"Gol");
            log.Clear();
            var after = Roster.Saved(saves).Select(p => p.Ch.Name).OrderBy(n => n).ToList();
            Check("a half-written Hob is still listed - put back from his last good copy", string.Join(",", after), "Ann,Hob");
            Check("...his file reads again", Character.Load(Path.Combine(saves, "Hob.json")).Name, "Hob");
            Check("...the damaged file is kept in damaged/ for a bug report",
                  Directory.GetFiles(Path.Combine(saves, SaveRecovery.DamagedDir), "Hob-*.json").Length, 1);
            Check("...crash.log says why and what was done",
                  log.Any(l => l.Contains("could not be read")) && log.Any(l => l.Contains("put back")), true);
            var notices = SaveRecovery.TakeNotices();
            Check("...and the player is told once", notices.Count == 1 && notices[0].StartsWith("Hob's save file could not be read"), true);

            // ---- damaged with no copy: said, once, and left as it is ----
            File.WriteAllText(Path.Combine(saves, "Ann.json"), "");
            File.Delete(SaveRecovery.GoodPathFor(Path.Combine(saves, "Ann.json")));
            log.Clear();
            Roster.Saved(saves);
            Roster.Saved(saves);
            Check("an empty Ann.json with no copy: not listed", Roster.Saved(saves).Any(p => p.Ch.Name == "Ann"), false);
            Check("...left as it is (the name stays taken, as before)", File.Exists(Path.Combine(saves, "Ann.json")), true);
            Check("...crash.log told once, not at every list", log.Count(l => l.Contains("Ann.json") && l.Contains("could not be read")), 1);
            notices = SaveRecovery.TakeNotices();
            Check("...and the player is told once, with where to look",
                  notices.Count == 1 && notices[0].Contains("\"Ann.json\"") && notices[0].Contains("crash.log"), true);

            // ---- every save writer goes through SafeFile ----
            string core = Path.Combine(PortTree.Root, "src", "Mordor.Core");
            var unsafeWrites = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains("Extraction") && !p.EndsWith("InstallerUnpack.cs") && !p.EndsWith("SafeFile.cs"))
                .Where(p => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(p), @"(?<!Safe)File\.WriteAll(Text|Bytes)\("))
                .Select(Path.GetFileName).ToList();
            Check("no save in Mordor.Core is written in place (File.WriteAllText) any more", string.Join(", ", unsafeWrites), "");
        }
        finally
        {
            SafeFile.Log = oldSafeLog;
            SaveRecovery.Log = oldRecoveryLog;
            try { Directory.Delete(scratch, true); } catch { }
        }
    }
}
