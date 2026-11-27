namespace Mordor.Core;

/// <summary>
/// Writes the game's .MDR data files, the mirror of <see cref="MdrLoader"/> and <see cref="Mdata1"/>:
/// every record length, record number and field is the one they read, in their order, so what is
/// written here reads back as the same values. It is for the invented game the headless checks run
/// on (<see cref="FixtureGame"/>), and for the selfcheck that holds the two sides to each other.
///
/// Only what the port reads is written. A field the loaders step over (the words and Singles
/// <see cref="MdrLoader.LoadGuilds"/> passes by, the seventh stat, the twelfth resistance, MDATA5's
/// second record) is written as nought, and the tail of every record is zeros. So a rewritten copy
/// of a real file reads back the same through the port, but is not that file byte for byte.
///
/// The original reads the same files in <c>loadDataFile</c>: MDATA1 at MODULE1.BAS:678-717,
/// MDATA3 at 718-737, MDATA2 at 738-756 and MDATA5 at 868-886, each opened For Random with the
/// record length used here and checked for its version in record 1; and MDATA11 in sub04A3
/// (MAINMOD.BAS:823-828), Len 20, where record level + 1 holds the record of that level's header.
/// </summary>
public static class MdrWriter
{
    /// <summary>MDATA2: Len 75 (gc1C68). Version, count, then one spell to a record from record 3.</summary>
    public const int SpellRecordLength = 75;

    /// <summary>MDATA3: Len 125 (gc1C6A). Version, store code, count, then one item to a record from record 4.</summary>
    public const int ItemRecordLength = 125;

    /// <summary>MDATA5: Len 160 (gc1C5C). Version, a record the port does not read, count, then one monster to a record from record 4.</summary>
    public const int MonsterRecordLength = 160;

    /// <summary>MDATA11: Len 20 (mc01B8).</summary>
    public const int DungeonRecordLength = 20;

    /// <summary>The area records every level carries, whatever its header counts (<see cref="MdrLoader.LoadDungeon"/>).</summary>
    public const int AreaRecords = 201;

    public static void WriteSpells(string path, IReadOnlyList<Spell> spells, string version)
    {
        var w = new RecordWriter(SpellRecordLength);
        w.PutString(version); w.Write();
        w.PutUWord(spells.Count); w.Write();
        foreach (var s in spells)
        {
            w.PutString(s.Name);
            w.PutWord(s.Id); w.PutWord(s.Category); w.PutWord(s.Level); w.PutWord(s.U4);
            w.PutWord(s.KillEffect); w.PutWord(s.AffectMonster); w.PutWord(s.AffectGroup);
            w.PutWord(s.Damage1); w.PutWord(s.Damage2); w.PutWord(s.SpecialEffect);
            w.PutWord(s.Effect);
            w.PutWords(s.Required, 7);
            w.PutWord(s.ResistIndex);
            w.Write();
        }
        w.Save(path);
    }

    /// <summary>
    /// MDATA3. <paramref name="storeCode"/> is record 2, <c>gv19E0</c>: the original keeps it beside
    /// the General Store's shelves and, when the two differ, matches the shelves to the items by Id
    /// (MODULE1.BAS:766-783). The port does not read it.
    /// </summary>
    public static void WriteItems(string path, IReadOnlyList<Item> items, string version, int storeCode = 0)
    {
        var w = new RecordWriter(ItemRecordLength);
        w.PutString(version); w.Write();
        w.PutUWord(storeCode); w.Write();
        w.PutUWord(items.Count); w.Write();
        foreach (var it in items)
        {
            w.PutString(it.Name);
            w.PutWord(it.Id); w.PutWord(it.Att); w.PutWord(it.Def); w.PutDword(it.Price);
            w.PutWord(it.Floor); w.PutWord(it.Rarity); w.PutDword(it.Abilities);
            w.PutWord(it.Swings); w.PutWord(it.SpecialType); w.PutWord(it.SpellIndex); w.PutWord(it.SpellId);
            w.PutDword(it.Charges); w.PutDword(it.Guilds); w.PutWord(it.LevelScale); w.PutFloat(it.DamageMod);
            w.PutDword(it.AlignmentFlags); w.PutWord(it.NHands); w.PutWord(it.Type); w.PutDword(it.ResistanceFlags);
            w.PutWords(it.StatsRequired, 7);
            w.PutWords(it.StatsMod, 7);
            w.PutWord(it.Cursed); w.PutWord(it.SpellLvl); w.PutWord(it.ClassRestricted);
            w.Write();
        }
        w.Save(path);
    }

