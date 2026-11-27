using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mordor.App;
using Mordor.Core;
using Mordor.Modding;

/// <summary>
/// Issue #13: a stat raised by a Tome is still there after the character is unloaded and loaded
/// again. The original writes the raise into <c>Chars().Stats</c> (sub15CD, MAINMOD2.BAS:6203-6225),
/// fn04ED Puts the whole record (MAINMOD.BAS:384-402) - when the game loop ends (MAINMOD2.BAS:6318-6321)
/// and when mdrcharUnload lets the character go (MAINMOD.BAS:419-423) - and fn04CD Gets it back as
/// it lies, rebuilding nothing from it (MAINMOD.BAS:323-347). So the raise is kept.
///
/// Played through the real windows, in a scratch saves folder: the title runs a character, the
/// town takes them down, the Use button reads a Tome of Learning in the dungeon, the dungeon and the
/// town are left, the title unloads the character and loads them again. Once alone, and once with the
/// Tome read by the second member of a remembered party, who comes back with the leader.
/// </summary>
static class StatItemSaveCheck
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static int Run(string game)
    {
        Console.WriteLine("\na stat raised by a Tome survives unloading and loading the character (issue #13):");
        int failed = 0;
        void Check(string what, object? got, object? want)
        {
            bool ok = Equals(got, want);
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what}{(ok ? "" : $": got {got}, want {want}")}");
            if (!ok) failed++;
        }
        void Pump(int n = 6)
        {
            for (int i = 0; i < n; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        }
        T Field<T>(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
                if (t.GetField(name, Any) is FieldInfo f) return (T)f.GetValue(o)!;
            throw new MissingFieldException(o.GetType().Name, name);
        }
        object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, Any)!.Invoke(o, args);

        Mdata1.LoadFrom(game); RaceData.LoadFrom(game); GuildData.LoadFrom(game);
        var tome = MdrLoader.LoadItems(GameFiles.DataFile(game, "MDATA3.MDR")).FirstOrDefault(i => i.Name.Trim() == "Tome of Learning");
        if (tome == null) { Console.WriteLine("  skip  no Tome of Learning in this game's items"); return 0; }

        foreach (bool inParty in new[] { false, true })
        {
            string scenario = inParty ? "read by the second member of a party" : "read alone";
            string scratch = Path.Combine(Path.GetTempPath(), "mordor-uicheck-tome-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            var envWas = new[] { Saves.EnvDir, GameFolder.EnvIni, "MORDOR_MUTE" }.ToDictionary(k => k, Environment.GetEnvironmentVariable);
            var hostWas = ModHost.Active;
            string gameDirWas = TitleWindow.GameDir;
            var opened = new List<Window>();
            var watch = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
            TitleWindow? title = null;
            try
            {
                Environment.SetEnvironmentVariable(Saves.EnvDir, scratch);
                Environment.SetEnvironmentVariable(GameFolder.EnvIni, Path.Combine(scratch, "launch.ini"));
                Environment.SetEnvironmentVariable("MORDOR_MUTE", "1");
                ModHost.Active = new ModHost(new ModSettingsStore(Path.Combine(scratch, "settings.ini")), _ => { });
                TitleWindow.GameDir = game;

                var leader = Character.Create("Tomeleader", RaceData.All[0], Alignment.Neutral, Sex.Female, new[] { 12, 12, 12, 12, 12, 12 });
                var reader = leader;
                if (inParty)
                {
                    reader = Character.Create("Tomereader", RaceData.All[0], Alignment.Neutral, Sex.Male, new[] { 12, 12, 12, 12, 12, 12 });
                    reader.Save(Path.Combine(scratch, "Tomereader.json"));
                    var remembered = new Party(leader);
                    remembered.Members.Add(reader);
                    remembered.SaveParty(scratch, remember: true);
                }
                reader.Pack[1] = new InventorySlot { ItemId = tome.Id, Charges = (short)tome.Charges, Known = IdLevel.All };
                reader.Save(Path.Combine(scratch, Character.SafeFileName(reader.Name) + ".json"));
                if (inParty) leader.Save(Path.Combine(scratch, "Tomeleader.json"));
                string readerFile = Path.Combine(scratch, Character.SafeFileName(reader.Name) + ".json");
                int before = reader.Stats[(int)Stat.Int];

                title = new TitleWindow();
                title.Show(); Pump(20);
                foreach (var w in opened.ToList()) if (w != title && w.IsVisible) w.Close();   // the Welcome box
                Pump(10);
                var loaded = Character.Load(Path.Combine(scratch, "Tomeleader.json"));
                typeof(TitleWindow).GetField("_loaded", Any)!.SetValue(title, loaded);
                Call(title, "Play", loaded); Pump(20);
                var town = opened.OfType<CityWindow>().Last();
                Call(town, "EnterDungeon", null, new RoutedEventArgs()); Pump(20);
                var below = Field<DungeonWindow?>(town, "_below");
                if (below == null) { Check($"{scenario}: the party went down", false, true); continue; }
                var party = Field<Party>(below, "_party");
                party.Select(inParty ? 1 : 0); Pump();
                Call(below, "RefreshPack");
                Field<ListBox>(below, "PackList").SelectedIndex = 1; Pump();
                Call(below, "UseSelectedItem"); Pump();
                Check($"{scenario}: the Tome raised Intelligence by one", party.Selected.Stats[(int)Stat.Int], before + 1);
                below.Close(); Pump(20);
                town.Close(); Pump(20);
                Call(title, "OnLoad", null, new RoutedEventArgs()); Pump(20);   // Unload Character
                Check($"{scenario}: unloaded, the saved record has it", Character.Load(readerFile).Stats[(int)Stat.Int], before + 1);

                // loaded again and run: the town opens on the record, the party with it
                var again = Character.Load(Path.Combine(scratch, "Tomeleader.json"));
                typeof(TitleWindow).GetField("_loaded", Any)!.SetValue(title, again);
                Call(title, "Play", again); Pump(20);
                var town2 = opened.OfType<CityWindow>().Last();
                var back = Field<Party>(town2, "_party").Members.FirstOrDefault(m => m.Name == reader.Name);
                Check($"{scenario}: loaded and run again, it is still there", back?.Stats[(int)Stat.Int], before + 1);
                town2.Close(); Pump(20);
            }
            catch (Exception ex) { Check($"{scenario}: the scenario could not be driven: {ex.GetType().Name}: {ex.Message}", false, true); }
            finally
            {
                watch.Dispose();
                foreach (var w in opened) try { if (w.IsVisible) w.Close(); } catch { }
                try { title?.Close(); } catch { }
                Pump(10);
                ModHost.Active = hostWas;
                TitleWindow.GameDir = gameDirWas;
                foreach (var (k, v) in envWas) Environment.SetEnvironmentVariable(k, v);
                if (new DirectoryInfo(scratch).Parent?.FullName != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                    throw new InvalidOperationException("Refusing cleanup outside the tome test folder.");
                try { Directory.Delete(scratch, true); } catch { }
            }
        }
        return failed;
    }
}
