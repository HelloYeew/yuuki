// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// The perspective note highway (a.k.a. playfield...)
/// </summary>
public partial class Playfield : Container
{
    /// <summary>
    /// How long (ms) a note lingers past its end before it despawns.
    /// </summary>
    private const double after_window = 400;

    /// <summary>
    /// Longest spacing (ms) between slide ticks while a hold is sustained. Ticks subdivide the beat,
    /// halved until no longer than this — a 16th at 180 BPM, an 8th at 100 BPM — so they keep time
    /// with the song at a steady 7–13 a second.
    /// </summary>
    private const double slide_tick_max_interval = 150;

    /// <summary>
    /// Slide tick spacing (ms) when the chart's tempo is unknown.
    /// </summary>
    private const double slide_tick_fallback_interval = 100;

    private readonly PlayableBeatmap chart;
    private readonly int laneCount;

    /// <summary>
    /// Time (ms) a note is visible before reaching the judgement line. Lower = faster scroll.
    /// </summary>
    public double Preempt { get; set; } = 750;

    /// <summary>
    /// When false, no auto-miss or input scoring occurs (for editor/replay/scrub use).
    /// </summary>
    public bool AllowJudgement { get; set; } = true;

    private readonly HitWindows hitWindows = new HitWindows();

    /// <summary>
    /// The timing windows notes are judged with.
    /// </summary>
    public HitWindows HitWindows => hitWindows;

    private readonly HitSoundPlayer? hitSounds;

    // Every sound goes through here, so a silent simulation (a replay seek) plays nothing.
    private HitSoundPlayer? sounds => Silent ? null : hitSounds;

    /// <summary>
    /// Judging runs without sound or feedback (no popup, bursts, beams or receptor flashes): set while
    /// a replay seek re-runs the run up to the new time. <see cref="SettleAfterSimulation"/> then
    /// brings the visuals in line with the state it left.
    /// </summary>
    public bool Silent { get; set; }

    /// <summary>
    /// Whether keyboard presses reach the lanes. Off while watching a replay, whose recorded inputs
    /// drive the field instead (see <see cref="ReplayInputs"/>).
    /// </summary>
    public bool AcceptsUserInput { get; set; } = true;

    /// <summary>
    /// Fired for every lane press/release and swipe, as it's judged — what a replay records.
    /// </summary>
    public event Action<ReplayInput>? InputReceived;

    private IReadOnlyList<ReplayInput>? replayInputs;

    // The next recorded input to feed in.
    private int replayIndex;

    /// <summary>
    /// A recorded run's inputs, fed in as the clock reaches each one (before that frame's judging, as
    /// live input is). Null for live play.
    /// </summary>
    public IReadOnlyList<ReplayInput>? ReplayInputs
    {
        get => replayInputs;
        set
        {
            replayInputs = value;
            replayIndex = 0;
        }
    }

    // How each note was left, so a note respawned after a seek looks the way it did: gone once hit,
    // dimmed once missed (the ends of AnimateHit and AnimateMiss).
    private enum NoteLook : byte
    {
        None,
        Hit,
        Missed
    }

    private readonly NoteLook[] looks;

    // The time judging runs at while simulating; null in normal play, which reads the clock.
    private double? simulatedTime;

    private double now => simulatedTime ?? Clock.CurrentTime;

    // Hold state: a hold is judged twice, at the press (head) and at the release
    // (tail). Transient play state, gated by AllowJudgement like the rest of judging.
    // Per lane: the index of the hold currently held there, or −1 for none.
    private readonly int[] activeHoldByLane;

    // Per note: the hold's head has been judged (hit or missed) and only its tail is left.
    private readonly bool[] holdHeadJudged;

    // Per note: the hold was dropped — head missed, or let go well before the end — so its tail
    // can still be caught by grabbing the body again, but only for a Hit at best.
    private readonly bool[] holdBroken;

    // Per note: the last slide tick played for the hold, counted in slideTickIntervals steps from its
    // start. Ticks sit on that grid rather than on frame times, so they don't drift or stagger, and
    // per note rather than per lane so a lane-changing hold keeps its count across the hand-off.
    private readonly int[] lastSlideTick;

    // Per note: the hold's tick spacing, from the tempo in force where it starts.
    private readonly double[] slideTickIntervals;

    // Which lanes' inputs are physically down, tracked even while input is paused so holds can be
    // resolved against it on resume.
    private readonly bool[] laneHeld;

    // Lanes pressed since the last frame — a tap shorter than a frame still counts for Touch notes.
    private readonly bool[] laneTapped;

    /// <summary>
    /// Lets the owner rewrite a judgement before it's shown and scored — skills that upgrade results
    /// (Break -> Critical) hook in here so the popup and the score agree.
    /// </summary>
    public Func<JudgementResult, JudgementResult>? JudgementFilter { get; set; }

    /// <summary>The lane the character stands in. Pass notes need it there; lane presses move it.</summary>
    public int CharacterLane { get; private set; }

    // Gameplay time the character arrived in CharacterLane, which grades Pass notes.
    private double characterArrivalTime = double.NegativeInfinity;

    /// <summary>
    /// While true (paused, counting down, failing) presses and releases judge nothing; lane held-state
    /// is still tracked so <see cref="ReleaseHoldsNotHeld()"/> can settle holds afterwards.
    /// </summary>
    public bool InputPaused { get; set; }

    /// <summary>
    /// The default 4-lane layout, in lane order. Other lane counts use <see cref="LaneKeyLayouts"/>
    /// </summary>
    public static readonly Key[] DEFAULT_LANE_KEYS = LaneKeyLayouts.Default(4);

    public const Key DEFAULT_SWIPE_LEFT_KEY = Key.LShift;
    public const Key DEFAULT_SWIPE_RIGHT_KEY = Key.RShift;

    /// <summary>
    /// Keyboard keys mapped to lanes, in lane order.
    /// </summary>
    public Key[] LaneKeys { get; set; }

    /// <summary>
    /// The key for the left swipe: hits <see cref="SwipeDirection.Left"/> and <see cref="SwipeDirection.Any"/> swipes.
    /// </summary>
    public Key SwipeLeftKey { get; set; } = DEFAULT_SWIPE_LEFT_KEY;

    /// <summary>
    /// The key for the right swipe: hits <see cref="SwipeDirection.Right"/> and <see cref="SwipeDirection.Any"/> swipes.
    /// </summary>
    public Key SwipeRightKey { get; set; } = DEFAULT_SWIPE_RIGHT_KEY;

