using Mordor.Core;

public static partial class SelfCheckProbe
{
    /// <summary>
    /// Issue #14 (2026-11-27): the Hands can be equipped. sub0F74 leaves on an empty slot with
    /// <c>If Pack(p).Ref &lt; 1 And p &gt; 0</c> (MAINMOD2.BAS:1725), so slot 0, whose Ref of 0 is
    /// Items(0), the Hands, goes on through the class check, fn11B6 and the stat checks like any
    /// weapon, and its equip fills a weapon hand, M5997 (1842-1848). The swing loop then has a
    /// weapon (MONSMOD.BAS:325-338): two swings, at the Hands' damage. A new character has both
    /// weapon hands at -1 (CHARCREA.BAS:245-246), so without the Hands on it is "Using no weapon!"
    /// and Defend. The port had refused slot 0 to Equip, and its sums left slot 0 out.
    /// </summary>
    private static void HandsEquipChecks(string dir)
    {
        Console.WriteLine("\nthe Hands can be equipped and fought with (issue #14; MAINMOD2.BAS:1725, 1842-1848)");
        string Norm(string s) => s.Replace("\r\n", "\n");
        string main2 = Norm(PortSource("decompiled-named", "MAINMOD2.BAS"));
        string mons = Norm(PortSource("decompiled-named", "MONSMOD.BAS"));
        string crea = Norm(PortSource("decompiled-named", "CHARCREA.BAS"));

        // ---- the original ----
        Check("MAINMOD2.BAS: sub0F74 leaves on an empty slot, but never on slot 0",
              main2.Contains("    If  p02F6 < 0 Or p02F6 > mc0190 - 1 Then\n        Exit Sub\n    End If\n"
                           + "    If  Chars(pv02F4).Pack(p02F6).Ref < 1 And p02F6 > 0 Then\n        Exit Sub\n    End If\n"
                           + "    l02F8 = ItemTypes(Items(Chars(pv02F4).Pack(p02F6).Ref).Kind).M54D5"), true);
        Check("MAINMOD2.BAS: an equip of wear class 0 fills a weapon hand with the slot's Ref",
              main2.Contains("            If  l02F8 = False Then\n                If  Chars(pv02F4).M5997(0) < 0 Then\n"
                           + "                    Chars(pv02F4).M5997(0) = Chars(pv02F4).Pack(p02F6).Ref"), true);
        Check("MONSMOD.BAS: no weapon hand filled is \"Using no weapon!\"",
              mons.Contains("    If  l0100 < 1 Or (Chars(pv00F4).M5997(0) = -1 And Chars(pv00F4).M5997(1) = -1) Then")
              && mons.Contains("frmDungeonMain!lblCharacterHit.Caption = \"Using no weapon!\""), true);
        Check("CHARCREA.BAS: a new character has neither weapon hand filled",
              crea.Contains("    Chars(m00C2).M5997(0) = -1\n    Chars(m00C2).M5997(1) = -1"), true);
        Check("FRMCHAR3.BAS: Equip hands sub0F74 the selection, slot 0 included",
              Norm(PortSource("decompiled-named", "FRMCHAR3.BAS")).Contains("            Case m0120\n                sub0F74 Me, m001A\n"), true);

        // ---- the data ----
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "MDATA3.MDR"))) { Console.WriteLine("  skipped: no MDATA3.MDR"); return; }
        var items = MdrLoader.LoadItems(System.IO.Path.Combine(dir, "MDATA3.MDR"));
        var byId = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        Item? Look(short id) => byId.TryGetValue(id, out var it) ? it : null;
        var hands = byId[Character.HandsItemId];
        Check("MDATA3 item 0: the Hands, wear class 0 (a weapon), one at a time, two hands, two swings",
              $"{hands.Name.Trim()} class {Character.WearClassOf(hands)} max {Character.MaxWornOfClass(Character.WearClassOf(hands))} hands {hands.NHands} swings {hands.Swings} wearable {Character.IsWearable(hands)}",
              "Hands class 0 max 1 hands 2 swings 2 wearable True");

        // ---- the port ----
        var c = Character.Create("Bare", RaceData.All[0], Alignment.Neutral, Sex.Male, new[] { 12, 12, 12, 12, 12, 12 });
        Check("Equip acts on slot 0, the Hands", ReferenceEquals(c.SlotToEquip(0), c.Pack[0]) && c.Pack[0]!.ItemId == Character.HandsItemId, true);
        Check("and leaves on an empty slot", c.SlotToEquip(5) == null, true);
        Check("a new character may put the Hands on", c.WearRefusal(c.Pack[0]!, hands, Look) ?? "(none)", "(none)");

        var foe = MdrLoader.LoadMonsters(System.IO.Path.Combine(dir, "MDATA5.MDR")).First(m => m.Id != 0 && m.Hits > 5);
        string Swing(Character who)
        {
            var party = new Party(); party.Members.Add(who);
            var fight = new CombatEngine(party, new List<CombatMonster> { new() { Def = foe, Hits = 9999, MaxHits = 9999 } }, Look, new Random(1));
            party.OrderFor(who).Kind = OrderKind.Fight;
            string said = fight.PlayerAttacks(who);
            return said == "Using no weapon!" || said.EndsWith("Using no weapon!") ? $"Using no weapon!, {party.OrderFor(who).Kind}" : $"swung, {party.OrderFor(who).Kind}";
        }
        Check("bare hands not on: \"Using no weapon!\" and Defend", Swing(c), "Using no weapon!, Defend");
        c.Pack[0]!.Equipped = true;
        Check("the Hands on: a swing, and the order stands", Swing(c), "swung, Fight");

        var dagger = items.First(i => i.Id != 0 && i.Type == 1 && i.NHands == 1 && i.Cursed == 0);
        c.PutInPack(new InventorySlot { ItemId = dagger.Id });
        var held = c.Inventory.First(s => s.ItemId == dagger.Id);
        Check("the Hands on, a one-handed weapon finds no hand free (fn11B6, MAINMOD2.BAS:577-578)",
              c.WearRefusal(held, dagger, Look) ?? "(none)", "No Free Hands!");
        c.Pack[0]!.Equipped = false; held.Equipped = true;
        Check("a weapon on, the Hands find no hand free", c.WearRefusal(c.Pack[0]!, hands, Look) ?? "(none)", "No Free Hands!");
        held.Equipped = false; c.Pack[0]!.Equipped = true;

        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mordor-selfcheck-hands-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            c.Save(tmp);
            Check("the Hands stay on through a save and a load", Character.Load(tmp).Pack[0]!.Equipped, true);
        }
        finally { try { System.IO.File.Delete(tmp); } catch { } }

        foreach (var (file, what) in new[] { ("DungeonWindow.axaml.cs", "dungeon"), ("CityWindow.Mdi.cs", "town") })
            Check($"the {what}'s Equip asks SlotToEquip, which lets slot 0 through",
                  PortSource("src/Mordor.App", file).Contains("var slot = _ch.SlotToEquip(PackList.SelectedIndex);"), true);
    }
}
