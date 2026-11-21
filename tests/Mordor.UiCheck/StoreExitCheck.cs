using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mordor.App;
using Mordor.Core;
using Mordor.Modding;

/// <summary>
/// The General Store's EXIT, after each way a player can leave the keyboard focus somewhere in the
/// form: a crash report showed Avalonia throwing while it took the Store's page out of the tree
/// (Visual.OnDetachedFromVisualTreeCore, ArgumentOutOfRangeException, from CloseService). Each
/// scenario opens a fresh town and Store, sets the focus up, clicks EXIT with the mouse, and on a
/// throw says which control in the page lost a child while it was being taken out.
/// </summary>
static class StoreExitCheck
{
    public static int Run(string game)
    {
        Console.WriteLine("\nGeneral Store: EXIT after each focus a player can leave in the form:");
        int failed = 0;
        void Pump()
        {
            for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        }
        Mdata1.LoadFrom(game); RaceData.LoadFrom(game); GuildData.LoadFrom(game);
        var items = MdrLoader.LoadItems(Path.Combine(game, "MDATA3.MDR"));

        // Each scenario leaves the keyboard somewhere in the form, then EXIT is clicked (or, where
        // CloseTown, the whole town window closed). The buy list is a DataGrid: a row that has the
        // keyboard and is scrolled out of view is held aside by the grid, and unloaded when the grid
        // loses the keyboard - which, unguarded, happened half way through taking the page out.
        var scenarios = new List<(string Name, Action<Ctx> Setup, bool CloseTown)>
        {
            ("nothing focused", _ => { }, false),
            ("search box clicked, a letter typed", c => { c.Click(c.Boxes[^1]); c.Town.KeyTextInput("b"); c.Pump(); }, false),
            ("each box clicked in turn", c => { foreach (var b in c.Boxes) c.Click(b); }, false),
            ("search typed and Enter", c => { c.Click(c.Boxes[^1]); c.Town.KeyTextInput("a"); c.Pump(); c.Key(Key.Enter); }, false),
            ("Tab x3", c => { for (int i = 0; i < 3; i++) c.Key(Key.Tab); }, false),
            ("Tab x8", c => { for (int i = 0; i < 8; i++) c.Key(Key.Tab); }, false),
            ("row clicked, alignment picked (no scroll)", c => { c.ClickGridRow(0); c.Pick(); }, false),
            // the crash.log's: the row's alignment picked so EXIT is back, then the row scrolled away
            ("row clicked, alignment picked, scrolled to the end", c => { c.ClickGridRow(0); c.Pick(); c.GridScrollTo(last: true); }, false),
            ("row clicked, alignment picked, wheel down x20", c => { c.ClickGridRow(0); c.Pick(); c.Wheel(-20); }, false),
            ("end scrolled to, last row clicked, picked, back to the top", c => { c.GridScrollTo(last: true); c.ClickGridRow(-1); c.Pick(); c.GridScrollTo(last: false); }, false),
            ("rows take keys (QoL): row clicked, picked, scrolled to the end", c => { c.KeysTakeRows(); c.ClickGridRow(0); c.Pick(); c.GridScrollTo(last: true); }, false),
            ("rows take keys (QoL): row clicked, Down x40, picked, wheel up x40", c => { c.KeysTakeRows(); c.ClickGridRow(0); for (int i = 0; i < 40; i++) c.Key(Key.Down); c.Pick(); c.Wheel(40); }, false),
            ("the town closed with the row held aside", c => { c.ClickGridRow(0); c.Pick(); c.GridScrollTo(last: true); }, true),
        };

        foreach (var (name, setup, closeTown) in scenarios)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "mordor-uicheck-storeexit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            string? savesWas = Environment.GetEnvironmentVariable(Saves.EnvDir);
            var hostWas = ModHost.Active;
            CityWindow? town = null;
            try
            {
                Environment.SetEnvironmentVariable(Saves.EnvDir, scratch);
                ModHost.Active = new ModHost(new ModSettingsStore(Path.Combine(scratch, "settings.ini")), _ => { });
                var hero = new Character
                {
                    Name = "Exitcheck", Race = RaceData.All[0].Name, CurHits = 100, MaxHits = 100, GoldOnHand = 100_000_000,
                    Guilds = { new GuildMembership { Guild = GuildData.All[0].Name, Level = 20, Experience = 2_000_000 } },
                };
                int slot = 1;
                foreach (var it in items.Where(i => i.Id > 0 && i.Cursed == 0 && !ItemUse.IsGuildIssued(i)).Take(6))
                    hero.Pack[slot++] = new InventorySlot { ItemId = it.Id, Known = IdLevel.All };
                hero.Save(Path.Combine(scratch, Character.SafeFileName(hero.Name) + ".json"));
                // a full Store: every item the shop sells, several of each alignment, so the list scrolls
                var stock = StoreStock.CreateInitial(items);
                foreach (var it in items.Where(i => i.Id > 0).Take(120))
                    foreach (var a in ItemAlignments.All) { stock.Add(it.Id, a); stock.Add(it.Id, a); }
                stock.Save(scratch);
                town = new CityWindow(new Party(hero), game) { Width = 1400, Height = 1000 };
                town.Show(); Pump();
                typeof(CityWindow).GetMethod("ShowStore", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(town, new object?[] { null, new RoutedEventArgs() });
                Pump();
                var page = (ContentControl)typeof(CityWindow).GetProperty("ServicePage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(town)!;
                var c = new Ctx(town, page, Pump);
                setup(c);
                Pump();
                var focused = town.FocusManager?.GetFocusedElement();
                int rows = c.GridRows;
                // every control in the page, with how many children it has, before EXIT
                var before = page.GetSelfAndVisualDescendants().ToDictionary(v => v, v => v.GetVisualChildren().Count());
                Exception? thrown = null;
                try { if (closeTown) town.Close(); else c.ClickButton("XIT"); Pump(); }
                catch (Exception ex) { thrown = ex; }
                string where = "";
                if (thrown != null)
                    where = string.Join("; ", before.Where(kv => kv.Key.GetVisualChildren().Count() != kv.Value)
                        .Select(kv => $"{PathOf(kv.Key)} had {kv.Value} children, now {kv.Key.GetVisualChildren().Count()}"));
                // and EXIT did its job: the Store is gone from the service window
                bool closed = closeTown || page.Content == null;
                bool ok = thrown == null && closed;
                if (thrown == null && !closed) where = "EXIT left the Store up";
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {name} (focus on {Describe(focused)}, {rows} rows)"
                    + (ok ? "" : $": {thrown!.GetType().Name}: {thrown.Message}\n          shrank: {where}"));
                if (!ok) failed++;
            }
            catch (Exception ex) { Console.WriteLine($"  FAIL  {name}: the scenario could not be driven: {ex.GetType().Name}: {ex.Message}"); failed++; }
            finally
            {
                try { town?.Close(); Pump(); } catch { }
                ModHost.Active = hostWas;
                Environment.SetEnvironmentVariable(Saves.EnvDir, savesWas);
                if (new DirectoryInfo(scratch).Parent?.FullName != System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())))
                    throw new InvalidOperationException("Refusing cleanup outside the store exit test folder.");
                try { Directory.Delete(scratch, true); } catch { }
            }
        }
        return failed;
    }

    private static string Describe(IInputElement? e) => e switch
    {
        null => "nothing",
        Control c => c.GetType().Name + (string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name) + (c is TextBox t ? $" \"{t.Text}\"" : ""),
        _ => e.GetType().Name,
    };

    private static string PathOf(Visual v)
    {
        var chain = new List<string>();
        for (Visual? x = v; x != null && chain.Count < 6; x = x.GetVisualParent())
            chain.Add(x.GetType().Name + (x is Control { Name: { Length: > 0 } n } ? "#" + n : ""));
        return string.Join(" < ", chain);
    }

    private sealed class Ctx
    {
        public readonly Window Town;
        public readonly ContentControl Page;
        public readonly Action Pump;
        public readonly List<TextBox> Boxes;
        public readonly ListBox? List;
        public Ctx(Window town, ContentControl page, Action pump)
        {
            Town = town; Page = page; Pump = pump;
            Boxes = page.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible)
                .OrderBy(t => t.TranslatePoint(default, town)!.Value.Y).ThenBy(t => t.TranslatePoint(default, town)!.Value.X).ToList();
            var lists = page.GetVisualDescendants().OfType<ListBox>().ToList();
            List = lists.OrderByDescending(l => l.ItemCount).FirstOrDefault();
        }
        public int Rows => List?.ItemCount ?? 0;
        public DataGrid? Grid => Page.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault();
        public int GridRows => Grid?.ItemsSource is System.Collections.IEnumerable e ? e.Cast<object>().Count() : 0;
        /// <summary>The alignment choice, when it is up: the first of U, G, N, E that can be clicked.</summary>
        public void Pick()
        {
            string Caption(Button x) => string.Join("", x.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).Replace("_", "");
            var b = Page.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.IsEffectivelyVisible && x.IsEffectivelyEnabled
                && Caption(x) is "U" or "G" or "N" or "E" && x.Bounds.Width > 0
                && x.GetVisualAncestors().OfType<DataGrid>().Any() == false);
            if (b != null) Click(b);
        }
        /// <summary>The grid takes the keyboard, as the QoL mod's UnlockedStoreAlignment setting makes it.</summary>
        public void KeysTakeRows() { if (Grid is { } g) { g.Focusable = true; g.IsTabStop = true; } }
        public void ClickGridRow(int index)
        {
            var rows = Grid?.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsEffectivelyVisible && r.Bounds.Height > 0)
                .OrderBy(r => r.TranslatePoint(default, Town)!.Value.Y).ToList();
            if (rows is not { Count: > 0 }) return;
            var row = index < 0 ? rows[^1] : rows[Math.Min(index, rows.Count - 1)];
            var cell = row.GetVisualDescendants().OfType<DataGridCell>().FirstOrDefault() ?? (Control)row;
            Click(cell);
        }
        public void GridScrollTo(bool last)
        {
            if (Grid?.ItemsSource is not System.Collections.IEnumerable e) return;
            var all = e.Cast<object>().ToList();
            if (all.Count == 0) return;
            Grid.ScrollIntoView(last ? all[^1] : all[0], null);
            Pump();
        }
        public void Wheel(int notches)
        {
            if (Grid is null) return;
            var p = Mid(Grid);
            for (int i = 0; i < Math.Abs(notches); i++) Town.MouseWheel(p, new Vector(0, Math.Sign(notches)));
            Pump();
        }
        Point Mid(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Town)!.Value;
        public void Click(Control control)
        {
            var p = Mid(control);
            Town.MouseDown(p, MouseButton.Left); Town.MouseUp(p, MouseButton.Left); Pump();
        }
        public void ClickButton(string text)
        {
            string Caption(Button x) => string.Join("", x.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)).Replace("_", "");
            var all = Page.GetVisualDescendants().OfType<Button>().ToList();
            var b = all.FirstOrDefault(x => x.IsEffectivelyVisible && x.IsEffectivelyEnabled
                && Caption(x).Contains(text, StringComparison.OrdinalIgnoreCase));
            if (b == null)
                throw new InvalidOperationException($"no enabled \"{text}\" button; there are: "
                    + string.Join(", ", all.Select(x => $"[{Caption(x)}{(x.IsEffectivelyEnabled ? "" : " disabled")}{(x.IsEffectivelyVisible ? "" : " hidden")}]")));
            Click(b);
        }
        public void ClickRow(int index)
        {
            if (List == null) return;
            var rows = List.GetVisualDescendants().OfType<ListBoxItem>().Where(r => r.IsEffectivelyVisible).ToList();
            if (rows.Count == 0) return;
            var row = index < 0 ? rows.OrderBy(r => r.TranslatePoint(default, Town)!.Value.Y).Last() : rows[Math.Min(index, rows.Count - 1)];
            Click(row);
        }
        public void ScrollList(double y)
        {
            var sv = List?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null) return;
            sv.Offset = new Vector(sv.Offset.X, Math.Min(y, Math.Max(0, sv.Extent.Height - sv.Viewport.Height)));
            Pump();
        }
        public void Key(Key key)
        {
            Town.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Town.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Pump();
        }
    }
}
