using Mordor.Modding;

namespace Mordor.Mod.ICloudSaves;

/// <summary>
/// The mod's behaviour, apart from its window: when to sync and what to ask. Every path and the clock
/// are given, so the checks drive it on temporary folders with a stand-in main menu.
///
/// <list type="bullet">
/// <item><b>Save points</b> (<see cref="ModEvents.Saved"/>): back in town up the stairs, and the game
///   ending (Exit to Main Menu, or the program closing) - each just after the game's own save, so the
///   snapshot is what the game wrote. Uploaded only when this Mac's world changed and iCloud's did not
///   (<see cref="CloudSync.AutoUpload"/>); otherwise it holds back and the window says why.</item>
/// <item><b>Start</b> (<see cref="ModActions.MainMenuOpened"/>): before anything is loaded, iCloud is
///   looked at. A newer world there is offered, a conflict is put to the player, a restore asked for
///   during the last game is offered again, and unsynced changes from an earlier session go up. A
///   restore made here ends the program, so that every mod that read its files as it loaded starts
///   afresh from the restored ones (the Kaizen buff lists, the Cartographer's notes, the DPS tallies).</item>
/// <item><b>The window</b>: Upload now at any time; a restore only at the next start, since a game in
///   play holds the world in memory and would write it back over the restored one.</item>
/// </list>
/// </summary>
public sealed class CloudSaves
{
    public const string Title = "iCloud Saves";

    private readonly Func<(string? Folder, string? Problem)> _locate;
    private readonly Func<DateTime>? _utcNow;
    private readonly Func<string, bool>? _isDataless;
    private readonly Action<string> _info;
    private readonly Action<string, Exception?> _error;

    /// <param name="savesDir">The game's saves folder (<see cref="IModContext.DataDirectory"/>).</param>
    /// <param name="locate">The mod's iCloud Drive folder, or why there is none (<see cref="ICloudDrive.Locate"/>).</param>
    /// <param name="device">This Mac's name.</param>
    /// <param name="uploadAtSavePoints">The "Upload at save points" toggle, read each time.</param>
    /// <param name="checkAtStart">The "Check iCloud at start" toggle, read each time.</param>
    /// <param name="toldProblem">Why there was no iCloud Drive folder when the player was last told at start ("" for never), kept on this Mac.</param>
    /// <param name="setToldProblem">Keeps it.</param>
    public CloudSaves(string savesDir, Func<(string? Folder, string? Problem)> locate, string device,
                      Func<bool> uploadAtSavePoints, Func<bool> checkAtStart,
                      Action<string> info, Action<string, Exception?> error,
                      Func<DateTime>? utcNow = null, Func<string, bool>? isDataless = null,
                      Func<string>? toldProblem = null, Action<string>? setToldProblem = null)
    {
        SavesDir = savesDir; _locate = locate; Device = device;
        _upload = uploadAtSavePoints; _check = checkAtStart; _info = info; _error = error;
        _utcNow = utcNow; _isDataless = isDataless;
        _told = toldProblem ?? (() => ""); _setTold = setToldProblem ?? (_ => { });
    }

    private readonly Func<string> _told;
    private readonly Action<string> _setTold;

    private readonly Func<bool> _upload, _check;

    /// <summary>
    /// One sync at a time: a save point, Upload now, a restore and the record they write never overlap
    /// (each reads the whole world and rewrites icloudsaves.json).
    /// </summary>
    private readonly object _gate = new();

    public string SavesDir { get; }
    public string Device { get; }

    /// <summary>The last thing the mod did or refused, for the window; empty at first.</summary>
    public string LastMessage { get; private set; } = "";

    /// <summary>Something changed that the window shows (a sync, a refusal). Raised on whatever thread did it.</summary>
    public event Action? Changed;

    /// <summary>The engine on this Mac's iCloud Drive folder, or null and why not.</summary>
    public (CloudSync? Sync, string? Problem) Open()
    {
        var (folder, problem) = _locate();
        return folder == null ? (null, problem) : (new CloudSync(SavesDir, folder, Device, _utcNow, _isDataless), null);
    }

    private void Say(string line)
    {
        LastMessage = line;
        try { _info(line); } catch { }
        try { Changed?.Invoke(); } catch { }
    }