    /// <summary>
    /// Fired for every judgement (hit or miss), in order. A hold fires twice: press, then release.
    /// </summary>
    public event Action<JudgementResult>? NewJudgement;

    /// <summary>
    /// Fired when a held hold is let go well before its end. That breaks the combo but isn't a
    /// judgement: the tail is still to be judged.
    /// </summary>
    public event Action<BeatmapNote>? HoldBroken;

    /// <summary>
    /// Fired on each beat of the song as gameplay time crosses it (the judgement line pulses on the
    /// same beats), with the beat length in ms, so the HUD can keep time too.
    /// </summary>
    public event Action<double>? Beat;

    /// <summary>
    /// The number of notes currently shown (for diagnostics/tests).
    /// </summary>
    public int VisibleNoteCount => visibleNotes.Count;

    /// <summary>
    /// Bursts still playing (diagnostics/tests).
    /// </summary>
    public int ActiveEffectCount => effectContainer.Children.Count;

    /// <summary>
    /// Whether any lane's hit or miss flash is showing (diagnostics/tests).
    /// </summary>
    public bool AnyLaneFlashing => missBeams.Any(b => b.Alpha > 0) || hitBeams.Any(b => b.Alpha > 0);

    private readonly LanePerspective perspective = new LanePerspective();
    private float noteWidth;

    // A live source so scroll-speed changes affect notes already on screen, not just new ones.
    private readonly Func<double> preemptSource;

    private LaneBackground laneBackground = null!;
    private Container structure = null!;
    private Container noteContainer = null!;

    // Feedback layers: lane beams under the notes, bursts and the judgement word over them.
    private Container beamContainer = null!;
    private Container effectContainer = null!;
    private JudgementPopup judgementPopup = null!;
    private LaneBeam[] keyBeams = Array.Empty<LaneBeam>();
    private LaneBeam[] missBeams = Array.Empty<LaneBeam>();
    private LaneBeam[] hitBeams = Array.Empty<LaneBeam>();
    private Drawable? judgementLine;
    private Color judgementLineColor;

    /// <summary>
    /// How much of its lane's width a note (and its receptor) spans; the rest is the gap to the lane lines.
    /// </summary>
    private const float note_lane_fill = 0.94f;

    private const float judgement_line_thickness = 7;

    /// <summary>
    /// How far up the lane (as depth, 0 = line, 1 = horizon) a held key's glow reaches.
    /// </summary>
    private const float beam_depth = 0.45f;

    /// <summary>How far up the lane a hit's light column shoots.</summary>
    private const float hit_beam_depth = 0.8f;

    // When the beat the judgement line last pulsed on started, for the beat pulse. A time rather
    // than an index, since an index restarts at every timing point.
    private double lastBeatStart = double.NaN;
    private DrawablePool<DrawableNote> notePool = null!;
    private readonly Drawable[] receptors;

    // Presentation: index -> drawable for the notes currently on screen. Reconciled by time each frame.
    private readonly Dictionary<int, DrawableNote> visibleNotes = new Dictionary<int, DrawableNote>();
    private readonly List<int> removeBuffer = new List<int>();

    // Judgement state, keyed by note index so it survives despawn/respawn across seeks.
    private readonly bool[] judged;
    private readonly double[] startTimes;
    private readonly double maxNoteDuration;

    private Color[] laneColors = Array.Empty<Color>();
    private Color swipeColor = Color.White;
    private YuukiColorPalette palette;

    private Vector2 lastSize;

