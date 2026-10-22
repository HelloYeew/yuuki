// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// A playfield to look at or tap along on rather than play a song on: the real <see cref="Playfield"/>, with the
/// player's keys, scroll speed and placement, running a chart round and round on a clock of its own, with no score or
/// HUD. Its notes look and move as they do in play, so whatever shows how play looks shows it with this: calibration
/// now, and a skin's preview later.
/// </summary>
/// <remarks>
/// It goes round by seeking back <see cref="LoopLength"/> once its clock passes <see cref="LoopEnd"/>, at the same
/// point within the chart, so a chart and a track that repeat every <see cref="LoopLength"/> past
/// <see cref="LoopEnd"/> carry on without a seam. It starts a scroll's length before 0, so the first notes come in
/// from the top.
/// </remarks>
public partial class MockPlayfield : Container
{
    private readonly PlayableBeatmap chart;
    private readonly IAdjustableClock? clockSource;

    private GameplayClockContainer clockContainer = null!;
    private Playfield playfield = null!;

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    /// <summary>
    /// How far back it goes each time round, in ms of the chart.
    /// </summary>
    public double LoopLength { get; }

    /// <summary>
    /// The time it goes back from, in ms of the chart.
    /// </summary>
    public double LoopEnd { get; }

    /// <summary>
    /// How long a note is on screen before the line. Null, before load, takes the player's scroll speed.
    /// </summary>
    public double? Preempt { get; init; }

    private bool allowJudgement;

    /// <summary>
    /// Whether taps and misses are judged, as in play, with the judgement shown over the line. Off, a tap only lights
    /// its lane.
    /// </summary>
    public bool AllowJudgement
    {
        get => allowJudgement;
        set
        {
            allowJudgement = value;
            if (playfield.IsNotNull())
                playfield.AllowJudgement = value;
        }
    }

    private double visualOffset;

    /// <summary>
    /// Where the notes are drawn, as in play (<see cref="Gameplay.Playfield.VisualOffset"/>).
    /// </summary>
    public double VisualOffset
    {
        get => visualOffset;
        set
        {
            visualOffset = value;
            if (playfield.IsNotNull())
                playfield.VisualOffset = value;
        }
    }

    private double audioOffset;

    /// <summary>
    /// The offset on the clock, as in play (<see cref="GameplayContainer.AudioOffset"/>).
    /// </summary>
    public double AudioOffset
    {
        get => audioOffset;
        set
        {
            audioOffset = value;
            if (clockContainer.IsNotNull())
                clockContainer.GameplayClock.Offset = value;
        }
    }

    /// <summary>
    /// Every press and release on the field, at the time it's judged at, as play records them. Raised whether or not
    /// they're judged.
    /// </summary>
    public event Action<ReplayInput>? InputReceived;

    /// <summary>
    /// The field itself.
    /// </summary>
    public Playfield Playfield => playfield;

    public GameplayClock GameplayClock => clockContainer.GameplayClock;

    /// <param name="chart">What it runs.</param>
    /// <param name="loopEnd">The time it goes back from.</param>
    /// <param name="loopLength">How far back it goes.</param>
    /// <param name="clockSource">What it's timed by, such as a track's clock to run with music; null runs it on its own.</param>
    public MockPlayfield(PlayableBeatmap chart, double loopEnd, double loopLength, IAdjustableClock? clockSource = null)
    {
        if (loopLength <= 0 || loopLength > loopEnd)
            throw new ArgumentOutOfRangeException(nameof(loopLength), loopLength, "A loop goes back a length that's there to go back over.");

        this.chart = chart;
        this.clockSource = clockSource;
        LoopEnd = loopEnd;
        LoopLength = loopLength;

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        double preempt = Preempt ?? GameplayScreen.ScrollPreempt(config, chart);

        Child = clockContainer = new GameplayClockContainer(clockSource)
        {
            RelativeSizeAxes = Axes.Both,
            LeadInTime = preempt,
            Child = new PlayfieldAdjustmentContainer
            {
                Child = playfield = new Playfield(chart)
                {
                    Preempt = preempt,
                    AllowJudgement = allowJudgement,
                    VisualOffset = visualOffset,
                    LaneKeys = LaneKeyLayouts.Parse(config?.Get<string>(YuukiConfigManager.LaneKeysSetting(chart.LaneCount)).Value, chart.LaneCount)
                }
            }
        };

        if (config?.Get<Key>(YuukiSetting.SwipeLeftKey).Value is Key swipeLeft)
            playfield.SwipeLeftKey = swipeLeft;

        if (config?.Get<Key>(YuukiSetting.SwipeRightKey).Value is Key swipeRight)
            playfield.SwipeRightKey = swipeRight;

        clockContainer.GameplayClock.Offset = audioOffset;
        playfield.InputReceived += input => InputReceived?.Invoke(input);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        clockContainer.Restart();
    }

    public override void Update()
    {
        base.Update();

        // In the clock's own time, which the offset doesn't move: that's what a seek is in.
        double time = GameplayClock.CurrentTime - GameplayClock.Offset;

        if (time < LoopEnd)
            return;

        clockContainer.Seek(time - LoopLength);
        playfield.Reset();
    }
}
