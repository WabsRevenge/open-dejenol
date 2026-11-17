using Mordor.Core;
using Mordor.Modding;

namespace Mordor.Mod.Cartographer;

/// <summary>
/// The squares a route can be told to count extra for, or to keep off: every feature the Automap can draw
/// that does something to a party standing on it. Stairs are a way, not a hazard; a teleporter or a chute
/// moves the party (see <see cref="RoutePlanner"/>); the facing squares are followed exactly.
/// </summary>
public enum Hazard { Pit, Water, Quicksand, Fog, Rotator, Extinguisher, AntiMagic, Stud }

/// <summary>What a route may do and what it counts.</summary>
public sealed class RouteRules
{
    /// <summary>Whether the player's waypoints are taken as ways from their source to their destination.</summary>
    public bool UseWaypoints { get; init; } = true;

    /// <summary>Extra actions counted for stepping onto each hazard; null keeps off it. A hazard not named counts nothing.</summary>
    public IReadOnlyDictionary<Hazard, int?> Penalties { get; init; } = new Dictionary<Hazard, int?>();

    /// <summary>
    /// What one cast of Ethereal Portal counts (the cast and whatever the player reckons it costs), or null:
    /// never cast (the default).
    /// </summary>
    public int? PortalCost { get; init; }

    /// <summary>The Automap bit each hazard is drawn from.</summary>
    public static int Bit(Hazard h) => h switch
    {
        Hazard.Pit => Floor.PIT,
        Hazard.Water => Floor.WATER,
        Hazard.Quicksand => Floor.QUICKSAND,
        Hazard.Fog => Floor.FOG,
        Hazard.Rotator => Floor.ROTATOR,
        Hazard.Extinguisher => Floor.EXTINGUISHER,
        Hazard.AntiMagic => Floor.ANTIMAGIC,
        _ => Floor.STUD,
    };
}

/// <summary>How the party came onto a square of a route.</summary>
public enum RouteMove { Start, Step, Stairs, Waypoint, Portal }

/// <summary>One square of a route, in order, and how the party came onto it (turns stay on a square and make none).</summary>
public sealed record RouteNode(AutomapSquare Square, RouteMove How);

/// <summary>A route found: its squares and what it costs.</summary>
public sealed class Route
{
    public required IReadOnlyList<RouteNode> Nodes { get; init; }
    /// <summary>Everything counted: steps, turns, stairs, the portals' cost and the hazards' extra.</summary>
    public int Actions => Steps + Turns + Stairs + PortalActions + Extra;
    /// <summary>Ethereal Portals cast.</summary>
    public int Portals { get; init; }
    /// <summary>What the portals count, all told.</summary>
    public int PortalActions { get; init; }
    /// <summary>Steps forward (Up).</summary>
    public int Steps { get; init; }
    /// <summary>Turns: left, right or about (each one key).</summary>
    public int Turns { get; init; }
    /// <summary>Stairs (or a waypoint on stairs) taken with t.</summary>
    public int Stairs { get; init; }
    /// <summary>Waypoints passed through (they cost no action of their own).</summary>
    public int Waypoints { get; init; }
    /// <summary>The hazards' extra actions.</summary>
    public int Extra { get; init; }
    /// <summary>The route crosses an edge the map cannot know: between two squares stood on only in fog.</summary>
    public bool ThroughFog { get; init; }
    /// <summary>The destination has never been walked: the route ends one step into the unknown.</summary>
    public bool EndsUnexplored { get; init; }
}