    public static void WriteMonsters(string path, IReadOnlyList<Monster> monsters, string version)
    {
        var w = new RecordWriter(MonsterRecordLength);
        w.PutString(version); w.Write();
        w.Write();                              // record 2, gv19E2: not read by the port, written blank
        w.PutUWord(monsters.Count); w.Write();
        foreach (var m in monsters)
        {
            w.PutString(m.Name);
            w.PutWord(m.Att); w.PutWord(m.Def); w.PutWord(m.Id); w.PutWord(m.Hits);
            w.PutWord(m.NumGroups); w.PutWord(m.PicId); w.PutWord(m.LockedChance); w.PutWord(m.LevelFound);
            w.PutWords(m.Resistances, 12);
            w.PutDword(m.SpecialPropertyFlags); w.PutDword(m.SpecialAttackFlags); w.PutDword(m.SpellFlags);
            w.PutWord(m.Chance);
            w.PutWords(m.BoxChance, 4);
            w.PutWord(m.AlignmentValue); w.PutWord(m.InGroup);
            w.PutDword(m.GoldFactor); w.PutDword(m.TrapFlags);
            w.PutWord(m.GuildLevel);
            w.PutWords(m.Stats, 7);
            w.PutWord(m.Type); w.PutFloat(m.DamageMod);
            w.PutWord(m.CompanionType); w.PutWord(m.CompanionSpawnMode); w.PutWord(m.CompanionId);
            w.PutWords(m.Items, 11);
            w.PutWord(m.Subtype); w.PutWord(m.CompanionSubtype); w.PutWord(m.Size);
            w.PutWord(m.ItemDropLevel); w.PutWord(m.ItemChance);
            w.Write();
        }
        w.Save(path);
    }

    /// <summary>
    /// MDATA11: record 1 the number of levels, records 2 to levels + 1 the record each level starts
    /// at, then the levels one after another. A level is its header (width, height, level number,
    /// the area count, the chute count, the teleporter count), one record per square in the order
    /// <see cref="Floor.At"/> indexes them, the area count again and the 201 area records, the
    /// teleporter count and the teleporters, the chute count and the chutes.
    ///
    /// The counts written are the lists' own. The shipped file leaves room for more teleporters and
    /// chutes than a level uses, as empty records the loader drops (an X or Y of nought); those are
    /// not written back, so a level here can be shorter than the one it was read from, and it
    /// starts wherever the one before it ends.
    /// </summary>
    public static void WriteDungeon(string path, IReadOnlyList<Floor> floors)
    {
        var w = new RecordWriter(DungeonRecordLength);
        w.PutUWord(floors.Count); w.Write();
        int at = 2 + floors.Count;
        var starts = new int[floors.Count];
        for (int i = 0; i < floors.Count; i++)
        {
            starts[i] = at;
            if (at > ushort.MaxValue) throw new InvalidDataException($"level {i + 1} would start at record {at}, past what a 16-bit offset holds");
            w.PutUWord(at); w.Write();
            var f = floors[i];
            at += 1 + f.Width * f.Height + 1 + AreaRecords + 1 + f.Teleporters.Count + 1 + f.Chutes.Count;
        }
        for (int i = 0; i < floors.Count; i++)
        {
            w.Seek(starts[i]);
            WriteFloor(w, floors[i]);
        }
        w.Save(path);
    }

    private static void WriteFloor(RecordWriter w, Floor f)
    {
        int cells = f.Width * f.Height;
        if (f.Fields.Length != cells) throw new InvalidDataException($"level {f.LevelNum} is {f.Width}x{f.Height} but has {f.Fields.Length} squares");
        if (f.Areas.Count > AreaRecords) throw new InvalidDataException($"level {f.LevelNum} has {f.Areas.Count} areas; a level holds {AreaRecords}");

        w.PutWord(f.Width); w.PutWord(f.Height); w.PutWord(f.LevelNum);
        w.PutUWord(f.HeaderAreaCount); w.PutUWord(f.Chutes.Count); w.PutUWord(f.Teleporters.Count);
        w.Write();

        foreach (var c in f.Fields) { w.PutWord(c.AreaId); w.PutIntCurrency(c.Flags); w.Write(); }

        w.PutUWord(f.HeaderAreaCount); w.Write();
        for (int i = 0; i < AreaRecords; i++)
        {
            var a = i < f.Areas.Count ? f.Areas[i] : default;
            w.PutDword(a.SpawnMask); w.PutWord(a.LairId); w.Write();
        }

        w.PutUWord(f.Teleporters.Count); w.Write();
        foreach (var t in f.Teleporters) { w.PutWord(t.X); w.PutWord(t.Y); w.PutWord(t.X2); w.PutWord(t.Y2); w.PutWord(t.Z2); w.Write(); }

        w.PutUWord(f.Chutes.Count); w.Write();
        foreach (var c in f.Chutes) { w.PutWord(c.X); w.PutWord(c.Y); w.PutWord(c.Depth); w.Write(); }
    }

