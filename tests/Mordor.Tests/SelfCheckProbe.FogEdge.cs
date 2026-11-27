using Mordor.Core;

public static partial class SelfCheckProbe
{
    /// <summary>
    /// Issue #11 (2026-11-27): a fog square on the level's top or right edge gets its outer side on
    /// the map once you walk into it. Standing in fog writes no edge (MAZEMOD.BAS:1590, 1623-1628),
    /// but walking into a wall there does (MAINMOD2.BAS:3863-3891) - and fn0B1B, which decides that
    /// the step is blocked, answers a step off the level as a wall without reading any square
    /// (MAZEMOD.BAS:157-165, 181-182). MDATA11 has no wall bit along the east and north edges, so the
    /// port, which asked the square's wall bit alone, never wrote those two sides for a fog square.
    /// </summary>
    private static void FogEdgeChecks(string dir)
    {
        Console.WriteLine("\nwalking into the level's edge in fog puts the wall on the map (issue #11; MAZEMOD.BAS:157-182, MAINMOD2.BAS:3863-3891)");
        string Norm(string s) => s.Replace("\r\n", "\n");
        string maze = Norm(PortSource("decompiled-named", "MAZEMOD.BAS"));
        string main2 = Norm(PortSource("decompiled-named", "MAINMOD2.BAS"));

        // ---- the original ----
        Check("MAZEMOD.BAS: fn0B1B reads no square for a step off the level, facing any way",
              maze.Contains("        Case mc00D2\n            If  l00CE = gv1864.M420C Then l00D0 = False\n"
                          + "        Case mc00D4\n            If  l00CC = gv1864.M4216 Then l00D0 = False\n"
                          + "        Case mc00D6\n            l00CE = p00C6.Y - 1\n            If  l00CE = 0 Then l00D0 = False\n"
                          + "        Case mc00D8\n            l00CC = p00C6.X - 1\n            If  l00CC = 0 Then l00D0 = False\n"), true);
        Check("MAZEMOD.BAS: and answers it with the wall bit",
              maze.Contains("    Else\n        l00CA = setBit(gv1D44, l00C8, True)\n    End If\n    fn0B1B = l00CA"), true);
        Check("MAINMOD2.BAS: fn1521's step asks fn0B1B, and a wall in fog is written to the map",
              main2.Contains("                        l053C = fn0B1B(Chars(m00A6))\n                        If  testBit(m05F8, l053C) = False Then\n")
              && main2.Contains("                            If  testBit(mc0616, squareWalls(squareIndex(Chars(m00A6)))) = True Then\n")
              && main2.Contains("Const mc0616 = 20 ' &H14%")
              && main2.Contains("                                If  l054C > False And l054E > False And l054C <= gv1864.M4216 And l054E <= gv1864.M420C Then\n"), true);
        Check("MAZEMOD.BAS: standing in fog writes no edge of the level",
              maze.Contains("    If  Chars(m00C0).X = gv1864.M4216 And l028E(BitDark) = False Then\n")
              && maze.Contains("    If  Chars(m00C0).Y = gv1864.M420C And l028E(BitDark) = False Then\n"), true);

        // ---- the data ----
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "MDATA11.MDR"))) { Console.WriteLine("  skipped: no MDATA11.MDR"); return; }
        var floors = MdrLoader.LoadDungeon(System.IO.Path.Combine(dir, "MDATA11.MDR"));
        Check("MDATA11: no square on any level's east or north edge carries the wall bit there",
              floors.Sum(f => Enumerable.Range(0, f.Height).Count(y => f.Has(f.Width - 1, y, Floor.WALL_EAST))
                            + Enumerable.Range(0, f.Width).Count(x => f.Has(x, f.Height - 1, Floor.WALL_NORTH))), 0);
        if (_shareware) { Console.WriteLine("  skipped: full game only (level 1's fog)"); return; }
        var fl = floors[0];
        Check("level 1: fog on the right edge at (30,30) and (30,26), on the top edge at (11,30), and at (1,28) on the left",
              fl.Has(29, 29, Floor.FOG) && fl.Has(29, 25, Floor.FOG) && fl.Has(10, 29, Floor.FOG) && fl.Has(0, 27, Floor.FOG), true);

        // ---- the port: the dungeon window's blocked step, as it runs it ----
        long Bump(int x, int y, Facing facing, out bool moved)
        {
            var map = new SharedMap();
            var nav = new DungeonNav(fl, x, y, facing);
            moved = nav.MoveForward();
            if (!moved && nav.WallAhead(nav.Facing)) MapRecord.OnBumpInFog(map, fl, nav.X, nav.Y, nav.Facing);
            return map.SeenFlags(fl.LevelNum, x, y, fl.Width);
        }
        var east = new DungeonNav(fl, 29, 25, Facing.East);
        Check("on the right edge, facing out: the square has no wall bit, fn0B1B's answer is a wall",
              $"{east.EdgeAt(29, 25, Facing.East)} {east.WallAhead(Facing.East)}", "Open True");
        long e = Bump(29, 25, Facing.East, out bool movedE);
        Check("fog on the right edge, walked east into the edge: blocked, and the east wall on the map",
              (movedE, (e & (1L << Floor.WALL_EAST)) != 0).ToString(), "(False, True)");
        long n = Bump(10, 29, Facing.North, out bool movedN);
        Check("fog on the top edge, walked north into the edge: blocked, and the north wall on the map",
              (movedN, (n & (1L << Floor.WALL_NORTH)) != 0).ToString(), "(False, True)");
        long c1 = Bump(29, 29, Facing.North, out _), c2 = Bump(29, 29, Facing.East, out _);
        Check("fog in the top right corner: each way out writes its own side",
              ((c1 & (1L << Floor.WALL_NORTH)) != 0, (c2 & (1L << Floor.WALL_EAST)) != 0).ToString(), "(True, True)");
        var westMap = new SharedMap();
        var west = new DungeonNav(fl, 0, 27, Facing.West);
        bool westWall = west.WallAhead(Facing.West);
        if (!west.MoveForward() && westWall) MapRecord.OnBumpInFog(westMap, fl, 0, 27, Facing.West);
        Check("fog on the left edge, walked west: a wall to fn0B1B, but no square to write it on (MAINMOD2.BAS:3880)",
              (westWall, Enumerable.Range(0, fl.Width * fl.Height).All(i => westMap.SeenFlags(fl.LevelNum, i % fl.Width, i / fl.Width, fl.Width) == 0)).ToString(),
              "(True, True)");
        var clear = Enumerable.Range(0, fl.Height).First(y => !fl.Has(fl.Width - 1, y, Floor.FOG) && !fl.Has(fl.Width - 1, y, Floor.ROCK));
        long k = Bump(fl.Width - 1, clear, Facing.East, out _);
        Check("out of fog the bump writes nothing (the step onto the square wrote the edge already)", k, 0L);

        string dun = Norm(PortSource("src/Mordor.App", "DungeonWindow.axaml.cs"));
        Check("the dungeon window's blocked step asks WallAhead, fn0B1B's answer",
              dun.Contains("                    if (_nav.WallAhead(_nav.Facing))\n                        MapRecord.OnBumpInFog(_map, _nav.Floor, _nav.X, _nav.Y, _nav.Facing);"), true);
    }
}
