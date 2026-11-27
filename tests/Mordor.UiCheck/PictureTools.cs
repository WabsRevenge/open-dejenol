using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

/// <summary>
/// CI's picture tools, beside the readability pass (<see cref="ContrastCheck"/>) and the playthrough
/// (<see cref="PlaythroughCheck"/>) that draw the pictures. Neither opens a window nor needs a game folder:
/// they read and write PNGs with SkiaSharp, the library Avalonia draws with.
///
///   Mordor.UiCheck --compare &lt;baseline dir&gt; &lt;current dir&gt; &lt;out dir&gt;
///       the same-named pictures of two runs on one machine, pixel by pixel. Into &lt;out dir&gt;: for each
///       picture that changed, before.png, after.png and diff.png (the changed pixels in magenta over
///       a faded copy, their bounding box ringed) under changed/&lt;name&gt;/; compare.json; and
///       summary.md, a short table for the GitHub run summary. An empty or missing baseline is a
///       first run. Always exit 0: it reports, it never fails the job (2 for a wrong command line).
///   Mordor.UiCheck --gallery &lt;artifacts root&gt; &lt;out dir&gt;
///       every machine's pictures side by side: &lt;out dir&gt;/index.html with the pictures and their
///       thumbnails copied beside it, one folder that opens from disk. Exit 0 (2 for a wrong command line).
///
/// What is compared: the readability pictures (the PNGs at the folder's top, Light_* and Dark_*), and
/// of the playthrough only <see cref="SteadyPlaythrough"/>. The rest of the playthrough differs from
/// run to run by design - the fights' blows and damage, and the clock and play time on the character
/// window in town - so it is shown in the gallery and never compared.
/// </summary>
static class PictureTools
{
    /// <summary>The readability pass's list of pictures and their failures, beside the pictures.</summary>
    public const string ChecksFile = "checks.json";

    /// <summary>Where a machine's comparison is put in its pictures folder, for the gallery to find.</summary>
    public const string CompareFolder = "compare";

    /// <summary>
    /// The playthrough pictures that come out the same every run: the main menu it ends on. Two runs
    /// of the playthrough on one machine (2026-11-27) differed in every other picture from the
    /// Sparring Room on (the fights' dice), and in 10-town (the clock on the character window).
    /// </summary>
    public static readonly string[] SteadyPlaythrough = { "playthrough/10-main-menu.png" };

    /// <summary>One failure under a picture in checks.json. A known one (LayoutCheck.KnownFindings) carries its reason and does not fail.</summary>
    public sealed record Failure(string Kind, string Message, bool Known, string? Reason);

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>checks.json: { "pictures": [ { "file", "failures": [ { "kind", "message", "known", "reason"? } ] } ] }, in the order taken.</summary>
    public static string ChecksJson(IEnumerable<(string Picture, List<Failure> Failures)> pictures)
    {
        var list = new JsonArray();
        foreach (var (picture, failures) in pictures)
        {
            var f = new JsonArray();
            foreach (var x in failures)
            {
                var o = new JsonObject { ["kind"] = x.Kind, ["message"] = x.Message, ["known"] = x.Known };
                if (x.Reason != null) o["reason"] = x.Reason;
                f.Add(o);
            }
            list.Add(new JsonObject { ["file"] = picture, ["failures"] = f });
        }
        return new JsonObject { ["pictures"] = list }.ToJsonString(Indented);
    }

    // ================================================================ --compare