    // ------------------------------------------------------------------ save points

    /// <summary>
    /// A save point: the upload, if one is due and wanted. Synchronous on purpose - on the way out the
    /// program closes as soon as this returns - and small: about a megabyte read, compressed and
    /// written to a local folder; macOS uploads it later. Never throws: a failure is logged and shown.
    /// </summary>
    public void SavePoint(SavePointKind kind)
    {
        if (!_upload()) return;
        try
        {
            var (sync, problem) = Open();
            if (sync == null) { Say(problem!); return; }
            SyncResult r;
            lock (_gate) r = sync.AutoUpload();
            Say((kind == SavePointKind.GameEnded ? "Game ended. " : "Back in town. ") + r.Message);
        }
        catch (Exception ex) { _error("uploading at a save point", ex); Say("The upload failed: " + ex.Message); }
    }

    /// <summary>The window's Upload now (and a conflict's "Keep this Mac's"): up whatever iCloud holds, iCloud's copies kept.</summary>
    public SyncResult UploadNow()
    {
        try
        {
            var (sync, problem) = Open();
            if (sync == null) { Say(problem!); return new SyncResult(false, problem!); }
            SyncResult r;
            lock (_gate) r = sync.UploadNow();
            Say(r.Done ? r.Message + " Every copy already in iCloud is kept." : r.Message);
            return r;
        }
        catch (Exception ex) { _error("uploading", ex); Say("The upload failed: " + ex.Message); return new SyncResult(false, ex.Message); }
    }

    /// <summary>
    /// The window's Restore: during a game the saves are in use, so the snapshot is marked to be
    /// offered at the next start, where it is asked about again before anything changes. Null
    /// cancels a restore waiting.
    /// </summary>
    public void RestoreAtNextStart(string? fileName)
    {
        try
        {
            var (sync, problem) = Open();
            if (sync == null) { Say(problem!); return; }
            lock (_gate) sync.RequestRestore(fileName);
            Say(fileName == null ? "The restore waiting for the next start is cancelled."
                                 : $"{fileName} will be offered for restoring the next time OpenDejenol starts, before anything is loaded. "
                                   + "Exit to the main menu and quit, then start again. Nothing is uploaded meanwhile.");
        }
        catch (Exception ex) { _error("asking for a restore", ex); Say("That failed: " + ex.Message); }
    }

    // ------------------------------------------------------------------ the main menu at start

    /// <summary>
    /// The start-up check, at the main menu with nothing loaded (<see cref="ModActions.MainMenuOpened"/>).
    /// The slow part - reading the world, listing iCloud - runs off the window's thread; the questions
    /// on it. Never throws: a failure is logged and told.
    /// </summary>
    public async Task MainMenu(MainMenuContext menu)
    {
        try
        {
            var (sync, problem) = Open();
            if (sync == null)
            {
                // only once the player has asked for the check, and once for each reason rather than at
                // every start (iCloud Drive left off is a choice); the window says it each time
                if (_check() && _told() != problem)
                {
                    await menu.Ask(Title, problem!, "OK");
                    _setTold(problem!);
                }
                Say(problem!);
                return;
            }
            // iCloud Drive is there again: turned off later, it is told again
            if (_told() != "") _setTold("");
            var st = await Task.Run(sync.Status);
            if (st.State?.RestoreInProgress is { } backup) { await OfferInterrupted(menu, sync, backup); return; }
            if (st.State?.PendingRestore is { } pending) { await OfferPending(menu, sync, st, pending); return; }
            if (!_check()) return;
            switch (st.Kind)
            {
                case SyncKind.LocalNewer:
                    if (_upload()) Say("At start: " + (await Task.Run(() => { lock (_gate) return sync.AutoUpload(); })).Message);
                    break;
                case SyncKind.InSync:
                    sync.Adopt(st);
                    break;
                case SyncKind.CloudNewer:
                    await OfferNewer(menu, sync, st);
                    break;
                case SyncKind.CloudNotDownloaded:
                    await menu.Ask(Title, $"iCloud has a newer saved world, from {CloudSync.Describe(st.Candidate)}, but it is not downloaded to this Mac yet, "
                                        + "so it cannot be checked or restored. Nothing was changed.\n\n"
                                        + "In Finder, open iCloud Drive > OpenDejenol > Snapshots and choose Download Now for it, then start OpenDejenol again.", "OK");
                    Say("A newer world in iCloud is still downloading.");
                    break;
                case SyncKind.Conflict:
                    await OfferConflict(menu, sync, st);
                    break;
            }
        }
        catch (Exception ex)
        {
            _error("the start-up check", ex);
            Say("The start-up check failed: " + ex.Message);
        }
    }