/// <summary>
/// The fewest actions from one square to another, reckoned on the Automap alone - what the map draws,
/// never the maze - as the player would walk it: a step forward is one action and so is each turn (left,
/// right or about face, one key each); <c>t</c> on recorded stairs is one, and lands on the same square a
/// level down or up, facing the same way. There is no step back in the game, so there is none here.
///
/// - Only squares the party has stood on (drawn black) are walked through, and a waypoint's destination.
///   The destination may be any square; stepping into one never walked is allowed only as the last step.
/// - An edge blocks when the map shows a wall on it, and the edges of the level are walls. A door, and a
///   secret door once found, does not. Walls are written down from every square stood on out of fog, so
///   the only edges the map cannot know are those between two squares stood on only in fog
///   (<see cref="Route.ThroughFog"/>); they are taken as the map draws them, open.
/// - A recorded facing square turns the party as it does in the game; a rotator leaves it facing any way,
///   counted at its worst - one turn to go on.
/// - A teleporter or a chute the map shows moves the party somewhere the map does not say, so a route
///   never steps onto one unless it is the destination - or it is a waypoint's source.
/// - With <see cref="RouteRules.UseWaypoints"/>, a waypoint is the player's word for where its source
///   leads: stepping onto the source puts the party on the destination at once, facing the same way, as a
///   teleporter does (the destination's own square is then entered: its hazard counted, its facing
///   followed); on recorded stairs it is taken with <c>t</c> instead, as the stairs are. Its source is
///   not otherwise walked across.
/// - A hazard's extra is counted on stepping onto it; one kept off (null) is never stepped onto unless it
///   is the destination, where nothing extra is counted.
/// - With <see cref="RouteRules.PortalCost"/>, Ethereal Portal: one square the way the party faces, whatever
///   is in the way, wrapping round at the level's edge (DungeonNav.StepThrough, MAINMOD2.BAS:1170-1184), the
///   square then entered as a step enters it (the square routine runs, 1209-1210). Only onto a square stood
///   on - one never walked may be rock, which kills the party - so never onto a destination not yet
///   explored; and never cast where the map shows anti-magic or an extinguisher, which refuse it
///   (mdrcharCheckCast).
/// </summary>
public static class RoutePlanner
{
    private const int AnyWay = 4;   // the facing after a rotator: unknown
    private const int Facings = 5;

    private sealed class Level
    {
        public int Width, Height;
        public bool[] Walked = Array.Empty<bool>();
        public long[] Flags = Array.Empty<long>();
        public bool Has(int x, int y, int bit) => (Flags[y * Width + x] & (1L << bit)) != 0;
        public bool In(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    }

    private enum Via : byte { None, Seed, Step, Turn, Stairs, StepWaypoint, StairsWaypoint, Portal, PortalWaypoint }

    /// <param name="levelSize">A level's width and height (0 by 0: none).</param>
    /// <param name="deepest">The deepest level the map shows.</param>
    /// <param name="record">What the map has written down about a square.</param>
    /// <param name="waypoints">Each waypoint's destination by its source.</param>
    /// <param name="from">Where to start; <paramref name="facing"/> null starts facing whichever way is best.</param>
    /// <param name="to">Where to go.</param>
    /// <returns>The route, or null when the map knows of none.</returns>
    public static Route? Plan(Func<int, (int Width, int Height)> levelSize, int deepest, Func<AutomapSquare, AutomapRecord> record,
                              IReadOnlyDictionary<AutomapSquare, AutomapSquare> waypoints, RouteRules rules,
                              AutomapSquare from, Facing? facing, AutomapSquare to)
    {
        if (deepest < 1) return null;
        var levels = new Level?[deepest + 1];
        var offset = new int[deepest + 2];
        for (int l = 1; l <= deepest; l++)
        {
            var (w, h) = levelSize(l);
            offset[l + 1] = offset[l];
            if (w <= 0 || h <= 0) continue;
            var lv = new Level { Width = w, Height = h, Walked = new bool[w * h], Flags = new long[w * h] };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var r = record(new AutomapSquare(l, x, y));
                lv.Walked[y * w + x] = r.Walked;
                lv.Flags[y * w + x] = r.Flags;
            }
            levels[l] = lv;
            offset[l + 1] = offset[l] + w * h * Facings;
        }

        Level? At(AutomapSquare s) => s.Level >= 1 && s.Level <= deepest && levels[s.Level] is { } lv && lv.In(s.X, s.Y) ? lv : null;
        if (At(from) == null || At(to) == null) return null;

        int total = offset[deepest + 1];
        int Index(AutomapSquare s, int f) => offset[s.Level] + (s.Y * levels[s.Level]!.Width + s.X) * Facings + f;
        AutomapSquare SquareOf(int i)
        {
            int l = 1;
            while (offset[l + 1] <= i) l++;
            int cell = (i - offset[l]) / Facings;
            return new AutomapSquare(l, cell % levels[l]!.Width, cell / levels[l]!.Width);
        }

        var cost = new int[total];
        Array.Fill(cost, int.MaxValue);
        var prev = new int[total];
        var via = new Via[total];
        var extra = new int[total];          // the hazards' extra on the move that came here
        var landedFrom = new int[total];     // a waypoint's source square, for the move that came through it
        var fogEdge = new bool[total];
        var queue = new PriorityQueue<int, (int Cost, long Seq)>();
        long seq = 0;

        void Offer(int i, int c, int p, Via how, int ext = 0, int src = -1, bool fog = false)
        {
            if (c >= cost[i]) return;
            cost[i] = c; prev[i] = p; via[i] = how; extra[i] = ext; landedFrom[i] = src; fogEdge[i] = fog;
            queue.Enqueue(i, (c, seq++));
        }

