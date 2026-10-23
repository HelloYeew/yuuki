// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Path = System.IO.Path;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Database;

/// <summary>
/// Imports .osz files into the library in the background, reporting through the notification overlay,
/// and adds the new sets to song select without a restart. Also takes in what's dropped into the top of the
/// game's folder (see <see cref="ImportDropped"/>).
/// </summary>
public partial class BeatmapImportManager : Component
{
    /// <summary>
    /// How long to wait for more dropped files before importing: a drop of several files arrives one
    /// file at a time.
    /// </summary>
    private const double drop_gather_ms = 250;

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? collection { get; set; }

    [Resolved(canBeNull: true)]
    private NotificationOverlay? notifications { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack { get; set; }

    [Resolved(canBeNull: true)]
    private IExternalFiles? externalFiles { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    [Resolved(canBeNull: true)]
    private IWindow? window { get; set; }

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }

    private readonly List<string> gathered = new List<string>();
    private ScheduledTask? gatherTask;

    // Looking in the top of the game's folder is due: at launch, and each time the game comes back to the front.
    private bool droppedLookDue = true;
    private bool wasActive = true;

    // What's dropped there and being taken in now, so another look doesn't take it twice.
    private readonly HashSet<string> droppedInHand = new HashSet<string>();

    /// <summary>
    /// The system's own folder in the top of the game's, for files other apps hand it.
    /// </summary>
    private const string inbox_folder = "Inbox";

    /// <summary>
    /// Whether <paramref name="path"/> is something this imports.
    /// </summary>
    public static bool CanImport(string path) => Path.GetExtension(path).Equals(".osz", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="path"/>, a path or a link (see <see cref="IExternalFiles"/>), is something this imports.
    /// A link whose name says nothing (no extension, or none the app could read) is tried, and a failed import says why.
    /// </summary>
    public bool Accepts(string path)
    {
        string name = nameOf(path);
        return CanImport(name) || (externalFiles?.IsLink(path) == true && !Path.HasExtension(name));
    }

    // The file's own name: a link's is the one the app it came from gives it.
    private string nameOf(string path) => externalFiles?.IsLink(path) == true ? externalFiles.NameOf(path) : Path.GetFileName(path);

    /// <summary>
    /// Adds a dropped file to the next import, which starts once the drop has finished arriving.
    /// </summary>
    public void Gather(string path)
    {
        gathered.Add(path);

        gatherTask ??= Scheduler.AddDelayed(() =>
        {
            gatherTask = null;

            var files = gathered.ToList();
            gathered.Clear();
            Import(files);
        }, drop_gather_ms);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // A phone's app stops updating while it's away, so the window's coming back can't be seen below: the host
        // says when it's back instead.
        if (host != null)
            host.ReturnedToForeground += lookAgain;
    }

    private void lookAgain() => droppedLookDue = true;

    protected override void Dispose(bool isDisposing)
    {
        if (host != null)
            host.ReturnedToForeground -= lookAgain;

        base.Dispose(isDisposing);
    }

    public override void Update()
    {
        base.Update();

        // On a desktop, the window being clicked back into.
        bool active = window?.IsActive ?? true;

        if (active && !wasActive)
            droppedLookDue = true;

        wasActive = active;

        // Not in the middle of a song, which an import would pop up over and take time from.
        if (droppedLookDue && active && screenStack?.CurrentScreen is not (GameplayScreen or ReplayScreen))
        {
            droppedLookDue = false;
            ImportDropped();
        }
    }

    /// <summary>
    /// Takes in what's been dropped into the top of the game's folder, which is all a phone's folder shown in the
    /// computer's file manager can be given. Each .osz is imported, and removed once it has been (one that fails
    /// stays, and says why). Each set folder in the game's own format (one holding a .yks) is moved into the
    /// library, taking the place of the set's folder there if the library has it already. Anything else is the
    /// game's own and left alone. Done at launch and whenever the game comes back to the front: a phone's app back
    /// from the background (<see cref="AppHost.ReturnedToForeground"/>), a desktop's window clicked back into.
    /// </summary>
    public void ImportDropped()
    {
        if (storage == null)
            return;

        try
        {
            List<string> archives = storage.GetFiles(string.Empty).Where(f => CanImport(f) && !droppedInHand.Contains(f)).ToList();
            List<string> sets = storage.GetDirectories(string.Empty)
                                       .Where(d => isDroppedSet(d) && !droppedInHand.Contains(d))
                                       .ToList();

            if (archives.Count == 0 && sets.Count == 0)
                return;

            droppedInHand.UnionWith(archives);
            droppedInHand.UnionWith(sets);

            import(archives.Select(f => storage.GetFullPath(f)).ToList(), sets.Select(d => storage.GetFullPath(d)).ToList(), removeImported: true, () =>
            {
                droppedInHand.ExceptWith(archives);
                droppedInHand.ExceptWith(sets);
            });
        }
        catch (Exception e)
        {
            Logger.Error("Couldn't look for beatmaps dropped into the game's folder.", e);
        }
    }

    // A folder at the top of the game's folder that's a set in the game's own format, and not one of the game's or
    // the system's own folders.
    private bool isDroppedSet(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (name.StartsWith('.') || name == BeatmapSetUtility.BEATMAP_FOLDER_NAME || name == inbox_folder)
            return false;

        return storage!.GetFiles(folder, "*.yks").Any();
    }

    /// <summary>
    /// Whether something dropped into the top of the game's folder is being taken in now (tests).
    /// </summary>
    public bool TakingInDropped => droppedInHand.Count > 0;

    /// <summary>
    /// Imports <paramref name="paths"/> one after another off the update thread. Closing the progress
    /// notification stops after the file in hand.
    /// </summary>
    public void Import(IReadOnlyList<string> paths) => import(paths, Array.Empty<string>(), removeImported: false, null);

    /// <param name="paths">The .osz files (or links) to import.</param>
    /// <param name="setFolders">Set folders in the game's own format to move into the library, before them.</param>
    /// <param name="removeImported">Whether each .osz is deleted once it's been imported.</param>
    /// <param name="finished">Called on the update thread when it's all done.</param>
    private void import(IReadOnlyList<string> paths, IReadOnlyList<string> setFolders, bool removeImported, Action? finished)
    {
        if ((paths.Count == 0 && setFolders.Count == 0) || storage == null)
        {
            finished?.Invoke();
            return;
        }

        int total = paths.Count + setFolders.Count;
        string firstName = setFolders.Count > 0 ? Path.GetFileName(setFolders[0]) : Path.GetFileNameWithoutExtension(nameOf(paths[0]));

        string beatmapsFolder = storage.GetFullPath(BeatmapSetUtility.BEATMAP_FOLDER_NAME, true);

        var progress = new ProgressNotification
        {
            Text = total == 1 ? $"Importing {firstName}" : $"Importing {total} beatmaps",
            State = ProgressNotificationState.Active
        };

        notifications?.Post(progress);

        // Taken here, on the update thread: the property creates the scheduler on first use, and the library is
        // only to be read on it.
        var scheduler = Scheduler;
        var library = storage;
        var links = externalFiles;
        var setFoldersById = collection?.BeatmapSets.GroupBy(s => s.ID).ToDictionary(g => g.Key, g => g.First().StorageFolder)
                             ?? new Dictionary<int, string?>();

        Task.Run(() =>
        {
            var importer = new OszImporter(beatmapsFolder);
            var failures = new List<string>();
            var importedSets = new List<BeatmapSet>();
            int imported = 0;

            foreach (string folder in setFolders)
            {
                if (progress.CancellationToken.IsCancellationRequested)
                    break;

                string name = Path.GetFileName(folder);

                try
                {
                    BeatmapSet set = moveIntoLibrary(folder, beatmapsFolder, setFoldersById);
                    imported++;
                    importedSets.Add(set);
                }
                catch (Exception e)
                {
                    failures.Add($"{name}: {e.Message}");
                }
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (progress.CancellationToken.IsCancellationRequested)
                    break;

                string fileName = links?.IsLink(paths[i]) == true ? links.NameOf(paths[i]) : Path.GetFileName(paths[i]);
                string name = Path.GetFileNameWithoutExtension(fileName);

                if (total > 1)
                    progress.Text = $"Importing {name} ({setFolders.Count + i + 1}/{total})";

                progress.Progress = (float)(setFolders.Count + i) / total;

                var result = links?.IsLink(paths[i]) == true ? importLink(importer, links, paths[i], fileName) : importer.Import(paths[i]);

                if (result.Success)
                {
                    imported++;

                    if (result.BeatmapSet != null)
                        importedSets.Add(result.BeatmapSet);

                    if (removeImported && !tryDelete(paths[i]))
                        failures.Add($"{name}: imported, but it couldn't be removed from the folder");
                }
                else
                    failures.Add($"{name}: {result.Error}");
            }

            // Scanned off the update thread into a collection of its own, then merged on it.
            var scanned = new BeatmapsCollection();
            new BeatmapCollectionReader(library, scanned).Read();

            scheduler.Add(() =>
            {
                finished?.Invoke();

                int added = collection?.AddMissingFrom(scanned) ?? 0;

                foreach (string failure in failures)
                    notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't import {failure}" });

                if (progress.State == ProgressNotificationState.Cancelled)
                    return;

                if (imported == 0)
                {
                    progress.CompleteSilently();
                    return;
                }

                string text = imported == 1 ? "Imported 1 beatmap set." : $"Imported {imported} beatmap sets.";

                // A set already in the library was replaced on disk; the copy song select is showing
                // stays until the next start.
                if (added < imported)
                    text += " Ones you already had update after a restart.";

                // One set: clicking the notification takes you to it in Solo Play. It stays until it can
                // (not mid-song, say), so it can be clicked again from the menu.
                if (imported == 1 && importedSets.Count == 1 && collection?.GetBeatmapSetById(importedSets[0].ID) is BeatmapSet live && !live.Hide)
                {
                    text += gameLayout?.Touch.Value == true ? " Tap to play it." : " Click to play it.";
                    progress.CompletionClickAction = () =>
                    {
                        bool went = screenStack?.PresentBeatmapSet(live) ?? false;

                        // Out of the way of the song select it opened.
                        if (went)
                            notifications?.Hide();

                        return went;
                    };
                }

                progress.Progress = 1;
                progress.CompletionText = text;
                progress.State = ProgressNotificationState.Completed;
            });
        });
    }

    /// <summary>
    /// Moves a set folder in the game's own format into the library: in place of the set's folder there if the
    /// library has the set already (a newer version of it, say, from the editor elsewhere), otherwise under its own
    /// name (numbered, if a folder has that already).
    /// </summary>
    /// <returns>The set, as its .yks says.</returns>
    private static BeatmapSet moveIntoLibrary(string folder, string beatmapsFolder, IReadOnlyDictionary<int, string?> setFoldersById)
    {
        string yks = Directory.GetFiles(folder, "*.yks").First();
        BeatmapSet set;

        using (Stream stream = File.OpenRead(yks))
            set = BeatmapSetUtility.Deserialize(stream) ?? throw new InvalidDataException("its .yks couldn't be read");

        string target;

        if (setFoldersById.TryGetValue(set.ID, out string? existing) && !string.IsNullOrEmpty(existing))
        {
            target = Path.Combine(beatmapsFolder, existing);

            if (Directory.Exists(target))
                Directory.Delete(target, true);
        }
        else
        {
            string name = Path.GetFileName(folder);
            target = Path.Combine(beatmapsFolder, name);

            for (int n = 2; Directory.Exists(target); n++)
                target = Path.Combine(beatmapsFolder, $"{name} ({n})");
        }

        Directory.Move(folder, target);
        return set;
    }

    // Deletes an imported .osz the player dropped in; whether it's gone.
    private static bool tryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't remove {path} after importing it.", e);
            return false;
        }
    }

    private static OszImportResult importLink(OszImporter importer, IExternalFiles links, string link, string fileName)
    {
        try
        {
            using var osz = links.Open(link);
            return importer.Import(osz, fileName);
        }
        catch (Exception e)
        {
            return OszImportResult.Failed(e.Message);
        }
    }
}