    /// <summary>The pictures a folder holds that are compared, by their path in it with '/' (Light_x.png, playthrough/10-main-menu.png).</summary>
    static SortedSet<string> Comparable(string dir)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return names;
        foreach (var f in Directory.GetFiles(dir, "*.png")) names.Add(Path.GetFileName(f));
        foreach (var p in SteadyPlaythrough)
            if (File.Exists(Path.Combine(dir, p))) names.Add(p);
        return names;
    }

    /// <summary>A PNG as 32-bit pixels, BGRA, row by row.</summary>
    sealed class Pixels
    {
        public int Width, Height;
        public uint[] Data = Array.Empty<uint>();
        public uint At(int x, int y) => x < Width && y < Height ? Data[y * Width + x] : 0;

        public static Pixels Load(string path)
        {
            using var decoded = SKBitmap.Decode(path) ?? throw new InvalidDataException($"{path} is not a picture SkiaSharp can read");
            using var bgra = decoded.Copy(SKColorType.Bgra8888) ?? throw new InvalidDataException($"{path} could not be read as 32-bit pixels");
            var p = new Pixels { Width = bgra.Width, Height = bgra.Height };
            p.Data = MemoryMarshal.Cast<byte, uint>(bgra.GetPixelSpan()).ToArray();
            return p;
        }
    }

    static void SavePng(SKBitmap bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    /// <summary>A changed picture: how many pixels differ and the box round them.</summary>
    public sealed record Change(string Name, int Pixels, int X, int Y, int Width, int Height, string SizeBefore, string SizeAfter, string Folder);

    public sealed record CompareResult(bool FirstRun, List<Change> Changed, List<string> Added, List<string> Removed, int Unchanged);

    /// <summary>Compare <paramref name="baseline"/> with <paramref name="current"/>, writing the report into <paramref name="outDir"/>.</summary>
    public static CompareResult Compare(string baseline, string current, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var before = Comparable(baseline);
        var after = Comparable(current);
        bool firstRun = before.Count == 0;
        var changed = new List<Change>();
        var added = new List<string>();
        var removed = new List<string>();
        int unchanged = 0;
        if (!firstRun)
        {
            foreach (var name in after)
            {
                if (!before.Contains(name)) { added.Add(name); continue; }
                var c = CompareOne(name, Path.Combine(baseline, name), Path.Combine(current, name), outDir);
                if (c == null) unchanged++; else changed.Add(c);
            }
            removed.AddRange(before.Where(n => !after.Contains(n)));
        }

        var json = new JsonObject
        {
            ["baseline"] = Path.GetFullPath(baseline),
            ["current"] = Path.GetFullPath(current),
            ["firstRun"] = firstRun,
            ["compared"] = firstRun ? 0 : changed.Count + unchanged,
            ["unchanged"] = unchanged,
            ["changed"] = new JsonArray(changed.Select(c => (JsonNode)new JsonObject
            {
                ["name"] = c.Name,
                ["pixels"] = c.Pixels,
                ["box"] = new JsonObject { ["x"] = c.X, ["y"] = c.Y, ["width"] = c.Width, ["height"] = c.Height },
                ["sizeBefore"] = c.SizeBefore,
                ["sizeAfter"] = c.SizeAfter,
                ["before"] = c.Folder + "/before.png",
                ["after"] = c.Folder + "/after.png",
                ["diff"] = c.Folder + "/diff.png",
            }).ToArray()),
            ["added"] = new JsonArray(added.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
            ["removed"] = new JsonArray(removed.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
        };
        File.WriteAllText(Path.Combine(outDir, "compare.json"), json.ToJsonString(Indented));

        var md = new StringBuilder();
        md.AppendLine("### Pictures against the baseline");
        md.AppendLine();
        if (firstRun)
        {
            md.AppendLine($"First run: no baseline. {after.Count} picture{(after.Count == 1 ? "" : "s")} taken, nothing compared.");
        }
        else
        {
            md.AppendLine($"{changed.Count} changed, {added.Count} new, {removed.Count} removed ({unchanged} unchanged).");
            if (changed.Count + added.Count + removed.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("| picture | what | pixels | where |");
                md.AppendLine("|---|---|---|---|");
                foreach (var c in changed)
                    md.AppendLine($"| `{c.Name}` | changed | {c.Pixels} | {c.Width}x{c.Height} at {c.X},{c.Y}{(c.SizeBefore != c.SizeAfter ? $" (size {c.SizeBefore} to {c.SizeAfter})" : "")} |");
                foreach (var a in added) md.AppendLine($"| `{a}` | new | | |");
                foreach (var r in removed) md.AppendLine($"| `{r}` | removed | | |");
            }
        }
        File.WriteAllText(Path.Combine(outDir, "summary.md"), md.ToString());
        return new CompareResult(firstRun, changed, added, removed, unchanged);
    }

    /// <summary>One picture against its baseline: null when every pixel is the same, else the change, with before, after and diff written.</summary>
    static Change? CompareOne(string name, string beforePath, string afterPath, string outDir)
    {
        var a = Pixels.Load(beforePath);
        var b = Pixels.Load(afterPath);
        int w = Math.Max(a.Width, b.Width), h = Math.Max(a.Height, b.Height);
        int count = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        var differs = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool inA = x < a.Width && y < a.Height, inB = x < b.Width && y < b.Height;
                if (inA && inB && a.At(x, y) == b.At(x, y)) continue;
                differs[y * w + x] = true;
                count++;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        if (count == 0) return null;

        string folder = "changed/" + Folder(name);
        string at = Path.Combine(outDir, folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(at);
        File.Copy(beforePath, Path.Combine(at, "before.png"), true);
        File.Copy(afterPath, Path.Combine(at, "after.png"), true);
        // the diff: the after picture (the before where the after is smaller) faded toward white, the
        // changed pixels solid magenta, and a magenta ring two pixels out round their box so a change
        // of a few pixels is seen at a glance
        using var diff = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var px = new uint[w * h];
        const uint Magenta = 0xFFFF00FF;   // BGRA little-endian: A=FF R=FF G=00 B=FF
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (differs[i]) { px[i] = Magenta; continue; }
                uint c = x < b.Width && y < b.Height ? b.At(x, y) : a.At(x, y);
                uint Fade(int shift) => (uint)(((c >> shift) & 0xFF) * 30 / 100 + 255 * 70 / 100) << shift;
                px[i] = 0xFF000000 | Fade(16) | Fade(8) | Fade(0);
            }
        for (int ring = 2; ring <= 3; ring++)
        {
            int rx0 = x0 - ring, ry0 = y0 - ring, rx1 = x1 + ring, ry1 = y1 + ring;
            for (int x = rx0; x <= rx1; x++) { Put(x, ry0); Put(x, ry1); }
            for (int y = ry0; y <= ry1; y++) { Put(rx0, y); Put(rx1, y); }
        }
        void Put(int x, int y) { if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = Magenta; }
        Marshal.Copy(MemoryMarshal.Cast<uint, int>(px).ToArray(), 0, diff.GetPixels(), px.Length);
        SavePng(diff, Path.Combine(at, "diff.png"));
        return new Change(name, count, x0, y0, x1 - x0 + 1, y1 - y0 + 1, $"{a.Width}x{a.Height}", $"{b.Width}x{b.Height}", folder);
    }

    /// <summary>A picture's own folder under changed/: its path with the folder separator and the .png gone.</summary>
    static string Folder(string name) => Path.ChangeExtension(name, null)!.Replace('/', '_').Replace('\\', '_');

    public static int CompareMain(string[] args)
    {
        if (args.Length != 4) { Console.WriteLine("usage: Mordor.UiCheck --compare <baseline dir> <current dir> <out dir>"); return 2; }
        try
        {
            var r = Compare(args[1], args[2], args[3]);
            Console.WriteLine(r.FirstRun
                ? $"first run: no baseline at {args[1]}; nothing compared"
                : $"{r.Changed.Count} changed, {r.Added.Count} new, {r.Removed.Count} removed, {r.Unchanged} unchanged");
            foreach (var c in r.Changed) Console.WriteLine($"  changed  {c.Name}: {c.Pixels} px in {c.Width}x{c.Height} at {c.X},{c.Y}");
            foreach (var a in r.Added) Console.WriteLine($"  new      {a}");
            foreach (var x in r.Removed) Console.WriteLine($"  removed  {x}");
            Console.WriteLine($"the report is in {Path.GetFullPath(args[3])} (compare.json, summary.md)");
        }
        catch (Exception ex)
        {
            // a comparison that cannot be made is said, not failed: the job's pass or fail is the checks'
            Console.WriteLine($"the comparison could not be made: {ex.GetType().Name}: {ex.Message}");
            try
            {
                Directory.CreateDirectory(args[3]);
                File.WriteAllText(Path.Combine(args[3], "summary.md"), $"### Pictures against the baseline\n\nThe comparison could not be made: {ex.Message}\n");
            }
            catch { }
        }
        return 0;
    }

    // ================================================================ --gallery

    /// <summary>The machines in the order of the gallery's columns: the artifact's folder name, and the column's heading.</summary>
    static readonly (string Folder, string Label)[] Machines =
    {
        ("screenshots-Windows-X64", "Windows x64"),
        ("screenshots-Windows-ARM64", "Windows ARM64"),
        ("screenshots-Linux-X64", "Linux x64"),
        ("screenshots-Linux-ARM64", "Linux ARM64"),
        ("screenshots-macOS-ARM64", "macOS ARM64"),
    };

    /// <summary>How wide a thumbnail is drawn, in pixels; the column fits it.</summary>
    const int ThumbWidth = 320;

    sealed class Machine
    {
        public string Folder = "", Label = "", Dir = "";
        public bool Present, FirstRun, HasCompare;
        public HashSet<string> Pictures = new(StringComparer.Ordinal);
        public Dictionary<string, List<Failure>> Failures = new(StringComparer.Ordinal);
        public List<string> Order = new();
        public Dictionary<string, (int Pixels, string Before, string After, string Diff, string Box)> Changed = new(StringComparer.Ordinal);
        public int ChangedCells, FailingCells;
    }

    static Machine Read(string root, string folder, string label)
    {
        var m = new Machine { Folder = folder, Label = label, Dir = Path.Combine(root, folder) };
        m.Present = Directory.Exists(m.Dir);
        if (!m.Present) return m;
        foreach (var f in Directory.GetFiles(m.Dir, "*.png")) m.Pictures.Add(Path.GetFileName(f));
        string play = Path.Combine(m.Dir, "playthrough");
        if (Directory.Exists(play))
            foreach (var f in Directory.GetFiles(play, "*.png")) m.Pictures.Add("playthrough/" + Path.GetFileName(f));
        string checks = Path.Combine(m.Dir, ChecksFile);
        if (File.Exists(checks))
        {
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(checks));
                foreach (var p in doc?["pictures"]?.AsArray() ?? new JsonArray())
                {
                    string file = p?["file"]?.GetValue<string>() ?? "";
                    if (file.Length == 0) continue;
                    m.Order.Add(file);
                    var list = new List<Failure>();
                    foreach (var f in p!["failures"]?.AsArray() ?? new JsonArray())
                        list.Add(new Failure(f?["kind"]?.GetValue<string>() ?? "", f?["message"]?.GetValue<string>() ?? "",
                                             f?["known"]?.GetValue<bool>() ?? false, f?["reason"]?.GetValue<string>()));
                    m.Failures[file] = list;
                }
            }
            catch (Exception ex) { Console.WriteLine($"  note  {checks} could not be read: {ex.Message}"); }
        }
        string compare = Path.Combine(m.Dir, CompareFolder, "compare.json");
        if (File.Exists(compare))
        {
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(compare));
                m.HasCompare = true;
                m.FirstRun = doc?["firstRun"]?.GetValue<bool>() ?? false;
                foreach (var c in doc?["changed"]?.AsArray() ?? new JsonArray())
                {
                    string name = c?["name"]?.GetValue<string>() ?? "";
                    var box = c?["box"];
                    m.Changed[name] = (c?["pixels"]?.GetValue<int>() ?? 0,
                                       c?["before"]?.GetValue<string>() ?? "", c?["after"]?.GetValue<string>() ?? "", c?["diff"]?.GetValue<string>() ?? "",
                                       box == null ? "" : $"{box["width"]}x{box["height"]} at {box["x"]},{box["y"]}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"  note  {compare} could not be read: {ex.Message}"); }
        }
        return m;
    }

    /// <summary>The rows: each readability picture, its Light and Dark beside each other in the order the pass took them, then the playthrough's steps.</summary>
    static List<string> Rows(List<Machine> machines)
    {
        var all = new HashSet<string>(machines.SelectMany(m => m.Pictures.Concat(m.Failures.Keys)), StringComparer.Ordinal);
        string Screen(string n) => n.StartsWith("Light_", StringComparison.Ordinal) ? n[6..] : n.StartsWith("Dark_", StringComparison.Ordinal) ? n[5..] : n;
        int Theme(string n) => n.StartsWith("Light_", StringComparison.Ordinal) ? 0 : n.StartsWith("Dark_", StringComparison.Ordinal) ? 1 : 2;
        // the screens in the order first taken, by the first machine's checks.json that lists each
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in machines)
            foreach (var n in m.Order)
                if (!order.ContainsKey(Screen(n))) order[Screen(n)] = order.Count;
        var readability = all.Where(n => !n.StartsWith("playthrough/", StringComparison.Ordinal))
            .OrderBy(n => order.TryGetValue(Screen(n), out int i) ? i : int.MaxValue)
            .ThenBy(n => Screen(n), StringComparer.Ordinal).ThenBy(Theme).ToList();
        var play = all.Where(n => n.StartsWith("playthrough/", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal);
        return readability.Concat(play).ToList();
    }

    /// <summary>A row's heading: the theme, and the window's name as the file has it, spaced again.</summary>
    static string Title(string name)
    {
        string n = Path.ChangeExtension(name, null)!;
        if (n.StartsWith("playthrough/", StringComparison.Ordinal)) return "playthrough: " + n[12..].Replace('-', ' ');
        string theme = n.StartsWith("Light_", StringComparison.Ordinal) ? "Light" : n.StartsWith("Dark_", StringComparison.Ordinal) ? "Dark" : "";
        if (theme.Length > 0) n = n[(theme.Length + 1)..];
        n = System.Text.RegularExpressions.Regex.Replace(n.Replace("__", ", ").Replace("_s_", "'s ").Replace('_', ' '), " {2,}", " ").Trim();
        return theme.Length > 0 ? $"{theme}: {n}" : n;
    }

    static string Url(params string[] parts) => string.Join("/", parts.SelectMany(p => p.Split('/', '\\')).Where(p => p.Length > 0).Select(Uri.EscapeDataString));
    static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>A thumbnail <see cref="ThumbWidth"/> wide (or the picture itself where it is narrower).</summary>
    static void Thumb(string from, string to)
    {
        using var src = SKBitmap.Decode(from);
        if (src == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (src.Width <= ThumbWidth) { File.Copy(from, to, true); return; }
        int h = Math.Max(1, (int)Math.Round(src.Height * (double)ThumbWidth / src.Width));
        using var small = src.Resize(new SKImageInfo(ThumbWidth, h), SKFilterQuality.High);
        if (small != null) SavePng(small, to); else File.Copy(from, to, true);
    }

    public static (int Rows, int Columns) Gallery(string root, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var machines = Machines.Select(m => Read(root, m.Folder, m.Label)).ToList();
        // any other machine's folder, after the five
        if (Directory.Exists(root))
            foreach (var d in Directory.GetDirectories(root, "screenshots-*").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal))
                if (!Machines.Any(m => m.Folder == d)) machines.Add(Read(root, d!, d!["screenshots-".Length..].Replace('-', ' ')));
        var rows = Rows(machines);

        var sb = new StringBuilder();
        var body = new StringBuilder();
        int r = 0;
        foreach (var name in rows)
        {
            var cells = new StringBuilder();
            bool rowChanged = false, rowFailing = false;
            foreach (var m in machines)
            {
                cells.Append("<td>");
                bool has = m.Pictures.Contains(name);
                var failures = m.Failures.TryGetValue(name, out var f) ? f : new List<Failure>();
                bool failing = failures.Any(x => !x.Known);
                bool changed = m.Changed.TryGetValue(name, out var ch);
                var cls = new List<string> { "cell" };
                if (changed) cls.Add("changed");
                if (failing) cls.Add("failing");
                cells.Append($"<div class=\"{string.Join(" ", cls)}\">");
                if (has)
                {
                    string full = Url("pictures", m.Folder, name), thumb = Url("thumbs", m.Folder, name);
                    Copy(Path.Combine(m.Dir, name), Path.Combine(outDir, "pictures", m.Folder, name));
                    Thumb(Path.Combine(m.Dir, name), Path.Combine(outDir, "thumbs", m.Folder, name));
                    cells.Append($"<a class=\"shot\" href=\"{full}\" data-full=\"{full}\" data-title=\"{H(m.Label + " - " + Title(name))}\"><img loading=\"lazy\" src=\"{thumb}\" alt=\"{H(Title(name))} on {H(m.Label)}\"></a>");
                }
                else cells.Append("<div class=\"none\">no picture</div>");
                if (changed)
                {
                    rowChanged = true; m.ChangedCells++;
                    string Cmp(string rel) => Url("pictures", m.Folder, CompareFolder, rel);
                    foreach (var rel in new[] { ch.Before, ch.After, ch.Diff })
                        if (rel.Length > 0) Copy(Path.Combine(m.Dir, CompareFolder, rel), Path.Combine(outDir, "pictures", m.Folder, CompareFolder, rel));
                    string t = H(m.Label + " - " + Title(name));
                    cells.Append($"<div class=\"tag\"><span class=\"badge\">changed</span> {ch.Pixels} px, {H(ch.Box)}: "
                        + $"<a href=\"{Cmp(ch.Before)}\" data-full=\"{Cmp(ch.Before)}\" data-title=\"{t} (before)\">before</a> "
                        + $"<a href=\"{Cmp(ch.After)}\" data-full=\"{Cmp(ch.After)}\" data-title=\"{t} (after)\">after</a> "
                        + $"<a href=\"{Cmp(ch.Diff)}\" data-full=\"{Cmp(ch.Diff)}\" data-title=\"{t} (diff)\">diff</a></div>");
                }
                if (failing) { rowFailing = true; m.FailingCells++; }
                if (failures.Count > 0)
                {
                    cells.Append("<ul class=\"failures\">");
                    foreach (var x in failures)
                        cells.Append(x.Known
                            ? $"<li class=\"known\"><b>known {H(x.Kind)}</b>: {H(x.Message)}{(x.Reason != null ? $" <i>({H(x.Reason)})</i>" : "")}</li>"
                            : $"<li><b>{H(x.Kind)}</b>: {H(x.Message)}</li>");
                    cells.Append("</ul>");
                }
                cells.Append("</div></td>");
            }
            string flags = (rowChanged ? " changed" : "") + (rowFailing ? " failing" : "");
            body.Append($"<tr data-row=\"{r++}\" class=\"row{flags}\"><th scope=\"row\">{H(Title(name))}<span class=\"file\">{H(name)}</span></th>{cells}</tr>\n");
        }

        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>OpenDejenol pictures</title>\n<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n");
        sb.Append("<header>\n<h1>OpenDejenol pictures</h1>\n<p class=\"lede\">Every window of the readability pass, Light and Dark, and the playthrough's steps, as each machine drew them. ");
        sb.Append("A magenta border is a picture changed since the baseline run; a red one has a check failing under it.</p>\n<ul class=\"counts\">\n");
        foreach (var m in machines)
        {
            string state = !m.Present ? "no pictures"
                : $"{m.ChangedCells} changed, {m.FailingCells} failing" + (m.HasCompare ? (m.FirstRun ? " (first run: no baseline)" : "") : " (not compared)");
            sb.Append($"<li data-machine=\"{H(m.Folder)}\"><b>{H(m.Label)}</b>: {H(state)}</li>\n");
        }
        sb.Append("</ul>\n<label class=\"filter\"><input type=\"checkbox\" id=\"only\"> Only rows that changed or fail</label>\n</header>\n");
        sb.Append("<div class=\"grid\">\n<table>\n<thead><tr><th scope=\"col\" class=\"corner\">screen</th>");
        foreach (var m in machines) sb.Append($"<th scope=\"col\" class=\"machine\" data-machine=\"{H(m.Folder)}\">{H(m.Label)}</th>");
        sb.Append("</tr></thead>\n<tbody>\n").Append(body).Append("</tbody>\n</table>\n</div>\n");
        sb.Append("<p class=\"empty\" id=\"nothing\" hidden>No row changed or fails.</p>\n");
        sb.Append("<div id=\"overlay\" hidden><div class=\"bar\"><span id=\"overlay-title\"></span><button type=\"button\" id=\"close\">Close</button></div><div class=\"view\"><img id=\"overlay-img\" alt=\"\"></div></div>\n");
        sb.Append("<script>\n").Append(Script).Append("</script>\n</body>\n</html>\n");
        File.WriteAllText(Path.Combine(outDir, "index.html"), sb.ToString());
        return (rows.Count, machines.Count);
    }

    static void Copy(string from, string to)
    {
        if (!File.Exists(from)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, true);
    }

    const string Css = """
:root { color-scheme: light dark; --bg: #f6f6f4; --fg: #1d1d1f; --muted: #5d5d66; --panel: #ffffff; --line: #d4d4d8;
        --changed: #b0129c; --failing: #c62828; --known: #6b6b73; --badge-fg: #ffffff; --link: #1f5fbf; }
@media (prefers-color-scheme: dark) {
  :root { --bg: #161618; --fg: #ececef; --muted: #a3a3ad; --panel: #222226; --line: #3a3a40;
          --changed: #e45bd2; --failing: #ff6b6b; --known: #9a9aa3; --badge-fg: #161618; --link: #8ab4ff; }
}
* { box-sizing: border-box; }
html { -webkit-text-size-adjust: 100%; }
body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 14px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; }
h1 { font-size: 1.35rem; margin: 0 0 4px; }
.lede { margin: 0 0 8px; color: var(--muted); max-width: 70ch; }
.counts { list-style: none; padding: 0; margin: 0 0 8px; display: flex; flex-wrap: wrap; gap: 4px 16px; }
.filter { display: inline-flex; gap: 6px; align-items: center; margin: 4px 0 12px; cursor: pointer; }
a { color: var(--link); }
.grid { overflow-x: auto; border: 1px solid var(--line); border-radius: 6px; background: var(--panel); }
table { border-collapse: collapse; }
th, td { border-bottom: 1px solid var(--line); vertical-align: top; text-align: left; padding: 8px; }
thead th { position: sticky; top: 0; background: var(--panel); z-index: 1; }
th[scope=row] { min-width: 160px; max-width: 220px; font-weight: 600; position: sticky; left: 0; background: var(--panel); z-index: 1; }
.file { display: block; font-weight: 400; color: var(--muted); font-size: 12px; word-break: break-all; }
td { min-width: 236px; width: 236px; }
.cell { border: 3px solid transparent; border-radius: 4px; padding: 2px; }
.cell.changed { border-color: var(--changed); }
.cell.failing { border-color: var(--failing); }
.shot { display: block; }
.shot img { display: block; width: 100%; height: auto; border: 1px solid var(--line); }
.none { color: var(--muted); font-style: italic; padding: 24px 0; text-align: center; }
.tag { margin-top: 4px; font-size: 12px; }
.badge { background: var(--changed); color: var(--badge-fg); border-radius: 3px; padding: 0 5px; font-weight: 600; }
.failures { margin: 4px 0 0; padding-left: 16px; font-size: 12px; color: var(--failing); word-break: break-word; }
.failures .known { color: var(--known); }
.only .row:not(.changed):not(.failing) { display: none; }
#overlay { position: fixed; inset: 0; background: rgba(0,0,0,.85); z-index: 10; display: flex; flex-direction: column; }
#overlay[hidden] { display: none; }
#overlay .bar { display: flex; justify-content: space-between; align-items: center; gap: 8px; padding: 8px 16px; color: #fff; }
#overlay button { font: inherit; padding: 4px 12px; cursor: pointer; }
#overlay .view { flex: 1; overflow: auto; padding: 0 16px 16px; }
#overlay img { display: block; max-width: none; margin: 0 auto; background: #fff; }
.empty { color: var(--muted); }
""";

    const string Script = """
(function () {
  var overlay = document.getElementById('overlay'), img = document.getElementById('overlay-img'),
      title = document.getElementById('overlay-title');
  function close() { overlay.hidden = true; img.removeAttribute('src'); }
  document.addEventListener('click', function (e) {
    var a = e.target.closest && e.target.closest('a[data-full]');
    if (!a) return;
    e.preventDefault();
    img.src = a.getAttribute('data-full');
    img.alt = a.getAttribute('data-title') || '';
    title.textContent = a.getAttribute('data-title') || '';
    overlay.hidden = false;
  });
  document.getElementById('close').addEventListener('click', close);
  overlay.addEventListener('click', function (e) { if (e.target === overlay || e.target.classList.contains('view')) close(); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
  var only = document.getElementById('only'), nothing = document.getElementById('nothing');
  only.addEventListener('change', function () {
    document.body.classList.toggle('only', only.checked);
    nothing.hidden = !(only.checked && !document.querySelector('.row.changed, .row.failing'));
  });
})();
""";

    public static int GalleryMain(string[] args)
    {
        if (args.Length != 3) { Console.WriteLine("usage: Mordor.UiCheck --gallery <artifacts root> <out dir>"); return 2; }
        try
        {
            var (rows, cols) = Gallery(args[1], args[2]);
            Console.WriteLine($"the gallery: {rows} rows, {cols} machines, at {Path.GetFullPath(Path.Combine(args[2], "index.html"))}");
        }
        catch (Exception ex) { Console.WriteLine($"the gallery could not be made: {ex.GetType().Name}: {ex.Message}"); }
        return 0;
    }

    // ================================================================ the checks of the tools

    /// <summary>
    /// --compare and --gallery on pictures made here: a folder against itself (nothing changed), against
    /// a copy with one pixel altered, one picture added and one taken away, and against no baseline;
    /// then a gallery of two machines. Returns how many failed.
    /// </summary>
    public static int SelfTest()
    {
        Console.WriteLine("\nthe CI picture tools: --compare and --gallery on pictures made here");
        int failed = 0;
        void Check(string what, object? got, object? want)
        {
            bool ok = Equals(got, want);
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what}{(ok ? "" : $": got {got}, want {want}")}");
            if (!ok) failed++;
        }
        string scratch = Path.Combine(Path.GetTempPath(), "mordor-uicheck-pictures-" + Guid.NewGuid().ToString("N"));
        try
        {
            void Png(string path, int w, int h, uint colour, (int X, int Y)? dot = null)
            {
                using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                bmp.Erase(new SKColor(colour));
                if (dot is { } d) bmp.SetPixel(d.X, d.Y, SKColors.Black);
                SavePng(bmp, path);
            }
            string a = Path.Combine(scratch, "a"), b = Path.Combine(scratch, "b");
            Png(Path.Combine(a, "Light_one.png"), 40, 30, 0xFFC0C0C0);
            Png(Path.Combine(a, "Dark_one.png"), 40, 30, 0xFF202020);
            Png(Path.Combine(a, "Light_gone.png"), 10, 10, 0xFFFFFFFF);
            Png(Path.Combine(a, "playthrough", "05-sparring-room.png"), 10, 10, 0xFF00FF00);
            Png(Path.Combine(a, "playthrough", "10-main-menu.png"), 10, 10, 0xFF0000FF);

            var same = Compare(a, a, Path.Combine(scratch, "same"));
            Check("--compare, a folder with itself: nothing changed, new or removed", (same.FirstRun, same.Changed.Count, same.Added.Count, same.Removed.Count, same.Unchanged), (false, 0, 0, 0, 4));

            Png(Path.Combine(b, "Light_one.png"), 40, 30, 0xFFC0C0C0, (7, 5));
            Png(Path.Combine(b, "Dark_one.png"), 40, 30, 0xFF202020);
            Png(Path.Combine(b, "Light_new.png"), 10, 10, 0xFFFFFFFF);
            Png(Path.Combine(b, "playthrough", "05-sparring-room.png"), 10, 10, 0xFFFF0000);
            Png(Path.Combine(b, "playthrough", "10-main-menu.png"), 10, 10, 0xFF0000FF);
            string outB = Path.Combine(scratch, "b-compare");
            var one = Compare(a, b, outB);
            Check("--compare, one pixel altered: one changed", one.Changed.Select(c => $"{c.Name} {c.Pixels} px at {c.X},{c.Y} {c.Width}x{c.Height}").FirstOrDefault(), "Light_one.png 1 px at 7,5 1x1");
            Check("--compare: and only the one", one.Changed.Count, 1);
            Check("--compare: one new, one removed", (string.Join(",", one.Added), string.Join(",", one.Removed)), ("Light_new.png", "Light_gone.png"));
            Check("--compare: the playthrough's fights are not compared", one.Changed.Any(c => c.Name.Contains("sparring")), false);
            Check("--compare: before, after and diff written", new[] { "before", "after", "diff" }.All(n => File.Exists(Path.Combine(outB, "changed", "Light_one", n + ".png"))), true);
            var diff = Pixels.Load(Path.Combine(outB, "changed", "Light_one", "diff.png"));
            Check("--compare: the diff marks the changed pixel", diff.At(7, 5), 0xFFFF00FFu);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(outB, "compare.json")))!;
            Check("--compare: compare.json says it", (json["changed"]!.AsArray().Count, json["added"]!.AsArray().Count, json["removed"]!.AsArray().Count, json["firstRun"]!.GetValue<bool>()), (1, 1, 1, false));
            Check("--compare: summary.md counts it", File.ReadAllText(Path.Combine(outB, "summary.md")).Contains("1 changed, 1 new, 1 removed"), true);
            var first = Compare(Path.Combine(scratch, "nowhere"), b, Path.Combine(scratch, "first"));
            Check("--compare, no baseline: a first run, not a failure", (first.FirstRun, first.Changed.Count, File.ReadAllText(Path.Combine(scratch, "first", "summary.md")).Contains("First run")), (true, 0, true));
            // the command itself, its report kept off this run's output
            var said = Console.Out;
            int code;
            try { Console.SetOut(TextWriter.Null); code = CompareMain(new[] { "--compare", a, b, Path.Combine(scratch, "cmd") }); }
            finally { Console.SetOut(said); }
            Check("--compare: the command exits 0 whatever it finds, and writes its report", (code, File.Exists(Path.Combine(scratch, "cmd", "compare.json"))), (0, true));

            // the gallery: two machines, one with a check failing and a picture changed
            string arts = Path.Combine(scratch, "artifacts");
            string win = Path.Combine(arts, "screenshots-Windows-X64"), lin = Path.Combine(arts, "screenshots-Linux-ARM64");
            foreach (var f in Directory.GetFiles(b, "*.png", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(b, f);
                Copy(f, Path.Combine(win, rel));
                if (!rel.Contains("new")) Copy(f, Path.Combine(lin, rel));
            }
            File.WriteAllText(Path.Combine(win, ChecksFile), ChecksJson(new[]
            {
                ("Light_one.png", new List<Failure> { new("clipped", "TextBlock \"A caption\": needs 64 px wide, has 60", false, null) }),
                ("Dark_one.png", new List<Failure> { new("overflow", "Border: 3 px past the right", true, "the original's form is as wide") }),
                ("Light_new.png", new List<Failure>()),
            }));
            Compare(a, win, Path.Combine(win, CompareFolder));
            string gal = Path.Combine(scratch, "gallery");
            var (rows, cols) = Gallery(arts, gal);
            string html = File.ReadAllText(Path.Combine(gal, "index.html"));
            int Count(string s) { int n = 0, i = 0; while ((i = html.IndexOf(s, i, StringComparison.Ordinal)) >= 0) { n++; i += s.Length; } return n; }
            Check("--gallery: a row for each picture (3 readability, 2 playthrough) and a column for each machine", (rows, cols, Count("<tr data-row="), Count("class=\"machine\"")), (5, 5, 5, 5));
            Check("--gallery: Light and Dark of a screen side by side, the playthrough after",
                  string.Join(",", System.Text.RegularExpressions.Regex.Matches(html, "<span class=\"file\">([^<]*)</span>").Select(m => m.Groups[1].Value)),
                  "Light_one.png,Dark_one.png,Light_new.png,playthrough/05-sparring-room.png,playthrough/10-main-menu.png");
            Check("--gallery: the changed cell is marked and linked", (Count("<span class=\"badge\">changed</span>"), html.Contains("compare/changed/Light_one/diff.png")), (1, true));
            Check("--gallery: the failing cell is marked red, the known finding is not", (Count("class=\"cell failing\"") + Count("class=\"cell changed failing\""), Count("class=\"known\"")), (1, 1));
            Check("--gallery: missing machines and pictures are empty cells", Count("<div class=\"none\">no picture</div>"), 5 * 5 - 5 - 4);
            Check("--gallery: the pictures and thumbnails copied beside it",
                  File.Exists(Path.Combine(gal, "pictures", "screenshots-Linux-ARM64", "playthrough", "10-main-menu.png")) && File.Exists(Path.Combine(gal, "thumbs", "screenshots-Windows-X64", "Light_one.png")), true);
            Check("--gallery: nothing loaded from elsewhere", System.Text.RegularExpressions.Regex.IsMatch(html, "(src|href)=\"(https?:)?//"), false);
        }
        catch (Exception ex) { Check($"the picture tools ran: {ex.GetType().Name}: {ex.Message}", false, true); }
        finally
        {
            if (new DirectoryInfo(scratch).Parent?.FullName == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                try { Directory.Delete(scratch, true); } catch { }
        }
        return failed;
    }
}
