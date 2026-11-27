namespace Mordor.Core;

/// <summary>
/// A tiny invented game, written by our own code, for the checks that need a game folder where
/// there is no copy of Mordor (CI). Every name and number in it is made up here: three races, three
/// guilds, six spells, fifteen items, eleven monsters and a three-level dungeon. None of it is the
/// original's data and none is meant to play like it; it only has to be read by the port the way a
/// real folder is, through <see cref="MdrLoader"/> and <see cref="Mdata1"/>.
///
/// What is kept from the game is only what the port's own code takes as a rule rather than reads
/// from the data: the first guild is the Nomad (<see cref="GuildData.StartingGuild"/>, and
/// Rules.cs tests GuildDefs(0) by that name); the levels are 30 by 30, the fixed grid the dungeon
/// view's arithmetic assumes (<see cref="MazeSelect.MapWidth"/>, MODULE3.BAS:73); the item type
/// numbers mean what the code takes them to mean (0 hands to 7 weapons, 23 to 28 used up, 34 a guild
/// crest: <see cref="ItemUse"/>, <see cref="Loot.TypeNames"/>); a crest is item 299 plus its guild
/// and is special type 500 plus it (<see cref="Guildmasters.CrestIdBase"/>,
/// <see cref="ItemUse.GuildIssuedBase"/>); record 0 of the monsters is never drawn
/// (<see cref="Combat.ClassRanges"/>), and the monsters run class by class, shallow to deep, as
/// the encounter pick walks them.
///
/// The folder gets <see cref="MarkerFile"/> and no MORDOR.EXE or MG11.DLL.
/// </summary>
public static class FixtureGame
{
    /// <summary>The marker at the folder's root that says it is this fixture, not a Mordor.</summary>
    public const string MarkerFile = "OPENDEJENOL.FIXTURE";

    /// <summary>
    /// The version string in record 1 of MDATA1, 2, 3 and 5: a digit, a dot, a digit, the form the
    /// game's own files have (GameFiles.DataVersion), with a number of our own.
    /// </summary>
    public const string Version = "0.1";

    /// <summary>The dungeon's size: three levels of the game's fixed 30 by 30.</summary>
    public const int Levels = 3, Width = MazeSelect.MapWidth, Height = MazeSelect.MapWidth;

    public const string MarkerText =
        "OpenDejenol test fixture.\r\n" +
        "An invented game's data files, written by Mordor.Core.FixtureGame for the headless checks.\r\n" +
        "Nothing in this folder comes from Mordor: every name and number in it is made up.\r\n";

    /// <summary>
    /// Write the whole fixture into <paramref name="dir"/>, creating it. Refuses a folder that holds
    /// a Mordor (a MORDOR.EXE, or an MDATA1 with no fixture marker beside it), so a real game is
    /// never written over.
    /// </summary>
    public static void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        if (File.Exists(Path.Combine(dir, "MORDOR.EXE")))
            throw new InvalidOperationException($"{dir} holds a MORDOR.EXE: the fixture is not written over a game.");
        if (File.Exists(Path.Combine(dir, "MDATA1.MDR")) && !File.Exists(Path.Combine(dir, MarkerFile)))
            throw new InvalidOperationException($"{dir} holds an MDATA1.MDR that is not the fixture's: the fixture is not written over a game.");

