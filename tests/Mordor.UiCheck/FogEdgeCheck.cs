using System.Reflection;
using Avalonia.Headless;
using Avalonia.Threading;
using Mordor.App;
using Mordor.Core;

/// <summary>
/// Issue #11: a fog square on the level's top or right edge, walked into that edge, is drawn with
/// its outer side. The bump is run as the dungeon window runs it (DungeonNav.WallAhead, then
/// MapRecord.OnBumpInFog - fn0B1B's wall bit and the fog branch of fn1521, MAZEMOD.BAS:157-182,
/// MAINMOD2.BAS:3863-3891), and the Automap plots the level from what was written down
/// (medPlotMaze, FRMAUTOM.BAS:427-430): the wall icon on the square's north or east side lands on
/// the picture's top row or right column (medDrawSquare, FRMAUTOM.BAS:366-369). Before the bump
/// the fog square's outer row is the fog icon's alone; level 1 of the real game, nothing written.
/// </summary>
static class FogEdgeCheck
{
    public static int Run(string game)
    {
        Console.WriteLine("\nthe Automap draws the outer side of a fog square on the top and right edges once walked into (issue #11):");
        int failed = 0;
        void Check(string what, object? got, object? want)
        {
            bool ok = Equals(got, want);
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what}{(ok ? "" : $": got {got}, want {want}")}");
            if (!ok) failed++;
        }
        void Pump()
        {
            for (int i = 0; i < 6; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        }
        var floors = MdrLoader.LoadDungeon(GameFiles.DataFile(game, "MDATA11.MDR"));
        var fl = floors[0];
        if (!fl.Has(29, 29, Floor.FOG) || !fl.Has(10, 29, Floor.FOG) || !fl.Has(29, 25, Floor.FOG))
        {
            Console.WriteLine("  skip  level 1 has not the full game's fog on its edges");
            return 0;
        }
        const int pitch = AutomapPics.Pitch;
        int w = fl.Width, h = fl.Height, bw = w * pitch + 1;
        const int white = unchecked((int)0xFFFFFFFF);

        // three fog squares walked onto - the top right corner, one on the top edge, one on the right
        var squares = new[] { (x: 29, y: 29), (x: 10, y: 29), (x: 29, y: 25) };
        var map = new SharedMap();
        map.Merge(1, squares, w);
        foreach (var (x, y) in squares) map.Record(1, x, y, w, Floor.FOG);   // the feature goes down in fog (MAZEMOD.BAS:1614-1617)

        int[] Plot()
        {
            var win = new AutomapWindow(floors, map, 1, (1, 0, 0, Facing.North), lost: true);
            win.RedrawLevel(); Pump();
            var px = (int[])typeof(AutomapWindow).GetField("_pxWith", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(win)!;
            win.Close();
            return px;
        }
        // the white pixels along a square's top side (picture row 0) and its right side (the last column)
        int TopWhite(int[] px, int x) => Enumerable.Range(x * pitch + 1, pitch - 1).Count(c => px[c] == white);
        int RightWhite(int[] px, int y) => Enumerable.Range((h - 1 - y) * pitch + 1, pitch - 1).Count(r => px[r * bw + bw - 1] == white);

        var before = Plot();
        Check("before any bump: no wall along the top of fog (11,30)", TopWhite(before, 10), 0);
        Check("before any bump: no wall down the right of fog (30,26)", RightWhite(before, 25), 0);

        void Bump(int x, int y, Facing facing)
        {
            var nav = new DungeonNav(fl, x, y, facing);
            if (!nav.MoveForward() && nav.WallAhead(nav.Facing)) MapRecord.OnBumpInFog(map, fl, nav.X, nav.Y, nav.Facing);
        }
        Bump(10, 29, Facing.North);
        Bump(29, 25, Facing.East);
        Bump(29, 29, Facing.North);
        Bump(29, 29, Facing.East);
        var after = Plot();
        Check("walked north into the edge: the top of fog (11,30) is a wall", TopWhite(after, 10), pitch - 1);
        Check("walked east into the edge: the right of fog (30,26) is a wall", RightWhite(after, 25), pitch - 1);
        Check("the corner: its top and its right", (TopWhite(after, 29), RightWhite(after, 29)), (pitch - 1, pitch - 1));
        return failed;
    }
}