        if (facing is { } f0) Offer(Index(from, (int)f0), 0, -1, Via.Seed);
        else for (int f = 0; f < 4; f++) Offer(Index(from, f), 0, -1, Via.Seed);

        int? Penalty(Level lv, int x, int y)
        {
            int sum = 0;
            foreach (var (hz, p) in rules.Penalties)
            {
                if (!lv.Has(x, y, RouteRules.Bit(hz))) continue;
                if (p == null) return null;
                sum += Math.Max(0, p.Value);
            }
            return sum;
        }

        AutomapSquare? Waypoint(AutomapSquare s) =>
            rules.UseWaypoints && waypoints.TryGetValue(s, out var d) && At(d) != null ? d : null;

        // the facing squares, in the square routine's order, then the rotator over them (MAZEMOD.BAS:1714-1741)
        int FacingAfter(Level lv, int x, int y, int f)
        {
            if (lv.Has(x, y, Floor.FACE_NORTH)) f = (int)Facing.North;
            if (lv.Has(x, y, Floor.FACE_EAST)) f = (int)Facing.East;
            if (lv.Has(x, y, Floor.FACE_SOUTH)) f = (int)Facing.South;
            if (lv.Has(x, y, Floor.FACE_WEST)) f = (int)Facing.West;
            if (lv.Has(x, y, Floor.ROTATOR)) f = AnyWay;
            return f;
        }

        // a square walked onto at the end of a move (a step's, or a waypoint's landing): null, it may not be;
        // else its extra and the state it leaves the party in. A waypoint's destination is the player's
        // word and may be a square never walked (a teleporter leaves a party lost where it lands, and the
        // lost do not map), so it is landed on all the same
        (int Extra, int State)? Enter(AutomapSquare s, int f, bool landing = false)
        {
            var lv = At(s)!;
            if (s == to) return (0, Index(s, f is AnyWay ? 0 : f));
            if (!landing && !lv.Walked[s.Y * lv.Width + s.X]) return null;
            int? pen = Penalty(lv, s.X, s.Y);
            if (pen == null) return null;
            return (pen.Value, Index(s, FacingAfter(lv, s.X, s.Y, f)));
        }

        static bool Blocked(Level lv, int x, int y, Facing d) => d switch
        {
            Facing.North => y + 1 >= lv.Height || lv.Has(x, y, Floor.WALL_NORTH),
            Facing.East => x + 1 >= lv.Width || lv.Has(x, y, Floor.WALL_EAST),
            Facing.South => y - 1 < 0 || lv.Has(x, y - 1, Floor.WALL_NORTH),
            _ => x - 1 < 0 || lv.Has(x - 1, y, Floor.WALL_EAST),
        };

        bool Clear(Level lv, int x, int y) => lv.Walked[y * lv.Width + x] && !lv.Has(x, y, Floor.FOG);