    private static string BackupNote(CloudSync sync) =>
        $"This Mac's saves are copied to a saves-backup folder beside {sync.SavesDir} first, and OpenDejenol closes afterwards so that everything starts from the restored world.";

    private async Task OfferNewer(MainMenuContext menu, CloudSync sync, SyncStatus st)
    {
        string here = st.Local.HasWorld ? "This Mac's saves have not changed since they were last synced." : "This Mac has no saved world yet.";
        int b = await menu.Ask(Title, $"iCloud has a newer saved world, from {CloudSync.Describe(st.Candidate)}.\n\n{here}\n\nRestore it?\n\n{BackupNote(sync)}",
                               "Restore", "Not now");
        if (b == 0) await DoRestore(menu, sync, st.Candidate!.FileName);
        else Say("A newer world in iCloud was not restored.");
    }

    private async Task OfferConflict(MainMenuContext menu, CloudSync sync, SyncStatus st)
    {
        string mine = st.Local.LastWrittenUtc is { } w ? $"This Mac: last saved at {CloudSync.Local(w)}." : "This Mac: changed.";
        string theirs = $"iCloud: saved on {CloudSync.Describe(st.Candidate)}.";
        // Keep this Mac's uploads them, which lifts the hold kept after an interrupted restore: say so
        if (st.State?.UploadsHeld == true)
            mine += " These saves were kept after an interrupted restore, and may be part one world and part another.";
        if (st.Candidate!.State != EntryState.Ready)
        {
            int k = await menu.Ask(Title, "This Mac's saves and iCloud's have both changed since they were last synced, and iCloud's is not downloaded "
                                        + $"to this Mac yet.\n\n{theirs}\n{mine}\n\nNothing will be uploaded until you choose. You can keep this Mac's now "
                                        + "(iCloud's copy stays there as an earlier version), or decide once iCloud's has downloaded.",
                                   "Keep this Mac's", "Decide later");
            if (k == 0) UploadNow(); else Say("Both copies kept; nothing uploaded until you choose.");
            return;
        }
        int b = await menu.Ask(Title, "This Mac's saves and iCloud's have both changed since they were last synced. Which world do you want to play?\n\n"
                                    + $"{theirs}\n{mine}\n\nBoth are kept whichever you choose: the other stays in iCloud as an earlier version, "
                                    + "or in a saves-backup folder.",
                               "Use iCloud's", "Keep this Mac's", "Decide later");
        if (b == 0)
        {
            if (await menu.Ask(Title, $"Replace this Mac's saves with iCloud's?\n\n{BackupNote(sync)}", "Restore", "Cancel") == 0)
                await DoRestore(menu, sync, st.Candidate.FileName);
            else Say("Both copies kept; nothing uploaded until you choose.");
        }
        else if (b == 1) UploadNow();
        else Say("Both copies kept; nothing uploaded until you choose.");
    }

