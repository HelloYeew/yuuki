// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Edit;

/// <summary>
/// The beatmap being edited: a private copy of a difficulty and its set, so nothing the editor does
/// reaches the library (or song select) until it is saved.
/// </summary>
/// <remarks>
/// <para>
/// Every edit goes through here so the rest of the editor can follow along: notes are kept in time
/// order, and <see cref="NotesChanged"/> / <see cref="TimingChanged"/> fire as they change. Edits are
/// grouped into transactions (<see cref="BeginChange"/> / <see cref="EndChange"/>), one per thing the
/// mapper did, and <see cref="TransactionEnded"/> is what undo history records. A single edit made
/// outside a transaction is its own.
/// </para>
/// <para>
/// The selection lives here too so the timeline and the compose area share it.
/// </para>
/// </remarks>
public class EditorBeatmap
{
    /// <summary>
    /// The working copy. Its <see cref="Beatmap.Notes"/> is only brought up to date by
    /// <see cref="CreateBeatmapForSave"/>; read <see cref="Notes"/> while editing.
    /// </summary>
    public Beatmap Beatmap { get; }

    public BeatmapSet BeatmapSet => Beatmap.BeatmapSet;

    public ControlPointInfo ControlPoints => Beatmap.ControlPoints;

    private readonly List<BeatmapNote> notes;

    /// <summary>
    /// The notes, in time order.
    /// </summary>
    public IReadOnlyList<BeatmapNote> Notes => notes;

    private readonly List<BeatmapNote> selected = new List<BeatmapNote>();

    public IReadOnlyList<BeatmapNote> SelectedNotes => selected;

    /// <summary>
    /// Fired after any note is added, removed, or changed.
    /// </summary>
    public event Action? NotesChanged;

    /// <summary>
    /// Fired after the timing points change.
    /// </summary>
    public event Action? TimingChanged;

    /// <summary>
    /// Fired after anything else about the difficulty or its set changes (metadata, lane count).
    /// </summary>
    public event Action? MetadataChanged;

    public event Action? SelectionChanged;

    /// <summary>
    /// Fired when the outermost transaction ends: one undoable step.
    /// </summary>
    public event Action? TransactionEnded;

    private int transactionDepth;

    /// <param name="source">The library difficulty. Left untouched; this works on copies of it and its set.</param>
    /// <param name="sourceNotes">The difficulty's notes, as loaded for it (the library keeps metadata only).</param>
    public EditorBeatmap(Beatmap source, IEnumerable<BeatmapNote> sourceNotes)
    {
        Beatmap = source.Clone();
        Beatmap.BeatmapSet = source.BeatmapSet.Clone();
        Beatmap.ControlPoints = source.ControlPoints.Clone();
        Beatmap.Notes = Array.Empty<BeatmapNote>();
        Beatmap.NoteSummary = null!;

        notes = sourceNotes.Select(n => n.Clone()).OrderBy(n => n.StartTime).ToList();
    }

    #region Transactions

    /// <summary>
    /// Starts a group of edits that undo as one, such as a drag. Nests.
    /// </summary>
    public void BeginChange() => transactionDepth++;

    public void EndChange()
    {
        if (transactionDepth == 0)
            throw new InvalidOperationException($"{nameof(EndChange)} without a matching {nameof(BeginChange)}.");

        if (--transactionDepth == 0)
            TransactionEnded?.Invoke();
    }

    /// <summary>
    /// Whether a transaction is open.
    /// </summary>
    public bool InTransaction => transactionDepth > 0;

    private void inChange(Action change)
    {
        BeginChange();

        try
        {
            change();
        }
        finally
        {
            EndChange();
        }
    }

    #endregion

    #region Notes

    public void Add(BeatmapNote note) => inChange(() =>
    {
        notes.Insert(insertionIndex(note.StartTime), note);
        NotesChanged?.Invoke();
    });

    public void AddRange(IEnumerable<BeatmapNote> added) => inChange(() =>
    {
        foreach (var note in added)
            notes.Insert(insertionIndex(note.StartTime), note);

        NotesChanged?.Invoke();
    });

    public void Remove(BeatmapNote note) => RemoveRange(new[] { note });

    public void RemoveRange(IEnumerable<BeatmapNote> removed) => inChange(() =>
    {
        var set = removed.ToHashSet();

        notes.RemoveAll(set.Contains);

        if (selected.RemoveAll(set.Contains) > 0)
            SelectionChanged?.Invoke();

        NotesChanged?.Invoke();
    });

    /// <summary>
    /// Applies <paramref name="change"/> to each of <paramref name="changed"/> and puts them back in
    /// time order, as one edit.
    /// </summary>
    public void Update(IEnumerable<BeatmapNote> changed, Action<BeatmapNote> change) => inChange(() =>
    {
        foreach (var note in changed)
            change(note);

        notes.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        NotesChanged?.Invoke();
    });

