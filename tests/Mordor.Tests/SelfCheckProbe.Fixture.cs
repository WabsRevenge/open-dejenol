using System.Text.Json;
using System.Text.Json.Serialization;
using Mordor.Core;

public static partial class SelfCheckProbe
{
    private static readonly JsonSerializerOptions RecordJson = new()
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>A record as text: every public property and field, so two that read the same print the same.</summary>
    private static string RecordText(object? o) => JsonSerializer.Serialize(o, o?.GetType() ?? typeof(object), RecordJson);

    /// <summary>The two lists hold the same records, field for field; the first that differs is named.</summary>
    private static void SameRecords<T>(string what, IReadOnlyList<T> want, IReadOnlyList<T> got, Func<T, string> name)
    {
        Check($"{what}: count", got.Count, want.Count);
        int differ = 0, first = -1;
        for (int i = 0; i < Math.Min(want.Count, got.Count); i++)
            if (RecordText(want[i]) != RecordText(got[i])) { differ++; if (first < 0) first = i; }
        Check($"{what}: records that differ", differ, 0);
        if (first >= 0) Console.WriteLine($"        the first is record {first}, {name(want[first])}");
    }

    /// <summary>
    /// The invented game CI plays on (src/Mordor.Core/FixtureGame.cs) and the writer behind it
    /// (MdrWriter.cs): the fixture is written to a scratch folder and read back through the port's
    /// own loaders, record for record; then every data file of the game folder given here is read,
    /// written back with MdrWriter and read again, and must read the same. Only scratch folders are
    /// written; the game folder is only read, and nothing of it is kept.
    /// </summary>
    private static void FixtureChecks(string dir)
    {
        Console.WriteLine("\nthe fixture game (FixtureGame, MdrWriter): written, read back, and the game's own files rewritten");
        string scratch = Path.Combine(Path.GetTempPath(), "OpenDejenol-fixture-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            FixtureGame.Write(scratch);
            string D(string f) => GameFiles.DataFile(scratch, f);
            Check("the marker file is written", File.Exists(Path.Combine(scratch, FixtureGame.MarkerFile)), true);
            Check("and no MORDOR.EXE or MG11.DLL", File.Exists(Path.Combine(scratch, "MORDOR.EXE")) || File.Exists(Path.Combine(scratch, "MG11.DLL")), false);
            Check("FixtureGame.Write refuses a folder that holds a game", RefusesGame(), true);
            // GameFiles.DataVersion's test: after the length, a digit, a dot and a digit
            foreach (string f in new[] { "MDATA1.MDR", "MDATA2.MDR", "MDATA3.MDR", "MDATA5.MDR" })
            {
                var b = File.ReadAllBytes(D(f));
                bool form = b.Length >= 12 && char.IsAsciiDigit((char)b[2]) && b[3] == '.' && char.IsAsciiDigit((char)b[4]);
                Check($"{f} opens with a version string", form ? System.Text.Encoding.ASCII.GetString(b, 2, b[0]) : "(none)", FixtureGame.Version);
            }

            // ---- MDATA2, 3, 5 ----
            var spells = MdrLoader.LoadSpells(D("MDATA2.MDR"));
            SameRecords("MDATA2 spells read back", FixtureGame.Spells(), spells, s => s.Name);
            Check("spell 2: name, category, effect, damage", spells.Count > 2 ? $"{spells[2].Name} {spells[2].Category} {spells[2].Effect} {spells[2].Damage2}" : "", "Fixture Mend 9 0 9");

            var items = MdrLoader.LoadItems(D("MDATA3.MDR"));
            SameRecords("MDATA3 items read back", FixtureGame.Items(), items, i => i.Name);
            var sword = items.FirstOrDefault(i => i.Name == "Test Sword");
            Check("Test Sword: type, attack, floor, guilds, hands", sword == null ? "" : $"{sword.Type} {sword.Att} {sword.Floor} {sword.Guilds} {sword.NHands}", "3 8 1 3 1");
            var crest = Guildmasters.CrestFor(items, 0);
            Check("the Nomad's crest: id, special type, type", crest == null ? "" : $"{crest.Id} {crest.SpecialType} {crest.Type}", "299 500 34");
            Check("the tonic casts spell 3 (one-based), Fixture Mend", items.FirstOrDefault(i => i.Name == "Test Tonic") is Item t ? ItemUse.SpellOf(t, spells)?.Name : "", "Fixture Mend");

            var monsters = MdrLoader.LoadMonsters(D("MDATA5.MDR"));
            SameRecords("MDATA5 monsters read back", FixtureGame.Monsters(), monsters, m => m.Name);
            Check("monsters found on levels 1, 2, 3", string.Join(" ", Enumerable.Range(1, 3).Select(l => monsters.Count(m => m.LevelFound == l))), "4 4 3");
            Check("every monster's Ref is its place in the table", monsters.Select((m, i) => m.Ref == i).All(x => x), true);
            Check("Fixture Wolf: class, level, hits", monsters.FirstOrDefault(m => m.Name == "Fixture Wolf") is Monster w ? $"{w.Type} {w.LevelFound} {w.Hits}" : "", "2 3 18");
            // the encounter roll has no way out, as the original's has none: on level 1 below area 18
            // it draws again for any lead of guild level above 2 (EncounterGenerator.Generate's newbie
            // guard), so a fixture whose level-1 monsters were all above 2 hung the dungeon for ever
            var rolled = System.Threading.Tasks.Task.Run(() =>
            {
                var rng = new Random(7);
                for (int level = 1; level <= 3; level++)
                    for (int i = 0; i < 200; i++) EncounterGenerator.Generate(monsters, level, rng);
            });
            Check("200 encounters drawn on each level end (within 20 seconds)", rolled.Wait(TimeSpan.FromSeconds(20)), true);

            // ---- MDATA11 ----
            var dungeon = MdrLoader.LoadDungeon(D("MDATA11.MDR"));
            SameRecords("MDATA11 levels read back", FixtureGame.Dungeon(), dungeon, f => "level " + f.LevelNum);
            Check("levels, each 30 by 30", string.Join(" ", dungeon.Select(f => $"{f.LevelNum}:{f.Width}x{f.Height}")), "1:30x30 2:30x30 3:30x30");
            string Squares(Floor f, int bit) => string.Join("", Enumerable.Range(0, f.Height).SelectMany(y => Enumerable.Range(0, f.Width).Where(x => f.Has(x, y, bit)).Select(x => $"({x},{y})")));
            Check("stairs up on each level", string.Join(" ", dungeon.Select(f => Squares(f, Floor.STAIRS_UP))), "(2,2) (14,15) (2,17)");
            Check("stairs down on each level", string.Join(" ", dungeon.Select(f => Squares(f, Floor.STAIRS_DOWN) is { Length: > 0 } s ? s : "-")), "(14,15) (2,17) -");
            Check("doors on each level, plain and secret", string.Join(" ", dungeon.Select(f => $"{f.Fields.Count(c => (c.Flags & ((1L << Floor.DOOR_EAST) | (1L << Floor.DOOR_NORTH))) != 0)}/{f.Fields.Count(c => (c.Flags & ((1L << Floor.SECRET_DOOR_EAST) | (1L << Floor.SECRET_DOOR_NORTH))) != 0)}")), "4/0 2/0 1/1");
            Check("areas in the header and in use", string.Join(" ", dungeon.Select(f => $"{f.HeaderAreaCount}/{f.AreasInUse}")), "5/5 3/3 3/3");
            var two = dungeon.Count > 1 ? dungeon[1] : null;
            Check("level 2's teleporter and chute records", two == null ? "" : string.Join(" ", two.Teleporters.Select(t => $"T{t.X},{t.Y}>{t.X2},{t.Y2},{t.Z2}").Concat(two.Chutes.Select(c => $"C{c.X},{c.Y}+{c.Depth}"))), "T9,17>15,14,2 C4,16+1");
            Check("the chute lands on open floor a level down", dungeon.Count > 2 && !dungeon[2].Has(3, 15, Floor.ROCK), true);
            Check("every open square is walkable to from the stairs in (doors open)", string.Join(" ", dungeon.Select(f => Reachable(f) == f.Fields.Count(c => (c.Flags & (1L << Floor.ROCK)) == 0))), "True True True");

            // ---- MDATA1, through the loaders the game uses ----
            var tables = Mdata1.ReadTables(D("MDATA1.MDR"));
            var want = FixtureGame.Mdata1();
            Check("MDATA1 version", tables.Version, FixtureGame.Version);
            SameRecords("MDATA1 item types read back", want.ItemTypes, tables.ItemTypes, e => e.Name);
            SameRecords("MDATA1 item descriptions read back", want.ItemDescriptions, tables.ItemDescriptions, e => e.Name);
            SameRecords("MDATA1 monster classes read back", want.MonsterClasses, tables.MonsterClasses, e => e.Name);
            SameRecords("MDATA1 monster groups read back", want.MonsterGroups, tables.MonsterGroups, e => e.Name);
            Mdata1.LoadFrom(scratch); RaceData.LoadFrom(scratch); GuildData.LoadFrom(scratch);
            SameRecords("MDATA1 races read back (RaceData.LoadFrom)", want.Races, RaceData.All, r => r.Name);
            SameRecords("MDATA1 guilds read back (GuildData.LoadFrom)", want.Guilds, GuildData.All, g => g.Name);
            Check("the races", string.Join(", ", RaceData.All.Select(r => r.Name)), "Plainfolk, Tallkin, Stoutling");
            Check("the guilds, the Nomad first", string.Join(", ", GuildData.All.Select(g => g.Name)), "Nomad, Brawler, Chanter");
            Check("Mdata1.LoadFrom: 35 item types, 4 monster classes", $"{Mdata1.ItemTypes.Count} {Mdata1.MonsterClasses.Count} {Mdata1.Loaded}", "35 4 True");
            Check("Brawler: hits, race mask, gold factor, A/D every", GuildData.All.FirstOrDefault(g => g.Name == "Brawler") is GuildDef br ? $"{br.AvgHits} {br.RaceMask} {br.GoldFactor} {br.AdEvery}" : "", "6 3 1 3");
            Check("a crest is guild issued with three guilds loaded", crest != null && ItemUse.IsGuildIssued(crest), true);
            Check("Tallkin: size, maximum strength", RaceData.All.FirstOrDefault(r => r.Name == "Tallkin") is Race tk ? $"{tk.Size} {tk.Max[0]}" : "", "Big 22");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  FAIL  the fixture could not be written or read back: {ex.Message}");
            _failed++;
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch { }
            // the game folder's tables back, for anything after this section
            Mdata1.LoadFrom(dir); RaceData.LoadFrom(dir); GuildData.LoadFrom(dir);
        }

        RealFilesRewritten(dir);
    }