    private async Task OfferPending(MainMenuContext menu, CloudSync sync, SyncStatus st, string pending)
    {
        var entry = st.Cloud.FirstOrDefault(e => string.Equals(e.FileName, pending, StringComparison.OrdinalIgnoreCase));
        string what = entry == null ? pending : CloudSync.Describe(entry);
        string changed = st.Local.HasWorld && st.State?.WorldHash != st.Local.WorldHash
            ? "\n\nThis Mac's saves have changed since they were last synced; they go into the backup."
            : "";
        // a newer one this Mac has not seen (another Mac's, uploaded since the restore was asked for)
        // is not buried by the restore: it stays news, and is offered at the next start
        string newer = st.Candidate is { } c && entry != null && c.CreatedUtc > entry.CreatedUtc
                       && !string.Equals(c.FileName, entry.FileName, StringComparison.OrdinalIgnoreCase)
                       && !(st.State?.Seen.Contains(c.FileName, StringComparer.OrdinalIgnoreCase) ?? false)
            ? $"\n\niCloud also has a newer world, from {CloudSync.Describe(c)}, saved since you asked. It is not restored now; it is offered again at the next start."
            : "";
        int b = await menu.Ask(Title, $"In your last game you asked to restore the world saved on {what}. Restore it now?{changed}{newer}\n\n{BackupNote(sync)}",
                               "Restore", "Cancel the restore");
        if (b != 0) { sync.RequestRestore(null); Say("The restore was cancelled."); return; }
        var r = await Task.Run(() => { lock (_gate) return sync.Restore(pending); });
        if (r.Done) { await Restored(menu, r); return; }
        await PendingFailed(menu, sync, r);
    }

    /// <summary>
    /// A restore asked for in the last game could not be done at this start. Left as it was, the
    /// request would hold every upload for the whole session (<see cref="CloudSync.AutoUpload"/>) and
    /// be offered again at the next start, where restoring it would put an older world over the game
    /// played meanwhile - backed up, but unlooked-for. So the player is told plainly what happens next:
    /// <list type="bullet">
    /// <item>one that can never be done (the snapshot gone from iCloud Drive, unreadable, failing its
    ///   check) is cancelled, and uploads go on as usual - the snapshot, if still there, stays in iCloud;</item>
    /// <item>one that might be done another time (not downloaded yet, the backup or the writing failed)
    ///   is the player's to keep waiting or cancel, each told what it means. The box closed with neither
    ///   pressed keeps it waiting: that changes nothing, and it is asked about again at the next start.</item>
    /// </list>
    /// Nothing here touches the saves: cancelling only lets this Mac's saves be uploaded as a new
    /// snapshot, beside every one already in iCloud.
    /// </summary>
    private async Task PendingFailed(MainMenuContext menu, CloudSync sync, SyncResult r)
    {
        bool interrupted = (await Task.Run(() => { lock (_gate) return sync.LoadState(); }))?.RestoreInProgress != null;
        if (!r.CanRetry || interrupted)
        {
            await Task.Run(() => { lock (_gate) sync.RequestRestore(null); });
            string next = interrupted
                ? "The restore you asked for is cancelled. Nothing is uploaded until the saves are sorted out at the next start."
                : "The restore you asked for is cancelled, since it cannot be done. This Mac's saves are played and uploaded as usual.";
            Say(r.Message + " " + next);
            await menu.Ask(Title, r.Message + "\n\n" + next, "OK");
            return;
        }
        int k = await menu.Ask(Title, r.Message + "\n\nThe restore you asked for can still wait for the next start. If it waits, nothing is uploaded "
                                    + "from this Mac until then, and restoring it then replaces whatever you play now (this Mac's saves are "
                                    + "copied to a saves-backup folder first). If you cancel it, this Mac's saves are uploaded as usual and the "
                                    + "snapshot stays in iCloud, where the iCloud Saves window can ask for it again.",
                               "Keep it waiting", "Cancel the restore");
        if (k == 1)
        {
            await Task.Run(() => { lock (_gate) sync.RequestRestore(null); });
            Say("The restore was cancelled; this Mac's saves are uploaded as usual.");
        }
        else Say("The restore still waits for the next start; nothing is uploaded until then.");
    }

    private async Task OfferInterrupted(MainMenuContext menu, CloudSync sync, string backup)
    {
        int b = await menu.Ask(Title, "The last restore from iCloud was interrupted, so the saves may be part one world and part another. "
                                    + $"This Mac's saves from before it are in {backup}.\n\nPut them back?",
                               "Put them back", "Keep the saves as they are");
        // the box closed with neither pressed (its close button, or the program ending under it) is
        // taken as Keep, the choice that changes no file: the saves are played as they are, and with
        // uploads held nothing of them goes up on its own. Leaving the mark instead would let a game
        // be played on them and then be lost to a later Put them back.
        bool ok = await Task.Run(() => { lock (_gate) return sync.SettleInterrupted(putBack: b == 0); });
        string line = !ok ? $"Putting the saves back failed: copy them back by hand from {backup}."
                    : b == 0 ? "The saves from before the interrupted restore were put back."
                    : "The saves were kept as they are. Nothing is uploaded from them on its own: once you are sure they are right, "
                      + "Upload now in the iCloud Saves window sends them up.";
        Say(line);
        if (!ok || b != 0) { await menu.Ask(Title, line, "OK"); return; }
        await menu.Ask(Title, line + "\n\nOpenDejenol will now close so that everything reads them. Start it again to play.", "OK");
        menu.Quit();
    }