        int found = -1;
        while (queue.TryDequeue(out int i, out var pri))
        {
            if (pri.Cost != cost[i]) continue;
            var here = SquareOf(i);
            if (here == to) { found = i; break; }
            var lv = At(here)!;
            int f = (i - offset[here.Level]) % Facings;
            int c = cost[i];

            // onto the square ahead, by a step or a portal: the square routine as it has it
            void Arrive(AutomapSquare next, int move, Via plain, Via throughWaypoint, bool fog)
            {
                bool moves = lv.Has(next.X, next.Y, Floor.TELEPORTER) || lv.Has(next.X, next.Y, Floor.CHUTE);
                bool stairs = lv.Has(next.X, next.Y, Floor.STAIRS_UP) || lv.Has(next.X, next.Y, Floor.STAIRS_DOWN);
                var wp = Waypoint(next);
                if (next == to)
                {
                    if (Enter(next, f) is { } e) Offer(e.State, c + move, i, plain, 0, -1, fog);
                }
                else if (wp is { } dest && !stairs && lv.Walked[next.Y * lv.Width + next.X])
                {
                    // onto a waypoint's source: the party is on its destination at once
                    int? pen = Penalty(lv, next.X, next.Y);
                    if (pen != null && Enter(dest, f, landing: true) is { } e)
                        Offer(e.State, c + move + pen.Value + e.Extra, i, throughWaypoint, pen.Value + e.Extra, Index(next, f), fog);
                }
                else if (!moves && Enter(next, f) is { } e)
                    Offer(e.State, c + move + e.Extra, i, plain, e.Extra, -1, fog);
            }

            // a step forward
            if (f != AnyWay && !Blocked(lv, here.X, here.Y, (Facing)f))
            {
                var (dx, dy) = DungeonNav.Delta((Facing)f);
                var next = new AutomapSquare(here.Level, here.X + dx, here.Y + dy);
                Arrive(next, 1, Via.Step, Via.StepWaypoint, fog: !Clear(lv, here.X, here.Y) && !Clear(lv, next.X, next.Y));
            }

            // Ethereal Portal: the square ahead whatever is between, wrapping at the edge - onto ground stood on
            if (rules.PortalCost is { } portal && f != AnyWay
                && !lv.Has(here.X, here.Y, Floor.ANTIMAGIC) && !lv.Has(here.X, here.Y, Floor.EXTINGUISHER))
            {
                var (dx, dy) = DungeonNav.Delta((Facing)f);
                int nx = here.X + dx, ny = here.Y + dy;
                if (nx >= lv.Width) nx = 0;
                if (nx < 0) nx = lv.Width - 1;
                if (ny >= lv.Height) ny = 0;
                if (ny < 0) ny = lv.Height - 1;
                if (lv.Walked[ny * lv.Width + nx] && !lv.Has(nx, ny, Floor.ROCK))
                    Arrive(new AutomapSquare(here.Level, nx, ny), Math.Max(0, portal), Via.Portal, Via.PortalWaypoint, fog: false);
            }

            // a turn: left, right or about from a known facing; from a rotator's, the one turn to whichever way
            for (int g = 0; g < 4; g++)
                if (g != f) Offer(offset[here.Level] + (i - offset[here.Level]) / Facings * Facings + g, c + 1, i, Via.Turn);

            // t on recorded stairs: the same square a level down or up, the same way faced
            if (f != AnyWay)
            {
                foreach (var (bit, dl) in new[] { (Floor.STAIRS_DOWN, 1), (Floor.STAIRS_UP, -1) })
                {
                    if (!lv.Has(here.X, here.Y, bit)) continue;
                    var land = new AutomapSquare(here.Level + dl, here.X, here.Y);
                    var ll = At(land);
                    if (ll == null || (land != to && !ll.Walked[land.Y * ll.Width + land.X])) continue;
                    Offer(Index(land, f), c + 1, i, Via.Stairs);
                }
                if ((lv.Has(here.X, here.Y, Floor.STAIRS_UP) || lv.Has(here.X, here.Y, Floor.STAIRS_DOWN))
                    && Waypoint(here) is { } dest && Enter(dest, f, landing: true) is { } e)
                    Offer(e.State, c + 1 + e.Extra, i, Via.StairsWaypoint, e.Extra);
            }
        }
        if (found < 0) return null;

        // back from the destination: the squares, and what each move counted
        var nodes = new List<RouteNode>();
        int steps = 0, turns = 0, takes = 0, wps = 0, ext = 0, portals = 0;
        bool throughFog = false;
        for (int i = found; i >= 0; i = prev[i])
        {
            var s = SquareOf(i);
            ext += extra[i];
            throughFog |= fogEdge[i];
            switch (via[i])
            {
                case Via.Step: steps++; nodes.Add(new RouteNode(s, RouteMove.Step)); break;
                case Via.Turn: turns++; break;
                case Via.Stairs: takes++; nodes.Add(new RouteNode(s, RouteMove.Stairs)); break;
                case Via.StepWaypoint:
                    steps++; wps++;
                    nodes.Add(new RouteNode(s, RouteMove.Waypoint));
                    nodes.Add(new RouteNode(SquareOf(landedFrom[i]), RouteMove.Step));
                    break;
                case Via.StairsWaypoint: takes++; wps++; nodes.Add(new RouteNode(s, RouteMove.Waypoint)); break;
                case Via.Portal: portals++; nodes.Add(new RouteNode(s, RouteMove.Portal)); break;
                case Via.PortalWaypoint:
                    portals++; wps++;
                    nodes.Add(new RouteNode(s, RouteMove.Waypoint));
                    nodes.Add(new RouteNode(SquareOf(landedFrom[i]), RouteMove.Portal));
                    break;
                default: nodes.Add(new RouteNode(s, RouteMove.Start)); break;
            }
        }
        nodes.Reverse();
        var end = At(to)!;
        return new Route
        {
            Nodes = nodes, Steps = steps, Turns = turns, Stairs = takes, Waypoints = wps, Extra = ext,
            Portals = portals, PortalActions = portals * Math.Max(0, rules.PortalCost ?? 0),
            ThroughFog = throughFog,
            EndsUnexplored = from != to && !end.Walked[to.Y * end.Width + to.X],
        };
    }
}
