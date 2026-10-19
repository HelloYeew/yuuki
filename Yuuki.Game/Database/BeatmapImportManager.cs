// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Path = System.IO.Path;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Database;

/// <summary>
/// Imports .osz files into the library in the background, reporting through the notification overlay,
/// and adds the new sets to song select without a restart.
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

    private readonly List<string> gathered = new List<string>();
    private ScheduledTask? gatherTask;

    /// <summary>
    /// Whether <paramref name="path"/> is something this imports.
    /// </summary>
    public static bool CanImport(string path) => Path.GetExtension(path).Equals(".osz", System.StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Imports <paramref name="paths"/> one after another off the update thread. Closing the progress
    /// notification stops after the file in hand.
    /// </summary>
    public void Import(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || storage == null)
            return;

        string beatmapsFolder = storage.GetFullPath(BeatmapSetUtility.BEATMAP_FOLDER_NAME, true);

        var progress = new ProgressNotification
        {
            Text = paths.Count == 1 ? $"Importing {Path.GetFileNameWithoutExtension(paths[0])}" : $"Importing {paths.Count} beatmaps",
            State = ProgressNotificationState.Active
        };

        notifications?.Post(progress);

        // Taken here, on the update thread: the property creates the scheduler on first use.
        var scheduler = Scheduler;
        var library = storage;

        Task.Run(() =>
        {
            var importer = new OszImporter(beatmapsFolder);
            var failures = new List<string>();
            var importedSets = new List<BeatmapSet>();
            int imported = 0;

            for (int i = 0; i < paths.Count; i++)
            {
                if (progress.CancellationToken.IsCancellationRequested)
                    break;

                string name = Path.GetFileNameWithoutExtension(paths[i]);

                if (paths.Count > 1)
                    progress.Text = $"Importing {name} ({i + 1}/{paths.Count})";

                progress.Progress = (float)i / paths.Count;

                var result = importer.Import(paths[i]);

                if (result.Success)
                {
                    imported++;

                    if (result.BeatmapSet != null)
                        importedSets.Add(result.BeatmapSet);
                }
                else
                    failures.Add($"{name}: {result.Error}");
            }

            // Scanned off the update thread into a collection of its own, then merged on it.
            var scanned = new BeatmapsCollection();
            new BeatmapCollectionReader(library, scanned).Read();

            scheduler.Add(() =>
            {
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
                    text += " Click to play it.";
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
}