    // ---- MDATA1 ----

    /// <summary>
    /// Everything MDATA1 holds, in the order <see cref="Mdata1.Read"/> finds it: the version, then
    /// the races, the guilds, the item types, the item descriptions, the monster classes and the
    /// monster groups.
    /// </summary>
    public sealed record Mdata1Contents(
        string Version,
        IReadOnlyList<Race> Races,
        IReadOnlyList<GuildDef> Guilds,
        IReadOnlyList<NamedIndex> ItemTypes,
        IReadOnlyList<NamedIndex> ItemDescriptions,
        IReadOnlyList<NamedIndex> MonsterClasses,
        IReadOnlyList<NamedIndex> MonsterGroups);

    /// <summary>
    /// MDATA1, Len 260: record 1 the version, records 2 to 7 the six counts, and from record 8 one
    /// entry to a record, table after table (<see cref="Mdata1"/>).
    /// </summary>
    public static void WriteMdata1(string path, Mdata1Contents d)
    {
        var w = new RecordWriter(Mdata1.RecordLength);
        w.PutString(d.Version); w.Write();
        foreach (int n in new[] { d.Races.Count, d.Guilds.Count, d.ItemTypes.Count, d.ItemDescriptions.Count, d.MonsterClasses.Count, d.MonsterGroups.Count })
        {
            w.PutUWord(n); w.Write();
        }
        foreach (var r in d.Races) { WriteRace(w, r); w.Write(); }
        foreach (var g in d.Guilds) { WriteGuild(w, g); w.Write(); }
        foreach (var table in new[] { d.ItemTypes, d.ItemDescriptions, d.MonsterClasses, d.MonsterGroups })
            foreach (var e in table) { w.PutString(e.Name); w.PutWord(checked((short)e.Index)); w.Write(); }
        w.Save(path);
    }

    /// <summary>A race record as <see cref="MdrLoader.LoadRaces"/> reads it; the seventh stat and the twelfth resistance are nought.</summary>
    private static void WriteRace(RecordWriter w, Race r)
    {
        w.PutString(r.Name);
        w.PutWords(r.Min, 7); w.PutWords(r.Max, 7); w.PutWords(r.Resistances, 12);
        w.PutDword(r.AlignMask);
        w.PutWord(checked((short)r.Size)); w.PutWord(checked((short)r.BonusPoints)); w.PutWord(checked((short)r.MaxAge));
        w.PutFloat(r.ExpFactor);
    }

    /// <summary>
    /// A guild record as <see cref="MdrLoader.LoadGuilds"/> reads it. The fields it passes over - a
    /// word after the experience factor, a word and a Single before the quest percentage - are
    /// nought, as is the seventh required stat. The spell level caps are Singles in the record.
    /// </summary>
    private static void WriteGuild(RecordWriter w, GuildDef g)
    {
        if (g.Abilities.Length > 7 || g.SpellRate.Length > 19 || g.SpellMax.Length > 19)
            throw new InvalidDataException($"guild {g.Name}: more abilities or spell categories than the record holds");
        w.PutString(g.Name);
        w.PutWord(checked((short)g.AvgHits)); w.PutWord(checked((short)g.HitsCapLevel)); w.PutWord(checked((short)g.LateHits));
        w.PutFloat(g.ExpFactor); w.PutWord(0);
        w.PutWords(g.ReqStats, 7); w.PutDword(g.AlignMask);
        for (int k = 0; k < 7; k++) w.PutFloat(k < g.Abilities.Length ? g.Abilities[k] : 0);
        w.PutWord(0); w.PutFloat(0); w.PutWord(checked((short)g.QuestPercent));
        for (int k = 0; k < 19; k++) w.PutFloat(k < g.SpellRate.Length ? g.SpellRate[k] : 0);
        for (int k = 0; k < 19; k++) w.PutFloat(k < g.SpellMax.Length ? g.SpellMax[k] : 0);
        w.PutDword(g.RaceMask);
        w.PutWord(checked((short)g.GoldFactor)); w.PutFloat(g.ItemUseFactor);
        w.PutFloat(g.AttPerLevel); w.PutFloat(g.DefPerLevel);
        w.PutWord(checked((short)g.AdMaxLevel)); w.PutFloat(g.AdEvery);
    }
}