    /// <summary>
    /// Where a note starting at <paramref name="time"/> goes: after every note at the same time, so
    /// chords keep the order they were placed in.
    /// </summary>
    private int insertionIndex(double time)
    {
        int low = 0;
        int high = notes.Count;

        while (low < high)
        {
            int mid = (low + high) / 2;

            if (notes[mid].StartTime <= time)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    #endregion

    #region Timing and metadata

    /// <summary>
    /// Edits the timing points as one edit. Pass the change, so listeners hear about it after it's done.
    /// </summary>
    public void UpdateTiming(Action<ControlPointInfo> change) => inChange(() =>
    {
        change(ControlPoints);
        ControlPoints.Sort();
        TimingChanged?.Invoke();
    });

    /// <summary>
    /// Edits anything else about the difficulty or its set as one edit.
    /// </summary>
    public void UpdateMetadata(Action<Beatmap> change) => inChange(() =>
    {
        change(Beatmap);
        MetadataChanged?.Invoke();
    });

    #endregion

    #region Selection

    public bool IsSelected(BeatmapNote note) => selected.Contains(note);

    public void Select(IEnumerable<BeatmapNote> toSelect, bool add = false)
    {
        if (!add)
            selected.Clear();

        foreach (var note in toSelect)
        {
            if (!selected.Contains(note))
                selected.Add(note);
        }

        SelectionChanged?.Invoke();
    }

    public void Deselect(BeatmapNote note)
    {
        if (selected.Remove(note))
            SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (selected.Count == 0)
            return;

        selected.Clear();
        SelectionChanged?.Invoke();
    }

    #endregion

    #region Copies out

    /// <summary>
    /// A standalone copy of the working beatmap with its notes filled in: what gets saved, and what
    /// undo history snapshots.
    /// </summary>
    public Beatmap CreateBeatmapForSave()
    {
        var copy = Beatmap.Clone();
        copy.BeatmapSet = BeatmapSet.Clone();
        copy.ControlPoints = ControlPoints.Clone();
        copy.Notes = notes.Select(n => n.Clone()).ToArray();
        return copy;
    }

    /// <summary>
    /// The chart as gameplay plays it, for test play.
    /// </summary>
    public PlayableBeatmap CreatePlayableBeatmap() => PlayableBeatmap.FromBeatmap(CreateBeatmapForSave(), Beatmap.LaneCount);

    /// <summary>
    /// Replaces everything with <paramref name="state"/> (an undo or redo). The selection is dropped,
    /// since the notes it held are not the ones restored.
    /// </summary>
    internal void RestoreFrom(Beatmap state)
    {
        notes.Clear();
        notes.AddRange(state.Notes.Select(n => n.Clone()).OrderBy(n => n.StartTime));

        var set = BeatmapSet.Clone();
        copySerialized(state.BeatmapSet, set);

        // A song, background, video or cover change undone or redone: where to find the file follows its
        // name. Both files stay in the folder until a save, so either one is there to go back to.
        if (set.TrackPath != BeatmapSet.TrackPath && !string.IsNullOrEmpty(set.StorageFolder))
            set.TrackStoragePath = $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{set.StorageFolder}/{set.TrackPath}";

        if (set.BackgroundPath != BeatmapSet.BackgroundPath && !string.IsNullOrEmpty(set.StorageFolder))
        {
            set.BackgroundStoragePath = $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{set.StorageFolder}/{set.BackgroundPath}";
            set.Background = null!;
        }

        if (set.VideoPath != BeatmapSet.VideoPath && !string.IsNullOrEmpty(set.StorageFolder))
            set.VideoStoragePath = string.IsNullOrEmpty(set.VideoPath) ? null! : $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{set.StorageFolder}/{set.VideoPath}";

        if (set.CoverPath != BeatmapSet.CoverPath && !string.IsNullOrEmpty(set.StorageFolder))
        {
            set.CoverStoragePath = $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{set.StorageFolder}/{set.CoverPath}";
            set.Cover = null!;
        }

        copySerialized(state, Beatmap);
        Beatmap.ControlPoints = state.ControlPoints.Clone();
        Beatmap.BeatmapSet = set;
        Beatmap.Notes = Array.Empty<BeatmapNote>();

        selected.Clear();

        SelectionChanged?.Invoke();
        NotesChanged?.Invoke();
        TimingChanged?.Invoke();
        MetadataChanged?.Invoke();
    }

    /// <summary>
    /// Copies what a snapshot holds (every serialized property) from <paramref name="from"/> onto
    /// <paramref name="onto"/>, leaving what it doesn't hold as it is: where the set lives on disk, its
    /// track and cover paths, its loaded textures. A snapshot is the beatmap as JSON, so without these
    /// an undo would forget where to play the song from and where to save to.
    /// </summary>
    private static void copySerialized<T>(T from, T onto)
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0 || property.IsDefined(typeof(JsonIgnoreAttribute)))
                continue;

            property.SetValue(onto, property.GetValue(from));
        }
    }

    #endregion
}