    private static bool RefusesGame()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "OpenDejenol-fixture-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(scratch);
            File.WriteAllText(Path.Combine(scratch, "MDATA1.MDR"), "not ours");
            try { FixtureGame.Write(scratch); return false; }
            catch (InvalidOperationException) { return File.ReadAllText(Path.Combine(scratch, "MDATA1.MDR")) == "not ours"; }
        }
        finally { try { Directory.Delete(scratch, true); } catch { } }
    }

    /// <summary>How many squares can be walked to from the level's first stairs up, through any door.</summary>
    private static int Reachable(Floor f)
    {
        var start = Enumerable.Range(0, f.Height).SelectMany(y => Enumerable.Range(0, f.Width).Select(x => (x, y)))
                              .Where(p => f.Has(p.x, p.y, Floor.STAIRS_UP)).DefaultIfEmpty((-1, -1)).First();
        if (start.Item1 < 0) return 0;
        var nav = new DungeonNav(f, start.Item1, start.Item2);
        var seen = new HashSet<(int, int)> { start };
        var todo = new Queue<(int x, int y)>(seen);
        while (todo.Count > 0)
        {
            var (x, y) = todo.Dequeue();
            foreach (Facing face in Enum.GetValues<Facing>())
            {
                var (dx, dy) = DungeonNav.Delta(face);
                if (nav.EdgeAt(x, y, face) == Edge.Wall || nav.IsRock(x + dx, y + dy)) continue;
                if (seen.Add((x + dx, y + dy))) todo.Enqueue((x + dx, y + dy));
            }
        }
        return seen.Count;
    }

    /// <summary>
    /// Each data file of the game folder, read, written back with MdrWriter and read again: the same
    /// values both times. Nothing of the game's is kept - the copies go to a scratch folder deleted
    /// after. Skipped for the fixture itself, and file by file where the folder lacks one.
    /// </summary>
    private static void RealFilesRewritten(string dir)
    {
        if (File.Exists(Path.Combine(dir, FixtureGame.MarkerFile)))
        {
            Console.WriteLine("  skip  the game's files rewritten: this folder is the fixture, not a Mordor");
            return;
        }
        string scratch = Path.Combine(Path.GetTempPath(), "OpenDejenol-rewrite-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(scratch);
        try
        {
            string? Source(string f)
            {
                string p = GameFiles.DataFile(dir, f);
                if (File.Exists(p)) return p;
                Console.WriteLine($"  skip  {f} rewritten: the game folder has none");
                return null;
            }
            string Copy(string f) => Path.Combine(scratch, f);

            if (Source("MDATA2.MDR") is string spellsAt)
            {
                var a = MdrLoader.LoadSpells(spellsAt);
                MdrWriter.WriteSpells(Copy("MDATA2.MDR"), a, FixtureGame.Version);
                SameRecords("the game's MDATA2 rewritten", a, MdrLoader.LoadSpells(Copy("MDATA2.MDR")), s => s.Name);
            }
            if (Source("MDATA3.MDR") is string itemsAt)
            {
                var a = MdrLoader.LoadItems(itemsAt);
                MdrWriter.WriteItems(Copy("MDATA3.MDR"), a, FixtureGame.Version);
                SameRecords("the game's MDATA3 rewritten", a, MdrLoader.LoadItems(Copy("MDATA3.MDR")), i => i.Name);
            }
            if (Source("MDATA5.MDR") is string monstersAt)
            {
                var a = MdrLoader.LoadMonsters(monstersAt);
                MdrWriter.WriteMonsters(Copy("MDATA5.MDR"), a, FixtureGame.Version);
                var b = MdrLoader.LoadMonsters(Copy("MDATA5.MDR"));
                SameRecords("the game's MDATA5 rewritten", a, b, m => m.Name);
                Check("the game's MDATA5 rewritten: every Ref the same", a.Count == b.Count && a.Zip(b).All(p => p.First.Ref == p.Second.Ref), true);
            }
            if (Source("MDATA11.MDR") is string dungeonAt)
            {
                var a = MdrLoader.LoadDungeon(dungeonAt);
                MdrWriter.WriteDungeon(Copy("MDATA11.MDR"), a);
                SameRecords("the game's MDATA11 rewritten", a, MdrLoader.LoadDungeon(Copy("MDATA11.MDR")), f => "level " + f.LevelNum);
            }
            if (Source("MDATA1.MDR") is string rulesAt)
            {
                var t = Mdata1.ReadTables(rulesAt);
                var races = MdrLoader.LoadRaces(rulesAt);
                var guilds = MdrLoader.LoadGuilds(rulesAt);
                MdrWriter.WriteMdata1(Copy("MDATA1.MDR"), new MdrWriter.Mdata1Contents(t.Version, races, guilds,
                                      t.ItemTypes, t.ItemDescriptions, t.MonsterClasses, t.MonsterGroups));
                var u = Mdata1.ReadTables(Copy("MDATA1.MDR"));
                Check("the game's MDATA1 rewritten: version", u.Version, t.Version);
                SameRecords("the game's MDATA1 rewritten: races", races, MdrLoader.LoadRaces(Copy("MDATA1.MDR")), r => r.Name);
                SameRecords("the game's MDATA1 rewritten: guilds", guilds, MdrLoader.LoadGuilds(Copy("MDATA1.MDR")), g => g.Name);
                SameRecords("the game's MDATA1 rewritten: item types", t.ItemTypes, u.ItemTypes, e => e.Name);
                SameRecords("the game's MDATA1 rewritten: item descriptions", t.ItemDescriptions, u.ItemDescriptions, e => e.Name);
                SameRecords("the game's MDATA1 rewritten: monster classes", t.MonsterClasses, u.MonsterClasses, e => e.Name);
                SameRecords("the game's MDATA1 rewritten: monster groups", t.MonsterGroups, u.MonsterGroups, e => e.Name);
            }
        }
        finally { try { Directory.Delete(scratch, true); } catch { } }
    }
}