        // the marker first, so a folder left half written is still known for the fixture's
        SafeFile.WriteAllText(Path.Combine(dir, MarkerFile), MarkerText);
        MdrWriter.WriteMdata1(Path.Combine(dir, "MDATA1.MDR"), Mdata1());
        MdrWriter.WriteSpells(Path.Combine(dir, "MDATA2.MDR"), Spells(), Version);
        MdrWriter.WriteItems(Path.Combine(dir, "MDATA3.MDR"), Items(), Version);
        MdrWriter.WriteMonsters(Path.Combine(dir, "MDATA5.MDR"), Monsters(), Version);
        MdrWriter.WriteDungeon(Path.Combine(dir, "MDATA11.MDR"), Dungeon());
    }

    // ---- MDATA1: races, guilds and the four name tables ----

    public static List<Race> Races() => new()
    {
        new Race { Name = "Plainfolk", Min = new[] { 5, 5, 5, 5, 5, 5 }, Max = new[] { 18, 18, 18, 18, 18, 18 },
                   Size = RaceSize.Normal, AlignMask = 7, BonusPoints = 6, MaxAge = 120, ExpFactor = 3,
                   Resistances = new int[Effects.ResistanceKinds] },
        new Race { Name = "Tallkin", Min = new[] { 8, 3, 3, 8, 3, 4 }, Max = new[] { 22, 16, 16, 20, 16, 17 },
                   Size = RaceSize.Big, AlignMask = 7, BonusPoints = 5, MaxAge = 220, ExpFactor = 6,
                   Resistances = new[] { 30, 30, 0, 0, 20, 20, 0, 0, 30, 30, 0 } },
        new Race { Name = "Stoutling", Min = new[] { 3, 6, 6, 3, 6, 7 }, Max = new[] { 16, 20, 19, 17, 19, 20 },
                   Size = RaceSize.Small, AlignMask = 3, BonusPoints = 8, MaxAge = 300, ExpFactor = 5,
                   Resistances = new[] { 0, 0, 40, 40, 0, 0, 50, 0, 0, 0, 0 } },
    };

    /// <summary>A spell table of 19 categories with these (category, guild levels per spell level, highest spell level) set.</summary>
    private static (float[] Rate, int[] Max) SpellTable(params (int Category, float Rate, int Max)[] set)
    {
        var rate = new float[19]; var max = new int[19];
        foreach (var (c, r, m) in set) { rate[c] = r; max[c] = m; }
        return (rate, max);
    }

    public static List<GuildDef> Guilds()
    {
        // categories as Spellbook numbers them: 0 Fire, 1 Cold, 9 Heal, 15 Opening, 16 Location, 17 Protection
        var nomad = SpellTable((15, 4, 1), (16, 1, 1));
        var brawler = SpellTable((16, 1, 1));
        var chanter = SpellTable((0, 1, 15), (1, 1, 15), (9, 1, 15), (16, 1, 15), (17, 2, 6));
        return new()
        {
            new GuildDef { Name = GuildData.StartingGuild, AvgHits = 4, HitsCapLevel = 25, LateHits = 1, ExpFactor = 10,
                           ReqStats = new[] { 1, 1, 1, 1, 1, 1 }, AlignMask = 7, RaceMask = 0x7, QuestPercent = 20,
                           Abilities = new[] { 3f, 0, 0, 0, 0.8f, 3, 0 }, SpellRate = nomad.Rate, SpellMax = nomad.Max,
                           GoldFactor = 2, ItemUseFactor = 1.0f, AttPerLevel = 2, DefPerLevel = 2, AdMaxLevel = 100, AdEvery = 2 },
            new GuildDef { Name = "Brawler", AvgHits = 6, HitsCapLevel = 20, LateHits = 2, ExpFactor = 14,
                           ReqStats = new[] { 12, 5, 5, 9, 3, 7 }, AlignMask = 7, RaceMask = 0x3, QuestPercent = 20,
                           Abilities = new[] { 1f, 0, 6, 4, 1.1f, 2, 0 }, SpellRate = brawler.Rate, SpellMax = brawler.Max,
                           GoldFactor = 1, ItemUseFactor = 1.1f, AttPerLevel = 3, DefPerLevel = 3, AdMaxLevel = 300, AdEvery = 3 },
            new GuildDef { Name = "Chanter", AvgHits = 2, HitsCapLevel = 25, LateHits = 2, ExpFactor = 24,
                           ReqStats = new[] { 5, 12, 12, 7, 5, 8 }, AlignMask = 7, RaceMask = 0x5, QuestPercent = 15,
                           Abilities = new[] { 2f, 0, 0, 0, 0.5f, 2, 0 }, SpellRate = chanter.Rate, SpellMax = chanter.Max,
                           GoldFactor = 3, ItemUseFactor = 1.3f, AttPerLevel = 1.2f, DefPerLevel = 2, AdMaxLevel = 400, AdEvery = 4 },
        };
    }

    /// <summary>
    /// The item type numbers the code tests (<see cref="Loot.TypeNames"/> lists the 35), each
    /// pointing at a wear class: what can be worn one at a time, what is used up (0, no limit) and
    /// the one class of oddments (-1, no limit), as <see cref="Character.MaxWornOfClass"/> reads them.
    /// </summary>
    private static readonly int[] ClassOfType =
    {
        0, 0, 0, 0, 0, 0, 0, 0,     // 0 hands, 1-7 weapons
        1, 1, 1, 2, 3, 3, 4, 4,     // body armour, shield, head, hands
        5, 6, 7, 8, 9, 10, 11,      // cloak, wrist, garment, waist, feet, ring, neck
        12, 12, 12, 12, 12, 12,     // 23-28 used up
        13, 12, 12, 14, 15, 16,     // 29 a worn oddity, 30-31 used up, 32 another, 33 oddments, 34 crest
    };

    private static readonly int[] MaxWorn = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, -1, 1 };

    public static MdrWriter.Mdata1Contents Mdata1() => new(
        Version, Races(), Guilds(),
        ItemTypes: ClassOfType.Select((cls, i) => new NamedIndex($"Test Kind {i}", cls)).ToList(),
        ItemDescriptions: MaxWorn.Select((n, i) => new NamedIndex($"Test Class {i}", n)).ToList(),
        MonsterClasses: MonsterClassNames.Select((name, i) => new NamedIndex(name, i)).ToList(),
        MonsterGroups: MonsterClassNames.Select(name => new NamedIndex(name + " Kin", 0)).ToList());

    /// <summary>The four monster classes, Type 0 to 3; each its own group.</summary>
    private static readonly string[] MonsterClassNames = { "Biped", "Ooze", "Critter", "Spook" };

    // ---- MDATA2: spells ----

    public static List<Spell> Spells() => new()
    {
        new Spell { Name = "Fixture Spark", Id = 0, Category = 0, Level = 1, U4 = 8, AffectMonster = 1, AffectGroup = 1,
                    Damage1 = 1, Damage2 = 6, SpecialEffect = 1, Effect = SpellEffect.Damage, Required = new short[] { 0, 10, 10, 0, 0, 0, 0 }, ResistIndex = 0 },
        new Spell { Name = "Fixture Frost", Id = 1, Category = 1, Level = 2, U4 = 14, AffectMonster = 1, AffectGroup = 2,
                    Damage1 = 2, Damage2 = 8, SpecialEffect = 1, Effect = SpellEffect.Damage, Required = new short[] { 0, 12, 11, 0, 0, 0, 0 }, ResistIndex = 1 },
        new Spell { Name = "Fixture Mend", Id = 2, Category = 9, Level = 1, U4 = 6,
                    Damage1 = 3, Damage2 = 9, SpecialEffect = 1, Effect = SpellEffect.Damage, Required = new short[] { 0, 0, 11, 0, 0, 0, 0 }, ResistIndex = -1 },
        new Spell { Name = "Fixture Glimpse", Id = 3, Category = 16, Level = 1, U4 = 5,
                    Effect = SpellEffect.DepthPerception, Required = new short[] { 0, 10, 10, 0, 0, 0, 0 }, ResistIndex = -1 },
        new Spell { Name = "Fixture Unlock", Id = 4, Category = 15, Level = 1, U4 = 10,
                    Effect = SpellEffect.OpenFirst, Required = new short[] { 0, 11, 11, 0, 0, 11, 0 }, ResistIndex = -1 },
        new Spell { Name = "Fixture Ward", Id = 5, Category = 17, Level = 2, U4 = 12,
                    Effect = SpellEffect.Protect, Required = new short[] { 0, 12, 12, 0, 0, 0, 0 }, ResistIndex = -1 },
    };

    // ---- MDATA3: items ----

    /// <summary>
    /// The items, by type and then by the level they are found from, as the treasure pick walks
    /// them (<see cref="Loot"/>). Item 0 is the bare hands. Guilds is a mask in guild order.
    /// </summary>
    public static List<Item> Items()
    {
        const int all = 0x7, fighters = 0x3, unaligned = 0x1, anyAlign = 0xE;
        Item Make(short id, string name, short type, short att, short def, int price, short floor, short rarity,
                  int guilds, int align, short hands, short swings = 0, short scale = 1, float dmg = 1.0f,
                  short[]? req = null, short[]? mod = null, short cursed = 0) => new()
        {
            Id = id, Name = name, Type = type, Att = att, Def = def, Price = price, Floor = floor, Rarity = rarity,
            Guilds = guilds, AlignmentFlags = align, NHands = hands, Swings = swings, LevelScale = scale, DamageMod = dmg,
            StatsRequired = req ?? new short[7], StatsMod = mod ?? new short[7], Cursed = cursed, ClassRestricted = -1,
        };
        var items = new List<Item>
        {
            Make(0, "Fixture Fists", 0, 0, 0, 0, 0, 0, all, unaligned, hands: 2, swings: 1, scale: 0),
            Make(1, "Test Dagger", 1, 4, 0, 150, 0, 30, all, unaligned, hands: 1, swings: 2, req: new short[] { 6, 0, 0, 0, 0, 6, 0 }),
            Make(2, "Test Sword", 3, 8, 0, 900, 1, 40, fighters, anyAlign, hands: 1, swings: 1, scale: 2, dmg: 1.1f, req: new short[] { 10, 0, 0, 0, 0, 0, 0 }),
            Make(3, "Test Mace", 5, 6, 1, 2500, 2, 30, all, anyAlign, hands: 1, swings: 1, scale: 3),
            Make(4, "Test Jerkin", 8, 0, 4, 300, 0, 30, all, unaligned, hands: 0),
            Make(5, "Test Hauberk", 9, 0, 8, 4000, 2, 20, fighters, anyAlign, hands: 0, scale: 3, req: new short[] { 12, 0, 0, 0, 0, 0, 0 }),
            Make(6, "Test Buckler", 11, 0, 3, 500, 1, 30, fighters, anyAlign, hands: 1, scale: 2),
            Make(7, "Test Cap", 12, 0, 2, 120, 0, 30, all, unaligned, hands: 0),
            Make(8, "Test Boots", 20, 0, 1, 250, 1, 30, all, anyAlign, hands: 0),
            Make(9, "Test Ring", 21, 0, 0, 8000, 3, 10, all, anyAlign, hands: 0, scale: 4, mod: new short[] { 1, 0, 0, 0, 0, 0, 0 }),
        };
        // a potion of the mending spell: SpellIndex counts from 1 (ItemUse.SpellOf)
        var tonic = Make(10, "Test Tonic", 23, 0, 0, 200, 1, 30, all, unaligned, hands: 0);
        tonic.SpellIndex = 3; tonic.SpellId = 2; tonic.SpellLvl = 1; tonic.Charges = 1;
        items.Add(tonic);
        items.Add(Make(11, "Test Shackle", 33, 0, -2, 400, 1, 10, all, unaligned, hands: 0, cursed: 1));
        // a crest for each guild: item 299 + guild, special type 500 + guild, that guild's bit only
        var guilds = Guilds();
        for (int g = 0; g < guilds.Count; g++)
        {
            var crest = Make((short)(Guildmasters.CrestIdBase + g), guilds[g].Name + " Test Crest", 34, 4, 4, 0, 0, 0,
                             1 << g, unaligned, hands: 0, scale: 0, dmg: 0, mod: new short[] { 1, 0, 0, 0, 0, 1, 0 });
            crest.SpecialType = (short)(ItemUse.GuildIssuedBase + g);
            items.Add(crest);
        }
        return items;
    }

    // ---- MDATA5: monsters ----

    public static List<Monster> Monsters()
    {
        var list = new List<Monster>();
        void Add(string name, short type, short level, short att, short def, short hits, short groups, short pic, short chance)
        {
            short i = (short)list.Count;
            list.Add(new Monster
            {
                Name = name, Id = i, Type = type, LevelFound = level, Att = att, Def = def, Hits = hits,
                NumGroups = groups, PicId = pic, Chance = chance, LockedChance = 20,
                Resistances = new short[12], BoxChance = new short[] { 60, 40, 5, 0 },
                AlignmentValue = 1, InGroup = 2, GoldFactor = 4, TrapFlags = 15, GuildLevel = (short)(level * 2),
                Stats = new short[] { 9, 0, 0, 9, 0, 7, 0 }, DamageMod = 1.0f,
                // what a body may drop: item types, -1 for any type
                Items = new short[] { 1, 3, 5, 8, 9, 11, 12, 20, 23, -1, 0 },
                Subtype = i, CompanionSubtype = i, Size = 2, ItemDropLevel = level, ItemChance = 50,
            });
        }
        Add("Fixture Dummy", 0, 1, 20, 5, 5, 1, 2, 10);     // record 0: in no class, never drawn
        Add("Fixture Goblin", 0, 1, 22, 6, 6, 2, 2, 50);
        Add("Fixture Bandit", 0, 2, 35, 15, 12, 2, 14, 30);
        Add("Fixture Brute", 0, 3, 50, 25, 22, 1, 21, 20);
        Add("Fixture Slime", 1, 1, 15, 2, 8, 1, 5, 40);
        Add("Fixture Sludge", 1, 2, 25, 8, 14, 1, 6, 30);
        Add("Fixture Rat", 2, 1, 18, 4, 4, 3, 8, 50);
        Add("Fixture Bat", 2, 2, 26, 10, 9, 2, 12, 30);
        Add("Fixture Wolf", 2, 3, 40, 18, 18, 2, 16, 25);
        Add("Fixture Wisp", 3, 2, 30, 20, 10, 1, 25, 20);
        Add("Fixture Ghost", 3, 3, 45, 30, 20, 1, 30, 15);
        return list;
    }

    // ---- MDATA11: the dungeon ----

    /// <summary>
    /// One level being laid out: rooms and corridors are rectangles of an area id, everything else
    /// is rock. Where two different areas meet, or an area meets rock, there is a wall, unless a
    /// door was put there. A square's east and north walls are its own; its west and south ones are
    /// its neighbours' (<see cref="DungeonNav.EdgeAt"/>), so nothing open is put on the map's
    /// west or south edge.
    /// </summary>
    private sealed class Plan
    {
        private readonly int[,] _area = new int[Width, Height];
        private readonly Dictionary<(int, int, bool), int> _doors = new();
        private readonly long[,] _extra = new long[Width, Height];
        public readonly List<Floor.Area> Areas = new();
        public readonly List<Floor.Teleporter> Teleporters = new();
        public readonly List<Floor.Chute> Chutes = new();

        /// <summary>A rectangle of area <paramref name="id"/>, corners included, with its spawn mask (bit = monster class) and lair.</summary>
        public void Room(int id, int x0, int y0, int x1, int y1, int spawnMask, short lair = 0)
        {
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) _area[x, y] = id;
            while (Areas.Count <= id) Areas.Add(default);
            Areas[id] = new Floor.Area { SpawnMask = spawnMask, LairId = lair };
        }

        /// <summary>A door between (x, y) and the square east of it.</summary>
        public void DoorEast(int x, int y, bool secret = false) => _doors[(x, y, false)] = secret ? Floor.SECRET_DOOR_EAST : Floor.DOOR_EAST;

        /// <summary>A door between (x, y) and the square north of it.</summary>
        public void DoorNorth(int x, int y, bool secret = false) => _doors[(x, y, true)] = secret ? Floor.SECRET_DOOR_NORTH : Floor.DOOR_NORTH;

        public void Mark(int x, int y, int bit) => _extra[x, y] |= 1L << bit;

        /// <summary>A teleporter square; the record's squares count from 1, as the dungeon reads them.</summary>
        public void Teleporter(int x, int y, int toX, int toY, int toLevel)
        {
            Mark(x, y, Floor.TELEPORTER);
            Teleporters.Add(new Floor.Teleporter { X = (short)(x + 1), Y = (short)(y + 1), X2 = (short)(toX + 1), Y2 = (short)(toY + 1), Z2 = (short)toLevel });
        }

        /// <summary>A chute square dropping <paramref name="depth"/> levels; the record counts from 1.</summary>
        public void Chute(int x, int y, int depth)
        {
            Mark(x, y, Floor.CHUTE);
            Chutes.Add(new Floor.Chute { X = (short)(x + 1), Y = (short)(y + 1), Depth = (short)depth });
        }

        private int At(int x, int y) => x < Width && y < Height ? _area[x, y] : 0;

        private long Edge(int here, int there, (int, int, bool) key, int wallBit)
        {
            if (here == there) return 0;
            return _doors.TryGetValue(key, out int door) ? 1L << door : 1L << wallBit;
        }

        public Floor Build(int level)
        {
            var f = new Floor { Width = Width, Height = Height, LevelNum = (short)level, Fields = new Floor.Field[Width * Height] };
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int a = _area[x, y];
                    long flags = _extra[x, y];
                    if (a == 0) flags |= 1L << Floor.ROCK;
                    flags |= Edge(a, At(x + 1, y), (x, y, false), Floor.WALL_EAST);
                    flags |= Edge(a, At(x, y + 1), (x, y, true), Floor.WALL_NORTH);
                    f.Fields[y * Width + x] = new Floor.Field { AreaId = (short)a, Flags = flags };
                }
            f.Areas.AddRange(Areas);
            while (f.Areas.Count < MdrWriter.AreaRecords) f.Areas.Add(default);
            f.HeaderAreaCount = Areas.Count - 1;     // the areas in use: 1 to N (0 is rock)
            f.Teleporters.AddRange(Teleporters);
            f.Chutes.AddRange(Chutes);
            return f;
        }
    }

    /// <summary>Where the party comes in from town: the level 1 stairs up (CityWindow.FindStart takes the first).</summary>
    public static readonly (int X, int Y) Entrance = (2, 2);

    /// <summary>
    /// Three levels. Stairs keep their square from level to level (a stair down at (x, y) lands on
    /// the stair up at (x, y) below), and the chute on level 2 lands in a room on level 3.
    /// <code>
    ///   level 1: room (1,1)-(6,6) with the town stairs up at (2,2), a door east to a corridor along
    ///            y 4, a door into a room (11,2)-(16,7) (a Fixture Rat lair), a door north into a
    ///            corridor up x 13, a door into a room (10,12)-(16,17) with the stairs down at (14,15)
    ///   level 2: room (12,13)-(17,18) with the stairs up at (14,15); a door west to a corridor along
    ///            y 16 with a teleporter at (8,16) back to (14,13); a door into a room (1,14)-(4,19)
    ///            (a Fixture Sludge lair) with a chute at (3,15) and the stairs down at (2,17)
    ///   level 3: room (1,13)-(6,19) with the stairs up at (2,17) and the chute's landing at (3,15);
    ///            a door east to a corridor along y 16, and a secret door into a room (13,13)-(18,19)
    ///            (a Fixture Ghost lair)
    /// </code>
    /// </summary>
    public static List<Floor> Dungeon()
    {
        var one = new Plan();
        one.Room(1, 1, 1, 6, 6, 0x5);
        one.Room(2, 7, 4, 10, 4, 0x4);
        one.Room(3, 11, 2, 16, 7, 0x7, lair: 6);
        one.Room(4, 13, 8, 13, 11, 0x6);
        one.Room(5, 10, 12, 16, 17, 0xF);
        one.DoorEast(6, 4); one.DoorEast(10, 4); one.DoorNorth(13, 7); one.DoorNorth(13, 11);
        one.Mark(Entrance.X, Entrance.Y, Floor.STAIRS_UP);
        one.Mark(14, 15, Floor.STAIRS_DOWN);

        var two = new Plan();
        two.Room(1, 12, 13, 17, 18, 0xF);
        two.Room(2, 5, 16, 11, 16, 0x6);
        two.Room(3, 1, 14, 4, 19, 0xB, lair: 5);
        two.DoorEast(11, 16); two.DoorEast(4, 16);
        two.Mark(14, 15, Floor.STAIRS_UP);
        two.Teleporter(8, 16, 14, 13, 2);
        two.Chute(3, 15, 1);
        two.Mark(2, 17, Floor.STAIRS_DOWN);

        var three = new Plan();
        three.Room(1, 1, 13, 6, 19, 0xF);
        three.Room(2, 7, 16, 12, 16, 0xC);
        three.Room(3, 13, 13, 18, 19, 0xF, lair: 10);
        three.DoorEast(6, 16); three.DoorEast(12, 16, secret: true);
        three.Mark(2, 17, Floor.STAIRS_UP);

        return new List<Floor> { one.Build(1), two.Build(2), three.Build(3) };
    }
}
