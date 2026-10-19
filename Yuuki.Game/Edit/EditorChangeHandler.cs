// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Text.Json;
using Sakura.Framework.Reactive;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Edit;

/// <summary>
/// Undo and redo for an <see cref="EditorBeatmap"/>
/// </summary>
/// <remarks>
/// Whole snapshots rather than recorded operations, so nothing that edits the beatmap has to know how
/// to undo itself, and undo can never drift out of step with what was done. A chart is a few hundred
/// kilobytes as text, and a snapshot is taken per edit rather than per frame (a drag is one
/// transaction), so this costs nothing anyone notices. The same snapshots tell whether there is
/// anything unsaved.
/// </remarks>
public class EditorChangeHandler
{
    /// <summary>
    /// How far back undo goes
    /// </summary>
    public const int MAX_SAVED_STATES = 50;

    private readonly EditorBeatmap beatmap;
    private readonly List<string> states = new List<string>();

    private int current = -1;
    private string? savedState;
    private bool restoring;

    public ReactiveBool CanUndo { get; } = new ReactiveBool();

    public ReactiveBool CanRedo { get; } = new ReactiveBool();

    /// <summary>
    /// Whether the beatmap differs from what was last saved (or loaded).
    /// </summary>
    public ReactiveBool HasUnsavedChanges { get; } = new ReactiveBool();

    public EditorChangeHandler(EditorBeatmap beatmap)
    {
        this.beatmap = beatmap;

        beatmap.TransactionEnded += SaveState;

        SaveState();
        MarkSaved();
    }

    /// <summary>
    /// Records the beatmap as it is now, if it changed since the last record. Anything that could be
    /// redone is dropped, as a new edit starts a new branch of history.
    /// </summary>
    public void SaveState()
    {
        if (restoring)
            return;

        string state = snapshot();

        if (current >= 0 && states[current] == state)
            return;

        states.RemoveRange(current + 1, states.Count - current - 1);
        states.Add(state);

        if (states.Count > MAX_SAVED_STATES)
            states.RemoveAt(0);

        current = states.Count - 1;
        updateState();
    }

    public void Undo()
    {
        if (current <= 0)
            return;

        restore(--current);
    }

    public void Redo()
    {
        if (current >= states.Count - 1)
            return;

        restore(++current);
    }

    /// <summary>
    /// Records that what's there now is what's on disk.
    /// </summary>
    public void MarkSaved()
    {
        savedState = states[current];
        updateState();
    }

    private void restore(int index)
    {
        restoring = true;

        try
        {
            beatmap.RestoreFrom(BeatmapUtility.Deserialize(states[index]));
        }
        finally
        {
            restoring = false;
        }

        updateState();
    }

    private string snapshot() => JsonSerializer.Serialize(beatmap.CreateBeatmapForSave());

    private void updateState()
    {
        CanUndo.Value = current > 0;
        CanRedo.Value = current < states.Count - 1;
        HasUnsavedChanges.Value = savedState != null && states[current] != savedState;
    }
}