    public Playfield(PlayableBeatmap chart, HitSoundPlayer? hitSounds = null)
    {
        this.chart = chart;
        this.hitSounds = hitSounds;
        laneCount = Math.Max(1, chart.LaneCount);
        receptors = new Drawable[laneCount];
        activeHoldByLane = new int[laneCount];
        Array.Fill(activeHoldByLane, -1);
        holdHeadJudged = new bool[chart.Notes.Length];
        holdBroken = new bool[chart.Notes.Length];
        lastSlideTick = new int[chart.Notes.Length];
        slideTickIntervals = new double[chart.Notes.Length];
        laneHeld = new bool[laneCount];
        preemptSource = () => Preempt;

        // Precompute for the per-frame time window. Notes are already sorted by StartTime.
        judged = new bool[chart.Notes.Length];
        looks = new NoteLook[chart.Notes.Length];
        startTimes = new double[chart.Notes.Length];
        double maxDuration = 0;
        for (int i = 0; i < chart.Notes.Length; i++)
        {
            startTimes[i] = chart.Notes[i].StartTime;
            maxDuration = Math.Max(maxDuration, chart.Notes[i].Duration);

            double bpm = chart.ControlPoints.HasTiming ? chart.ControlPoints.TimingPointAt(chart.Notes[i].StartTime).Bpm : 0;
            slideTickIntervals[i] = slideTickIntervalFor(bpm);
        }
        maxNoteDuration = maxDuration;

        // Fills its parent. Inside a PlayfieldAdjustmentContainer that's a fixed-shape canvas scaled
        // to the window, so the highway keeps its proportions on any aspect ratio.
        RelativeSizeAxes = Axes.Both;

        perspective.LaneCount = laneCount;
        LaneKeys = LaneKeyLayouts.Default(laneCount);
        laneTapped = new bool[laneCount];
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Color[] cycle = { palette.Accent, palette.TextLight, palette.TextMuted, palette.Surface, palette.Control, palette.TextLight, palette.Accent };
        laneColors = new Color[laneCount];
        for (int i = 0; i < laneCount; i++)
            laneColors[i] = cycle[i % cycle.Length];
        swipeColor = palette.Control;

        Children = new Drawable[]
        {
            // Backdrop filling only the lane trapezoid (corners set in rebuildGeometry).
            laneBackground = new LaneBackground
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Backdrop,
                Alpha = 0.5f
            },
            beamContainer = new Container
            {
                RelativeSizeAxes = Axes.Both
            },
            // Lane lines / receptors / judgement line, rebuilt whenever the field is resized.
            structure = new Container
            {
                RelativeSizeAxes = Axes.Both
            },
            notePool = new DrawablePool<DrawableNote>(64),
            noteContainer = new Container
            {
                RelativeSizeAxes = Axes.Both
            },
            effectContainer = new Container
            {
                RelativeSizeAxes = Axes.Both
            },
            judgementPopup = new JudgementPopup()
        };
    }

    /// <summary>
    /// Recomputes the projection and rebuilds lane visuals for the field's current size.
    /// </summary>
    private void rebuildGeometry()
    {
        float width = DrawSize.X;
        float height = DrawSize.Y;
        if (width <= 0 || height <= 0)
            return;

        perspective.LayOut(width, height);

        noteWidth = 2 * perspective.BottomHalfWidth / laneCount * note_lane_fill;

        // Backdrop covers only the lane trapezoid: horizon edges -> player edges.
        laneBackground.SetCorners(
            perspective.Project(1, perspective.EdgeOffset(0)),
            perspective.Project(1, perspective.EdgeOffset(laneCount)),
            perspective.Project(0, perspective.EdgeOffset(laneCount)),
            perspective.Project(0, perspective.EdgeOffset(0)));

        structure.Clear();

        // Converging lane separators from the top edge down to the player edge.
        for (int edge = 0; edge <= laneCount; edge++)
        {
            float offset = perspective.EdgeOffset(edge);
            Vector2 top = perspective.Project(1, offset);
            Vector2 bottom = perspective.Project(0, offset);

            bool outer = edge == 0 || edge == laneCount;
            structure.Add(laneLine(top, bottom, outer ? 5 : 2, outer ? palette.TextLight : palette.Text, outer ? 0.85f : 0.35f));
        }

        // Receptors at the player edge, one per lane.
        for (int i = 0; i < laneCount; i++)
        {
            Vector2 pos = perspective.Project(0, perspective.LaneOffset(i));
            Drawable receptor;
            structure.Add(receptor = new Circle
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = pos,
                Size = new Vector2(noteWidth, DrawableNote.NOTE_HEIGHT),
                Color = laneColors[i],
                Alpha = 0.4f
            });
            receptors[i] = receptor;
        }

        // Judgement line across the player edge.
        judgementLineColor = palette.Text;
        structure.Add(judgementLine = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Position = new Vector2(perspective.CentreX, perspective.PlayerY),
            Size = new Vector2(2 * perspective.BottomHalfWidth, judgement_line_thickness),
            Color = judgementLineColor
        });

        // A key glow and a miss flash per lane, each a gradient up the lane from the line.
        beamContainer.Clear();
        keyBeams = new LaneBeam[laneCount];
        missBeams = new LaneBeam[laneCount];
        hitBeams = new LaneBeam[laneCount];

        for (int i = 0; i < laneCount; i++)
        {
            keyBeams[i] = laneBeam(i, laneColors[i], beam_depth);
            missBeams[i] = laneBeam(i, JudgementColors.MISS, beam_depth);
            hitBeams[i] = laneBeam(i, Color.White, hit_beam_depth);
            hitBeams[i].Blending = BlendingMode.Additive;
        }

        judgementPopup.Position = new Vector2(perspective.CentreX, perspective.PlayerY - height * 0.22f);

        // The highway's edges are straight, so its right edge at the combo's height lies on the line
        // from its top corner to its bottom one.
        float comboY = height * combo_height;
        Vector2 edgeTop = perspective.Project(1, perspective.EdgeOffset(laneCount));
        Vector2 edgeBottom = perspective.Project(0, perspective.EdgeOffset(laneCount));
        float along = (comboY - edgeTop.Y) / (edgeBottom.Y - edgeTop.Y);
        float edgeX = edgeTop.X + (edgeBottom.X - edgeTop.X) * along;

        ComboPosition = new Vector2((edgeX + width) / 2, comboY);
    }

    /// <summary>
    /// How far down the playfield the combo sits, as a fraction of its height.
    /// </summary>
    private const float combo_height = 0.42f;

    /// <summary>
    /// Where the combo goes, in this playfield's space: right of the highway, about two-fifths of the way
    /// down, midway between the highway's edge there and the playfield's. The HUD
    /// draws it there (<see cref="HUD.GameplayHud.ComboSpace"/>), so it follows the playfield's size and place.
    /// </summary>
    public Vector2 ComboPosition { get; private set; }

    /// <summary>
    /// Moves the character to <paramref name="lane"/>. While a hold is held the character stays
    /// anchored to that hold's lane, as the design says, so other presses don't drag it away.
    /// </summary>
    private void moveCharacter(int lane, double time)
    {
        if (lane == CharacterLane)
            return;

        // While a hold is held, the character stays with it: it can follow the hold across the lanes
        // the hold covers, but other presses don't drag it away.
        int anchor = anchoringHold();
        if (anchor >= 0 && !covers(anchor, lane))
            return;

        CharacterLane = lane;
        characterArrivalTime = time;
    }

    /// <summary>
    /// The hold the character is anchored to (one being held), or −1.
    /// </summary>
    private int anchoringHold()
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (activeHoldByLane[lane] >= 0)
                return activeHoldByLane[lane];
        }

        return -1;
    }

    /// <summary>
    /// The lane a hold ends in: its end lane if it changes lanes, otherwise its own.
    /// </summary>
    private int endLaneOf(int index)
    {
        BeatmapNote note = chart.Notes[index];
        return note.EndLane is NoteLane end ? Math.Clamp(laneIndex(end), 0, laneCount - 1) : laneIndex(note.Lane);
    }

    /// <summary>
    /// Whether <paramref name="lane"/> is one of the lanes a hold passes through, start to end.
    /// </summary>
    private bool covers(int index, int lane)
    {
        int start = laneIndex(chart.Notes[index].Lane);
        int end = endLaneOf(index);
        return lane >= Math.Min(start, end) && lane <= Math.Max(start, end);
    }

    /// <summary>
    /// The lowest lane whose key is holding the hold <paramref name="index"/>, or −1 if none is.
    /// </summary>
    private int firstLaneHolding(int index)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (activeHoldByLane[lane] == index)
                return lane;
        }

        return -1;
    }

    /// <summary>
    /// A press in a lane a held hold passes through joins it: following a lane-changing hold means
    /// pressing the lanes it moves into (rolling across, or jumping to its end lane) before letting go
    /// of the earlier ones. A finger sliding across lanes on a touch screen does the same.
    /// </summary>
    private void joinActiveHold(int lane)
    {
        if (activeHoldByLane[lane] >= 0)
            return;

        for (int other = 0; other < laneCount; other++)
        {
            int index = activeHoldByLane[other];
            if (index >= 0 && covers(index, lane))
            {
                activeHoldByLane[lane] = index;
                return;
            }
        }
    }

    private LaneBeam laneBeam(int lane, Color color, float depth)
    {
        float left = perspective.EdgeOffset(lane);
        float right = perspective.EdgeOffset(lane + 1);

        var beam = new LaneBeam
        {
            RelativeSizeAxes = Axes.Both,
            Color = color,
            Alpha = 0
        };

        beam.SetCorners(
            perspective.Project(depth, left),
            perspective.Project(depth, right),
            perspective.Project(0, right),
            perspective.Project(0, left));

        beamContainer.Add(beam);
        return beam;
    }

    private static Drawable laneLine(Vector2 a, Vector2 b, float thickness, Color color, float alpha)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);

        return new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Position = new Vector2((a.X + b.X) / 2, (a.Y + b.Y) / 2),
            Size = new Vector2(length, thickness),
            Rotation = MathF.Atan2(dy, dx) * 180f / MathF.PI,
            Color = color,
            Alpha = alpha
        };
    }

    /// <summary>
    /// Clears all notes and judgement state. Call before (re)starting a run.
    /// </summary>
    public void Reset()
    {
        foreach (DrawableNote note in visibleNotes.Values)
            noteContainer.Remove(note);
        visibleNotes.Clear();
        Array.Clear(judged, 0, judged.Length);
        Array.Clear(holdHeadJudged, 0, holdHeadJudged.Length);
        Array.Clear(holdBroken, 0, holdBroken.Length);

        Array.Fill(activeHoldByLane, -1);
        hitSounds?.StopAllSlides();
        Array.Clear(looks);
        replayIndex = 0;

        CharacterLane = 0;
        characterArrivalTime = double.NegativeInfinity;

        resetEffects();
    }

    /// <summary>
    /// Clears judgements for notes at or after <paramref name="fromTime"/>, e.g. after seeking back
    /// so replaying forward re-judges them. Notes before it keep their result.
    /// </summary>
    public void ResetJudgementsFrom(double fromTime)
    {
        for (int i = 0; i < chart.Notes.Length; i++)
        {
            if (chart.Notes[i].StartTime >= fromTime)
            {
                judged[i] = false;
                holdHeadJudged[i] = false;
                holdBroken[i] = false;
                looks[i] = NoteLook.None;
            }
        }

        // Cancel any hold whose head is in the reset range so it doesn't resolve against a stale press.
        for (int lane = 0; lane < laneCount; lane++)
        {
            int idx = activeHoldByLane[lane];
            if (idx >= 0 && chart.Notes[idx].StartTime >= fromTime)
            {
                activeHoldByLane[lane] = -1;
                sounds?.StopSlide(lane);
            }
        }
    }

    public override void Update()
    {
        base.Update();

        if (DrawSize.X != lastSize.X || DrawSize.Y != lastSize.Y)
        {
            lastSize = DrawSize;
            rebuildGeometry();
        }

        advance(Clock.CurrentTime);
    }

    /// <summary>
    /// One frame of play at <paramref name="time"/>: recorded inputs due by then, then the notes and judging.
    /// </summary>
    private void advance(double time)
    {
        applyReplayInputs(time);
        reconcileNotes(time);

        if (!Silent)
            updateEffects(time);

        if (AllowJudgement)
        {
            updateHolds(time);
            updatePassAndTouch(time);
            updateMisses(time);
        }

        Array.Clear(laneTapped);
    }

    /// <summary>
    /// Runs one frame of judging at <paramref name="time"/> without the clock: a replay seek steps
    /// through the run with this (with <see cref="Silent"/> on) to rebuild the state at a new time.
    /// </summary>
    public void SimulateFrame(double time)
    {
        simulatedTime = time;

        try
        {
            advance(time);
        }
        finally
        {
            simulatedTime = null;
        }
    }

    /// <summary>
    /// After a simulation: drops the notes on screen (the next frame respawns them with the looks the
    /// simulation left), clears every effect, and shows held lanes as held.
    /// </summary>
    public void SettleAfterSimulation()
    {
        foreach (DrawableNote note in visibleNotes.Values)
            noteContainer.Remove(note);
        visibleNotes.Clear();

        effectContainer.Clear();
        judgementPopup.ClearDisplay();

        foreach (LaneBeam beam in missBeams.Concat(hitBeams))
        {
            beam.ClearTransforms();
            beam.Alpha = 0;
        }

        for (int lane = 0; lane < laneCount; lane++)
        {
            if (lane < keyBeams.Length)
            {
                keyBeams[lane].ClearTransforms();
                keyBeams[lane].Alpha = laneHeld[lane] ? 0.3f : 0;
            }

            if (receptors[lane] is not Drawable receptor)
                continue;

            receptor.ClearTransforms();
            receptor.Scale = new Vector2(laneHeld[lane] ? 0.85f : 1);
            receptor.Color = laneColors.Length > lane ? laneColors[lane] : receptor.Color;
        }

        if (judgementLine.IsNotNull())
        {
            judgementLine.ClearTransforms();
            judgementLine.Color = judgementLineColor;
            judgementLine.Scale = Vector2.One;
        }

        lastBeatStart = double.NaN;
    }

    /// <summary>
    /// Starts the slide loops of the holds being held — they stop whenever the clock does.
    /// </summary>
    public void ResumeHeldSlides()
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (activeHoldByLane[lane] is int index and >= 0)
                sounds?.StartSlide(lane, chart.Notes[index].Bank);
        }
    }

    /// <summary>
    /// Puts the recorded inputs back to the start (after a reset).
    /// </summary>
    public void RewindReplay() => replayIndex = 0;

    private void applyReplayInputs(double time)
    {
        if (replayInputs.IsNull())
            return;

        while (replayIndex < replayInputs.Count && replayInputs[replayIndex].Time <= time)
        {
            ReplayInput input = replayInputs[replayIndex++];

            switch (input.Kind)
            {
                case ReplayInputKind.LanePress:
                    PressLane(input.Lane, input.Time);
                    break;

                case ReplayInputKind.LaneRelease:
                    ReleaseLane(input.Lane, input.Time);
                    break;

                case ReplayInputKind.SwipeLeft:
                    PressSwipe(SwipeDirection.Left, input.Time);
                    break;

                case ReplayInputKind.SwipeRight:
                    PressSwipe(SwipeDirection.Right, input.Time);
                    break;

                case ReplayInputKind.Pause:
                    InputPaused = true;
                    break;

                case ReplayInputKind.Resume:
                    if (InputPaused)
                    {
                        InputPaused = false;
                        ReleaseHoldsNotHeld(input.Time);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// The notes nobody presses. A Pass note is caught by the character standing in its lane when it
    /// arrives — graded by how early the character got there. A Touch note is caught by its lane being
    /// held (or tapped this frame) as it reaches the line. Either one missed is left to updateMisses.
    /// </summary>
    private void updatePassAndTouch(double time)
    {
        foreach (KeyValuePair<int, DrawableNote> kv in visibleNotes)
        {
            int index = kv.Key;
            if (judged[index])
                continue;

            BeatmapNote note = chart.Notes[index];
            int lane = laneIndex(note.Lane);

            switch (note.Type)
            {
                case NoteType.Pass when time >= note.StartTime && CharacterLane == lane:
                {
                    double lead = note.StartTime - characterArrivalTime;
                    HitResult result = lead >= hitWindows.Hit ? HitResult.Critical
                        : lead >= hitWindows.Critical ? HitResult.Break
                        : HitResult.Hit;

                    catchNote(index, kv.Value, result, characterArrivalTime - note.StartTime);
                    break;
                }

                case NoteType.Touch when time >= note.StartTime - hitWindows.Critical && lane < laneCount && (laneHeld[lane] || laneTapped[lane]):
                    catchNote(index, kv.Value, HitResult.Critical, 0);
                    break;
            }
        }
    }

    private void catchNote(int index, DrawableNote drawable, HitResult result, double timingError)
    {
        judged[index] = true;
        looks[index] = NoteLook.Hit;
        drawable.AnimateHit();
        sounds?.PlayNote(chart.Notes[index]);
        emitJudgement(new JudgementResult(chart.Notes[index], result, timingError));
    }

    /// <summary>
    /// Shows/hides notes so the visible set matches the current time — any direction.
    /// </summary>
    private void reconcileNotes(double time)
    {
        double preempt = Preempt;

        // Candidate range by StartTime. hi: notes that have entered from the top. lo: extended back
        // by the longest note so a hold whose head is behind us but whose tail is still in view is
        // not dropped.
        int hi = upperBound(startTimes, time + preempt);
        int lo = lowerBound(startTimes, time - after_window - maxNoteDuration);

        // Remove notes that should no longer be shown.
        removeBuffer.Clear();
        foreach (KeyValuePair<int, DrawableNote> kv in visibleNotes)
        {
            if (kv.Key < lo || kv.Key >= hi || !inView(kv.Key, time, preempt))
                removeBuffer.Add(kv.Key);
        }
        foreach (int idx in removeBuffer)
        {
            noteContainer.Remove(visibleNotes[idx]);
            visibleNotes.Remove(idx);
        }

        // Add notes that should now be shown.
        for (int i = lo; i < hi; i++)
        {
            // A hit note is gone for good (it only comes back by being un-judged, e.g. ResetJudgementsFrom).
            if (visibleNotes.ContainsKey(i) || looks[i] == NoteLook.Hit || !inView(i, time, preempt))
                continue;

            spawnNote(i);
        }
    }

    private bool inView(int index, double time, double preempt)
    {
        BeatmapNote note = chart.Notes[index];
        return time >= note.StartTime - preempt && time <= note.EndTime + after_window;
    }

    private void spawnNote(int index)
    {
        BeatmapNote note = chart.Notes[index];
        int lane = laneIndex(note.Lane);

        bool isSwipe = note.Type == NoteType.Swipe;
        float laneOffset = isSwipe ? 0 : perspective.LaneOffset(lane);
        float width = isSwipe ? 2 * perspective.BottomHalfWidth : noteWidth;

        float? endLaneOffset = note.Type == NoteType.Hold && note.EndLane is NoteLane endLane
            ? perspective.LaneOffset(Math.Clamp(laneIndex(endLane), 0, laneCount - 1))
            : null;

        var layout = new NoteLayout(perspective, laneOffset, preemptSource, width, endLaneOffset);
        Color color = isSwipe ? swipeColor : laneColors[lane];

        DrawableNote drawable = notePool.Get(d => d.Apply(note, layout, color));

        // Respawned after a seek: show it as it was left. A hold being held is lit, even one dropped before.
        if (isActiveHold(index))
            drawable.Grab(animate: false);
        else if (looks[index] == NoteLook.Missed)
            drawable.Alpha = 0.25f;

        noteContainer.Add(drawable);
        visibleNotes[index] = drawable;
    }

    /// <summary>
    /// Auto-misses what passed unhit: a note's head once it's beyond the hit window, and a hold's tail
    /// once it's beyond the release window without a release.
    /// </summary>
    private void updateMisses(double time)
    {
        foreach (KeyValuePair<int, DrawableNote> kv in visibleNotes)
        {
            int index = kv.Key;
            if (judged[index] || isActiveHold(index))
                continue;

            BeatmapNote note = chart.Notes[index];

            // Only the tail of this hold is left; it's missed if the release window passes untouched.
            if (holdHeadJudged[index])
            {
                if (time - note.EndTime > hitWindows.ReleaseWindow)
                    judgeTail(index, HitResult.Miss, time - note.EndTime);
                continue;
            }

            if (time - note.StartTime <= hitWindows.MissWindow)
                continue;

            kv.Value.AnimateMiss();
            looks[index] = NoteLook.Missed;

            // A missed head doesn't end a hold: the tail can still be caught by grabbing the body.
            if (note.Type == NoteType.Hold)
            {
                holdHeadJudged[index] = true;
                holdBroken[index] = true;
            }
            else
                judged[index] = true;

            emitJudgement(new JudgementResult(note, HitResult.Miss, time - note.StartTime));
        }
    }

    /// <summary>
    /// Advances held holds: slide ticks until the end, and a tail miss for one held past its release
    /// window, a hold has to be let go of, not just held through.
    /// </summary>
    private void updateHolds(double time)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            int index = activeHoldByLane[lane];

            // Once per hold, however many keys are holding it (a lane-changing hold being followed).
            if (index < 0 || firstLaneHolding(index) != lane)
                continue;

            BeatmapNote note = chart.Notes[index];

            if (time - note.EndTime > hitWindows.ReleaseWindow)
            {
                judgeTail(index, HitResult.Miss, time - note.EndTime);
                continue;
            }

            int tick = slideTickAt(index, time);

            if (time < note.EndTime && tick > lastSlideTick[index])
            {
                lastSlideTick[index] = tick;
                sounds?.PlaySlideTick(note.Bank);

                if (!holdBroken[index])
                {
                    burstAt(lane, laneColors[lane], small: true);

                    if (!Silent && visibleNotes.TryGetValue(index, out DrawableNote? drawable))
                        drawable.PulseHold();
                }
            }
        }
    }

    /// <summary>The slide tick grid step <paramref name="time"/> falls in, counted from the hold's start.</summary>
    private int slideTickAt(int index, double time) => (int)Math.Floor((time - chart.Notes[index].StartTime) / slideTickIntervals[index]);

    private static double slideTickIntervalFor(double bpm)
    {
        if (bpm <= 0)
            return slide_tick_fallback_interval;

        double interval = 60000 / bpm;
        while (interval > slide_tick_max_interval)
            interval /= 2;

        return interval;
    }

    // Keyboard is one input source: it only maps keys onto the source-agnostic entry points below,
    // which touch buttons or a gamepad can call the same way.

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.IsRepeat || !AcceptsUserInput)
            return false;

        if (e.Key == SwipeLeftKey || e.Key == SwipeRightKey)
        {
            PressSwipe(e.Key == SwipeLeftKey ? SwipeDirection.Left : SwipeDirection.Right, pressTime(e));
            return true;
        }

        int lane = Array.IndexOf(LaneKeys, e.Key);
        if (lane < 0 || lane >= laneCount)
            return false;

        PressLane(lane, pressTime(e));
        return true;
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (!AcceptsUserInput)
            return false;

        int lane = Array.IndexOf(LaneKeys, e.Key);
        if (lane < 0 || lane >= laneCount)
            return false;

        ReleaseLane(lane, pressTime(e));
        return false;
    }

    /// <summary>
    /// A lane was pressed at gameplay <paramref name="time"/>: hits that lane's nearest note.
    /// </summary>
    public void PressLane(int lane, double time)
    {
        if (lane < 0 || lane >= laneCount)
            return;

        InputReceived?.Invoke(new ReplayInput(time, ReplayInputKind.LanePress, lane));
        laneHeld[lane] = true;

        if (InputPaused)
            return;

        laneTapped[lane] = true;
        flashReceptor(lane);
        setLanePressed(lane, true);
        moveCharacter(lane, time);
        if (AllowJudgement)
        {
            tryHit(time, swipeSide: null, lane: lane);
            joinActiveHold(lane);
        }
    }

    /// <summary>
    /// A lane was released at gameplay <paramref name="time"/>: resolves its active hold, if any.
    /// </summary>
    public void ReleaseLane(int lane, double time)
    {
        if (lane < 0 || lane >= laneCount)
            return;

        InputReceived?.Invoke(new ReplayInput(time, ReplayInputKind.LaneRelease, lane));
        laneHeld[lane] = false;
        setLanePressed(lane, false);

        if (InputPaused)
            return;

        // Releasing a lane resolves that lane's active hold against the tail timing.
        if (AllowJudgement && activeHoldByLane[lane] >= 0)
            releaseHold(lane, time);
    }

    /// <summary>
    /// A swipe input on <paramref name="side"/> (<see cref="SwipeDirection.Left"/> or
    /// <see cref="SwipeDirection.Right"/>) at gameplay <paramref name="time"/>. Hits the nearest swipe
    /// that accepts that side; a swipe pointing the other way ignores it.
    /// </summary>
    public void PressSwipe(SwipeDirection side, double time)
    {
        if (side == SwipeDirection.Any)
            throw new ArgumentException("A swipe input comes from a side, Left or Right.", nameof(side));

        InputReceived?.Invoke(new ReplayInput(time, side == SwipeDirection.Left ? ReplayInputKind.SwipeLeft : ReplayInputKind.SwipeRight));

        if (InputPaused)
            return;

        flashSide(side);
        if (AllowJudgement)
            tryHit(time, swipeSide: side, lane: -1);
    }

    /// <summary>
    /// Translates the OS event timestamp into gameplay time for latency-free judging, falling back
    /// to the current frame time if the source clock isn't a <see cref="GameplayClock"/> or the
    /// event carried no timestamp (e.g. synthesized test input).
    /// </summary>
    private double pressTime(KeyEvent e)
    {
        if (Clock is GameplayClock gameplayClock && !double.IsNaN(e.Timestamp))
            return gameplayClock.GetTimeAt(e.Timestamp);

        return Clock.CurrentTime;
    }

    /// <summary>
    /// After a pause: any hold whose lane was let go while input was paused is released now, as if
    /// the release happened at the moment play resumes.
    /// </summary>
    public void ReleaseHoldsNotHeld() => ReleaseHoldsNotHeld(now);

    /// <inheritdoc cref="ReleaseHoldsNotHeld()"/>
    /// <param name="time">The gameplay time play resumes at.</param>
    public void ReleaseHoldsNotHeld(double time)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            int index = activeHoldByLane[lane];
            if (index < 0)
                continue;

            if (!laneHeld[lane])
                releaseHold(lane, time);
            else if (firstLaneHolding(index) == lane)
                sounds?.StartSlide(lane, chart.Notes[index].Bank); // pausing stopped the loop
        }
    }

    /// <summary>
    /// Resolves a press: finds the nearest in-window unjudged note — for a swipe input, a swipe that
    /// accepts <paramref name="swipeSide"/>; otherwise a note in <paramref name="lane"/>. A tap or swipe
    /// is graded immediately; a hold's head instead begins a sustain, resolved later on release.
    /// </summary>
    private void tryHit(double time, SwipeDirection? swipeSide, int lane)
    {
        bool swipe = swipeSide.IsNotNull();

        DrawableNote? best = null;
        int bestIndex = -1;
        double bestError = double.MaxValue;

        foreach (KeyValuePair<int, DrawableNote> kv in visibleNotes)
        {
            // A hold whose head is already judged has no head left to hit (see tryGrabHold).
            if (judged[kv.Key] || holdHeadJudged[kv.Key] || isActiveHold(kv.Key))
                continue;

            BeatmapNote note = chart.Notes[kv.Key];

            // Nothing to press on these: Pass is the character's position, Touch is a held lane.
            if (note.Type is NoteType.Pass or NoteType.Touch)
                continue;

            bool isSwipe = note.Type == NoteType.Swipe;
            if (swipe != isSwipe)
                continue;
            if (swipe && note.Direction != SwipeDirection.Any && note.Direction != swipeSide)
                continue;
            if (!swipe && laneIndex(note.Lane) != lane)
                continue;

            double error = time - note.StartTime;
            if (!hitWindows.CanHit(error))
                continue;

            if (Math.Abs(error) < Math.Abs(bestError))
            {
                bestError = error;
                best = kv.Value;
                bestIndex = kv.Key;
            }
        }

        if (best.IsNull())
        {
            // Nothing to hit — but a lane press may be grabbing a dropped hold's body again.
            if (!swipe)
                tryGrabHold(lane, time);

            return; // otherwise an empty or wrong-way press: no penalty, just the lane/swipe flash.
        }

        BeatmapNote target = chart.Notes[bestIndex];
        HitResult result = hitWindows.ResultFor(bestError);

        // A press in the miss band (outside Hit, inside MissWindow) still takes the note, as a Miss —
        // mashing ahead of the notes costs combo instead of being free.
        if (result.IsHit())
            sounds?.PlayNote(target);

        // A hold's head is judged now, like a tap, and starts the hold; its tail is judged on release.
        // A missed head still starts holding, but the tail can then only score a Hit. Either way it
        // shows held while it's held; the judgement shows the miss.
        if (target.Type == NoteType.Hold)
        {
            holdHeadJudged[bestIndex] = true;

            if (!result.IsHit())
            {
                holdBroken[bestIndex] = true;
                looks[bestIndex] = NoteLook.Missed;
            }

            beginHold(bestIndex, lane, time);
        }
        else
        {
            judged[bestIndex] = true;
            looks[bestIndex] = result.IsHit() ? NoteLook.Hit : NoteLook.Missed;

            if (result.IsHit())
            {
                best.AnimateHit();
                if (swipe)
                    flashSwipe();
            }
            else
                best.AnimateMiss();
        }

        emitJudgement(new JudgementResult(target, result, bestError));
    }

    /// <summary>
    /// Starts holding a hold in <paramref name="lane"/>: its head lands on the line and the slide loop
    /// runs until it's let go. Also a re-grab, so a dropped hold lights up again while held.
    /// </summary>
    private void beginHold(int index, int lane, double time)
    {
        moveCharacter(lane, time);
        activeHoldByLane[lane] = index;

        if (visibleNotes.TryGetValue(index, out DrawableNote? drawable))
            drawable.Grab(animate: !Silent);

        // The next tick is the next grid step; a late press (or a re-grab) doesn't catch up on missed ones.
        lastSlideTick[index] = Math.Max(0, slideTickAt(index, time));
        sounds?.StartSlide(lane, chart.Notes[index].Bank);
    }

    /// <summary>
    /// Re-grabs a hold in <paramref name="lane"/> whose head is already judged but whose tail is still
    /// open — after missing the head, or letting go early. Its tail is then capped at Hit.
    /// </summary>
    private void tryGrabHold(int lane, double time)
    {
        foreach (KeyValuePair<int, DrawableNote> kv in visibleNotes)
        {
            int index = kv.Key;
            BeatmapNote note = chart.Notes[index];

            if (!holdHeadJudged[index] || judged[index] || isActiveHold(index) || !covers(index, lane))
                continue;

            if (time - note.EndTime > hitWindows.ReleaseWindow)
                continue;

            beginHold(index, lane, time);
            return;
        }
    }

    /// <summary>
    /// Lets go of <paramref name="lane"/>'s key on the hold it's holding. Near the end, letting go from
    /// the hold's end lane judges the tail. A hold that changes lanes has to be followed there, so
    /// letting go of its last key anywhere else near the end is a Miss. Another key still down in the
    /// hold's lanes carries it on. Letting go of the last key well before the end is a hold break: combo
    /// lost, tail still open.
    /// </summary>
    private void releaseHold(int lane, double time)
    {
        int index = activeHoldByLane[lane];
        if (index < 0)
            return;

        BeatmapNote note = chart.Notes[index];
        double releaseError = time - note.EndTime;
        bool atTail = releaseError >= -hitWindows.ReleaseWindow;

        if (atTail && lane == endLaneOf(index))
        {
            HitResult result = releaseError > hitWindows.ReleaseWindow
                ? HitResult.Miss
                : hitWindows.ResultForRelease(releaseError);

            judgeTail(index, result, releaseError);
            return;
        }

        activeHoldByLane[lane] = -1;
        sounds?.StopSlide(lane);

        // Still held by another key in its lanes: the hold goes on (the slide moves to that key).
        int carrier = firstLaneHolding(index);
        if (carrier >= 0)
        {
            sounds?.StartSlide(carrier, note.Bank);
            return;
        }

        // Let go at the end, but not where the hold ended up: it wasn't followed.
        if (atTail)
        {
            judgeTail(index, HitResult.Miss, releaseError);
            return;
        }

        holdBroken[index] = true;
        looks[index] = NoteLook.Missed;

        if (visibleNotes.TryGetValue(index, out DrawableNote? drawable))
        {
            drawable.LetGo(time);
            drawable.AnimateMiss();
        }

        HoldBroken?.Invoke(note);
    }

    /// <summary>
    /// Judges a hold's tail, ending it. A hold that was dropped along the way (missed head, or let go
    /// early) is capped at Hit.
    /// </summary>
    private void judgeTail(int index, HitResult result, double releaseError)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (activeHoldByLane[lane] != index)
                continue;

            activeHoldByLane[lane] = -1;
            sounds?.StopSlide(lane);
        }

        if (holdBroken[index] && result.IsHit() && result < HitResult.Hit)
            result = HitResult.Hit;

        judged[index] = true;
        looks[index] = result.IsHit() ? NoteLook.Hit : NoteLook.Missed;

        BeatmapNote note = chart.Notes[index];

        if (visibleNotes.TryGetValue(index, out DrawableNote? drawable))
        {
            drawable.LetGo(note.EndTime + releaseError);
            if (result.IsHit())
                drawable.AnimateHit();
            else
                drawable.AnimateMiss();
        }

        if (result.IsHit())
            sounds?.PlayNote(note); // tail hitsound

        emitJudgement(new JudgementResult(note, result, releaseError, isHoldTail: true));
    }

    private bool isActiveHold(int index)
    {
        for (int lane = 0; lane < laneCount; lane++)
        {
            if (activeHoldByLane[lane] == index)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Every judgement leaves through here: the event for scoring, plus the feedback — the judgement
    /// word, and a burst at the receptor for a hit or a red flash up the lane for a miss. A swipe
    /// spans every lane, so its feedback does too.
    /// </summary>
    private void emitJudgement(JudgementResult judgement)
    {
        if (JudgementFilter.IsNotNull())
            judgement = JudgementFilter(judgement);

        NewJudgement?.Invoke(judgement);

        if (Silent)
            return;

        judgementPopup.Show(judgement);

        BeatmapNote note = judgement.Note;
        bool everyLane = note.Type == NoteType.Swipe;
        // A lane-changing hold's release happens where its tail ended up.
        int noteLane = judgement.IsHoldTail && note.EndLane is NoteLane endLane ? laneIndex(endLane) : laneIndex(note.Lane);

        for (int lane = 0; lane < laneCount; lane++)
        {
            if (!everyLane && lane != noteLane)
                continue;

            if (judgement.Result.IsHit())
            {
                Color color = JudgementColors.For(judgement.Result);
                burstAt(lane, color, small: false, critical: judgement.Result == HitResult.Critical);

                // A column of light up the lane, tinted by the judgment.
                if (lane < hitBeams.Length)
                {
                    hitBeams[lane].ClearTransforms();
                    hitBeams[lane].Color = color;
                    hitBeams[lane].Alpha = judgement.Result == HitResult.Critical ? 0.7f : 0.45f;
                    hitBeams[lane].FadeOut(280, Easing.OutQuint);
                }
            }
            else if (lane < missBeams.Length)
            {
                missBeams[lane].ClearTransforms();
                missBeams[lane].Alpha = 0.45f;
                missBeams[lane].FadeOut(400, Easing.OutQuint);
            }
        }
    }

    private void burstAt(int lane, Color color, bool small, bool critical = false)
    {
        if (lane < 0 || lane >= laneCount || noteWidth <= 0)
            return;

        // Round, sized off the lane but capped so wide lanes don't get a screen-sized burst.
        float size = Math.Min(noteWidth * 0.55f, 150);

        HitBurst burst = small ? HitBurst.Small(color, size) : new HitBurst(color, size, critical);
        burst.Position = perspective.Project(0, perspective.LaneOffset(lane));
        effectContainer.Add(burst);
    }

    /// <summary>
    /// Glow up the lane and squash its receptor while the key is down.
    /// </summary>
    private void setLanePressed(int lane, bool pressed)
    {
        if (Silent)
            return;

        if (lane < keyBeams.Length)
        {
            keyBeams[lane].ClearTransforms();
            keyBeams[lane].FadeTo(pressed ? 0.3f : 0f, pressed ? 50 : 200, Easing.OutQuint);
        }

        if (receptors[lane] is Drawable receptor)
        {
            if (pressed)
                receptor.ScaleTo(0.85f, 40, Easing.OutQuint);
            else
                receptor.ScaleTo(1f, 250, UserInterfaceConstant.SoftBounce);
        }
    }

    private void updateEffects(double time)
    {
        // Clear finished bursts. Collected first: removing while enumerating the children would throw.
        removeEffectBuffer.Clear();
        foreach (Drawable effect in effectContainer.Children)
        {
            if (effect is HitBurst { IsFinished: true })
                removeEffectBuffer.Add(effect);
        }

        foreach (Drawable effect in removeEffectBuffer)
            effectContainer.Remove(effect);

        // Pulse the judgement line on each beat of the chart's timing.
        if (!chart.ControlPoints.HasTiming || judgementLine.IsNull() || time < 0)
            return;

        double beatStart = chart.ControlPoints.BeatStartAt(time);

        if (beatStart.Equals(lastBeatStart))
            return;

        lastBeatStart = beatStart;
        Beat?.Invoke(chart.ControlPoints.TimingPointAt(time).BeatLength);

        judgementLine.ClearTransforms();
        judgementLine.Color = palette.Accent;
        judgementLine.Scale = new Vector2(1, 2.2f);
        judgementLine.FadeToColor(judgementLineColor, 300, Easing.OutQuint);
        judgementLine.ScaleTo(Vector2.One, 300, Easing.OutQuint);
    }

    private readonly List<Drawable> removeEffectBuffer = new List<Drawable>();

    /// <summary>
    /// Puts every effect back to rest. The effects animate on the gameplay clock, so when a restart
    /// sends it back, anything started later in the song would freeze on its first frame (a lane
    /// stuck red) — they're cleared instead.
    /// </summary>
    private void resetEffects()
    {
        effectContainer?.Clear();
        judgementPopup?.ClearDisplay();

        foreach (LaneBeam beam in keyBeams.Concat(missBeams).Concat(hitBeams))
        {
            beam.ClearTransforms();
            beam.Alpha = 0;
        }

        for (int lane = 0; lane < receptors.Length; lane++)
        {
            if (receptors[lane] is not Drawable receptor)
                continue;

            receptor.ClearTransforms();
            receptor.Scale = Vector2.One;
            receptor.Color = laneColors.Length > lane ? laneColors[lane] : receptor.Color;
        }

        if (judgementLine.IsNotNull())
        {
            judgementLine.ClearTransforms();
            judgementLine.Color = judgementLineColor;
            judgementLine.Scale = Vector2.One;
        }

        lastBeatStart = double.NaN;
        Array.Clear(laneHeld);
        Array.Clear(laneTapped);
    }

    private void flashReceptor(int lane)
    {
        if (Silent)
            return;

        if (lane >= 0 && lane < receptors.Length && receptors[lane].IsNotNull())
            receptors[lane].FlashColor(Color.White, 200, Easing.OutQuint);
    }

    private void flashSwipe()
    {
        if (Silent)
            return;

        foreach (Drawable receptor in receptors)
            receptor?.FlashColor(Color.White, 200, Easing.OutQuint);
    }

    /// <summary>
    /// Flashes the half of the field a swipe input came from (the middle lane counts for both).
    /// </summary>
    private void flashSide(SwipeDirection side)
    {
        for (int lane = 0; lane < receptors.Length; lane++)
        {
            bool onSide = side == SwipeDirection.Left ? lane < (laneCount + 1) / 2 : lane >= laneCount / 2;
            if (onSide)
                flashReceptor(lane);
        }
    }

    private static int laneIndex(NoteLane lane) => (int)lane;

    /// <summary>
    /// First index whose value is &gt;= <paramref name="value"/>.
    /// </summary>
    private static int lowerBound(double[] sorted, double value)
    {
        int low = 0;
        int high = sorted.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (sorted[mid] < value)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }

    /// <summary>
    /// First index whose value is &gt; <paramref name="value"/>.
    /// </summary>
    private static int upperBound(double[] sorted, double value)
    {
        int low = 0;
        int high = sorted.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (sorted[mid] <= value)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }
}
