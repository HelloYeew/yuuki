// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Edit;

/// <summary>
/// Everything one editing session shares between the editor's parts: the beatmap and its history, the
/// clock, and the choices the mapper has made (beat divisor, zoom, tool). Cached by the editor screen so
/// every part resolves the same one.
/// </summary>
public class EditorSession
{
    /// <summary>
    /// How much of the song the top timeline shows by default, in ms.
    /// </summary>
    public const double DEFAULT_TIMELINE_SPAN = 6000;

    public const double MIN_TIMELINE_SPAN = 500;
    public const double MAX_TIMELINE_SPAN = 10000;

    public EditorBeatmap Beatmap { get; }

    public EditorChangeHandler Changes { get; }

    /// <summary>
    /// The song's clock. Replaced when the song itself is (see <see cref="ReplaceClock"/>), so read it
    /// each time rather than keeping it.
    /// </summary>
    public EditorClock Clock { get; private set; }

    public EditorBeatDivisor Divisor { get; } = new EditorBeatDivisor();

    /// <summary>
    /// How many ms the top timeline spans. The compose playfield shows half of it from its top to the
    /// judgement line, so zooming one zooms both.
    /// </summary>
    public Reactive<double> TimelineSpan { get; } = new Reactive<double>(DEFAULT_TIMELINE_SPAN);

    public Reactive<ComposeTool> Tool { get; } = new Reactive<ComposeTool>(ComposeTool.Select);

    /// <summary>
    /// The hitsound additions new notes are placed with.
    /// </summary>
    public Reactive<HitSampleAddition> NewNoteAdditions { get; } = new Reactive<HitSampleAddition>(HitSampleAddition.None);

    /// <summary>
    /// Which side new swipe notes are hit from.
    /// </summary>
    public Reactive<SwipeDirection> NewSwipeDirection { get; } = new Reactive<SwipeDirection>(SwipeDirection.Any);

    /// <summary>
    /// What new skill notes do.
    /// </summary>
    public Reactive<SkillType> NewSkill { get; } = new Reactive<SkillType>(SkillType.Heal);

    /// <summary>
    /// How strongly the waveform shows behind the timeline, 0 to 1.
    /// </summary>
    public Reactive<float> WaveformOpacity { get; } = new Reactive<float>(0.5f);

    /// <summary>
    /// Whether placing a note ahead of the playhead seeks there.
    /// </summary>
    public ReactiveBool SeekAfterPlacing { get; } = new ReactiveBool(true);

    public EditorSession(EditorBeatmap beatmap, Func<EditorSession, EditorClock> createClock)
    {
        Beatmap = beatmap;
        Changes = new EditorChangeHandler(beatmap);
        Clock = createClock(this);
    }

    /// <summary>
    /// Puts <paramref name="clock"/> in place of the current one (a new song), where the old one was and
    /// at its speed. Stop the old one first: its song may already be gone.
    /// </summary>
    public void ReplaceClock(EditorClock clock)
    {
        double time = Clock.CurrentTime;
        double rate = Clock.Rate;

        Clock = clock;
        Clock.Rate = rate;
        Clock.Seek(time);
    }

    /// <summary>
    /// <paramref name="time"/> on the current beat divisor's grid.
    /// </summary>
    public double Snap(double time) => Beatmap.ControlPoints.HasTiming ? Beatmap.ControlPoints.GetClosestSnappedTime(time, Divisor.Value.Value) : time;
}

/// <summary>
/// What a click in the compose playfield does.
/// </summary>
public enum ComposeTool
{
    Select,
    Tap,
    Hold,
    Swipe,
    Pass,
    Skill,
    Touch
}

public static class ComposeToolExtensions
{
    /// <summary>
    /// The note type a placement tool puts down; null for <see cref="ComposeTool.Select"/>.
    /// </summary>
    public static NoteType? NoteType(this ComposeTool tool) => tool switch
    {
        ComposeTool.Tap => Beatmaps.NoteType.Tap,
        ComposeTool.Hold => Beatmaps.NoteType.Hold,
        ComposeTool.Swipe => Beatmaps.NoteType.Swipe,
        ComposeTool.Pass => Beatmaps.NoteType.Pass,
        ComposeTool.Skill => Beatmaps.NoteType.Skill,
        ComposeTool.Touch => Beatmaps.NoteType.Touch,
        _ => null
    };
}