    private async Task DoRestore(MainMenuContext menu, CloudSync sync, string fileName)
    {
        var r = await Task.Run(() => { lock (_gate) return sync.Restore(fileName); });
        if (!r.Done) { Say(r.Message); await menu.Ask(Title, r.Message, "OK"); return; }
        await Restored(menu, r);
    }

    private async Task Restored(MainMenuContext menu, SyncResult r)
    {
        Say(r.Message);
        await menu.Ask(Title, r.Message + "\n\nOpenDejenol will now close so that everything reads the restored world. Start it again to play.", "OK");
        menu.Quit();
    }

    // ------------------------------------------------------------------ for the window

    /// <summary>The window's lines, read now (off the window's thread): where things stand, the last sync, the newest in iCloud.</summary>
    public StatusLines Describe()
    {
        try
        {
            var (sync, problem) = Open();
            if (sync == null) return new StatusLines(problem!, "", "", null, null, false);
            var st = sync.Status();
            string head = st.Kind switch
            {
                SyncKind.Empty => "Nothing saved yet, here or in iCloud.",
                SyncKind.InSync => "In sync with iCloud.",
                SyncKind.LocalNewer => "This Mac has changes not yet in iCloud.",
                SyncKind.CloudNewer => $"iCloud has a newer world, from {CloudSync.Describe(st.Candidate)}.",
                SyncKind.CloudNotDownloaded => $"iCloud has a newer world, from {CloudSync.Describe(st.Candidate)}, still downloading.",
                _ => $"Both changed: this Mac's saves and iCloud's (from {CloudSync.Describe(st.Candidate)}). Choose which to keep.",
            };
            if (st.State?.PendingRestore is { } p) head += $" A restore of {p} waits for the next start.";
            if (st.State?.RestoreInProgress != null) head = "A restore was interrupted: start OpenDejenol again to sort it out. Nothing is uploaded until then.";
            else if (st.State?.UploadsHeld == true) head += " Kept after an interrupted restore: nothing is uploaded on its own until you Upload now.";
            else if (st.UnreadableNews && st.Kind == SyncKind.LocalNewer) head += " iCloud holds a snapshot this Mac cannot read, so nothing is uploaded on its own.";
            string last = st.State is { SyncedUtc: { } t } s
                ? $"Last sync: {s.LastAction?.ToLowerInvariant()} at {CloudSync.Local(t)}, the world from {CloudSync.Shown(s.SyncedDevice)}."
                : "Last sync: never, on this Mac.";
            string newest = st.NewestReady is { } n ? $"Newest in iCloud: {CloudSync.Describe(n)}." : "Newest in iCloud: none yet.";
            int waiting = st.Cloud.Count(e => e.State == EntryState.NotDownloaded), bad = st.Cloud.Count(e => e.State == EntryState.Unreadable);
            if (waiting > 0) newest += $" {waiting} not downloaded yet.";
            if (bad > 0) newest += $" {bad} unreadable (never restored).";
            CloudEntry? restorable = st.Candidate is { State: EntryState.Ready } c ? c : st.NewestReady;
            return new StatusLines(head, last, newest, restorable?.FileName, st.State?.PendingRestore, st.Kind == SyncKind.Conflict);
        }
        catch (Exception ex)
        {
            _error("reading the sync status", ex);
            return new StatusLines("The status could not be read: " + ex.Message, "", "", null, null, false);
        }
    }
}

/// <summary>What the window shows: three lines, the snapshot its Restore would ask for, a restore already waiting, and whether it is a conflict.</summary>
public sealed record StatusLines(string Head, string LastSync, string Newest, string? Restorable, string? Pending, bool Conflict);
