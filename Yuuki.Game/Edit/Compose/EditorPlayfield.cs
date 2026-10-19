// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Compose;

/// <summary>
/// The compose area
/// </summary>
public partial class EditorPlayfield : Container, IHasContextMenu, IBlockGlobalAltScrollVolume
{
    private const float max_lane_width = 90;
    private const float note_height = 16;
    private const float touch_height = 7;
    private const float tail_height = 8;
    private const float note_inset = 4;
    private const float judgement_margin = 110;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    private EditorColors colors;

    private readonly LineSet lanes;
    private readonly LineSet grid;
    private readonly Container noteLayer;
    private readonly EditorNotePiece ghost;
    private readonly Container dragBox;
    private readonly SpriteText hint;
    private HitSoundPlayer? hitSounds;

    private readonly Dictionary<BeatmapNote, EditorNotePiece> pieces = new Dictionary<BeatmapNote, EditorNotePiece>();
    private readonly Stack<EditorNotePiece> spare = new Stack<EditorNotePiece>();
    private readonly HashSet<BeatmapNote> visible = new HashSet<BeatmapNote>();

    private double maxDuration;
    private double lastHitSoundTime = double.NaN;

    private Vector2? mouse;

    private EditorBeatmap beatmap => session.Beatmap;

    public EditorPlayfield()
    {
        RelativeSizeAxes = Axes.Both;
        Size = Vector2.One;

        Children = new Drawable[]
        {
            lanes = new LineSet { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            grid = new LineSet { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            noteLayer = new Container { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            ghost = new EditorNotePiece { Alpha = 0 },
            dragBox = new Container
            {
                Masking = true,
                BorderThickness = 2,
                BorderColor = Color.White,
                Alpha = 0,
                Child = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = Color.White, Alpha = 0.1f }
            },
            hint = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "Add a timing point in the Timing tab to start placing notes.",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.Bold),
                Color = Color.White,
                Alpha = 0
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        colors = EditorColors.From(colorProvider);
        AddInternal(hitSounds = new HitSoundPlayer());

        beatmap.NotesChanged += onNotesChanged;
        onNotesChanged();
    }

    private void onNotesChanged() => maxDuration = beatmap.Notes.Count == 0 ? 0 : beatmap.Notes.Max(n => n.Duration);

    protected override void Dispose(bool isDisposing)
    {
        beatmap.NotesChanged -= onNotesChanged;
        base.Dispose(isDisposing);
    }

    #region Geometry

    private int laneCount => LaneKeyLayouts.Clamp(beatmap.Beatmap.LaneCount);

    private float laneWidth => Math.Min(max_lane_width, Math.Max(20, (DrawWidth - 40) / laneCount));

    private float fieldLeft => (DrawWidth - laneWidth * laneCount) / 2;

    private float fieldWidth => laneWidth * laneCount;

    private float judgementY => Math.Max(40, DrawHeight - judgement_margin);

    /// <summary>
    /// Pixels per millisecond: half the timeline's span fills the space above the judgement line.
    /// </summary>
    private float pixelsPerMs => judgementY / (float)(session.TimelineSpan.Value / 2);

    private double now => session.Clock.CurrentTime;

    private float yAt(double time) => judgementY - (float)((time - now) * pixelsPerMs);

    private double timeAt(float y) => now + (judgementY - y) / pixelsPerMs;

    private float laneLeft(int lane) => fieldLeft + lane * laneWidth;

    private float laneCentre(int lane) => laneLeft(lane) + laneWidth / 2;

    /// <summary>
    /// The lane under <paramref name="x"/>, kept within the field.
    /// </summary>
    private int laneAt(float x) => Math.Clamp((int)Math.Floor((x - fieldLeft) / laneWidth), 0, laneCount - 1);

    /// <summary>
    /// Where <paramref name="note"/>'s head is drawn, in screen space.
    /// </summary>
    public Vector2 ScreenSpacePositionOf(BeatmapNote note) =>
        ToScreenSpace(new Vector2(note.Type == NoteType.Swipe ? fieldLeft + fieldWidth / 2 : laneCentre((int)note.Lane), yAt(note.StartTime)));

    private bool insideField(Vector2 position) => position.X >= fieldLeft && position.X <= fieldLeft + fieldWidth;

    private int laneOf(BeatmapNote note) => Math.Clamp((int)note.Lane, 0, laneCount - 1);

    private NoteGeometry geometryOf(BeatmapNote note)
    {
        float y = yAt(note.StartTime);

        if (note.Type == NoteType.Swipe)
            return new NoteGeometry(new RectangleF(fieldLeft + note_inset, y - note_height / 2, fieldWidth - note_inset * 2, note_height), null, null, 0);

        int lane = laneOf(note);
        float height = note.Type == NoteType.Touch ? touch_height : note_height;
        var head = new RectangleF(laneLeft(lane) + note_inset, y - height / 2, laneWidth - note_inset * 2, height);

        if (note.Type != NoteType.Hold || note.Duration <= 0)
            return new NoteGeometry(head, null, null, 0);

        int endLane = note.EndLane is NoteLane end ? Math.Clamp((int)end, 0, laneCount - 1) : lane;
        float endY = yAt(note.EndTime);
        float bodyWidth = laneWidth * 0.55f;

        var tail = new RectangleF(laneLeft(endLane) + note_inset, endY - tail_height / 2, laneWidth - note_inset * 2, tail_height);

        float startX = laneCentre(lane);
        float endX = laneCentre(endLane);

        // Clockwise from the top left; the tail is the top end, as later times are higher up.
        var body = new[]
        {
            new Vector2(endX - bodyWidth / 2, endY),
            new Vector2(endX + bodyWidth / 2, endY),
            new Vector2(startX + bodyWidth / 2, y),
            new Vector2(startX - bodyWidth / 2, y)
        };

        return new NoteGeometry(head, tail, body, bodyWidth);
    }

    #endregion

    #region Drawing

    public override void Update()
    {
        base.Update();

        drawLanes();
        drawGrid();
        updateNotes();
        updateGhost();
        updateDragBox();
        playHitSounds();

        hint.Alpha = beatmap.ControlPoints.HasTiming ? 0 : 1;
    }

    private void drawLanes()
    {
        lanes.Clear();

        float top = 0, height = DrawHeight;

        for (int lane = 0; lane < laneCount; lane++)
            lanes.Add(new RectangleF(laneLeft(lane), top, laneWidth, height), colors.Backdrop.Lighten(lane % 2 == 0 ? 0.08f : 0.03f), 0.85f);

        for (int edge = 0; edge <= laneCount; edge++)
            lanes.Add(new RectangleF(laneLeft(edge) - 1, top, 2, height), Color.White, edge == 0 || edge == laneCount ? 0.35f : 0.12f);

        // The judgement line: where the playhead is.
        lanes.Add(new RectangleF(fieldLeft - 8, judgementY - 2, fieldWidth + 16, 4), colors.Accent, 0.9f);
    }

    private void drawGrid()
    {
        grid.Clear();

        var timing = beatmap.ControlPoints;

        if (!timing.HasTiming || pixelsPerMs <= 0)
            return;

        int divisor = session.Divisor.Value.Value;
        double from = timeAt(DrawHeight);
        double to = timeAt(0);
        int drawn = 0;

        var points = timing.TimingPoints;

        for (int i = 0; i < points.Count && drawn < 3000; i++)
        {
            var point = points[i];

            // The first section's grid runs back before it; every section runs up to the next.
            double sectionStart = i == 0 ? Math.Min(point.Time, from) : point.Time;
            double sectionEnd = i + 1 < points.Count ? points[i + 1].Time : double.MaxValue;

            if (sectionEnd < from || sectionStart > to)
                continue;

            double step = point.BeatLength / divisor;
            long firstTick = (long)Math.Ceiling((Math.Max(sectionStart, from) - point.Time) / step - 1e-6);

            for (long tick = firstTick; drawn < 3000; tick++)
            {
                double time = point.Time + tick * step;

                if (time > to || time >= sectionEnd)
                    break;

                int kind = EditorBeatDivisor.DivisorForTick(tick, divisor);
                bool bar = ((tick % (point.BeatsPerBar * divisor)) + point.BeatsPerBar * divisor) % (point.BeatsPerBar * divisor) == 0;

                float thickness = bar ? 3 : kind == 1 ? 2 : 1.5f;
                float alpha = bar ? 0.85f : kind == 1 ? 0.5f : 0.35f;

                grid.Add(new RectangleF(fieldLeft, yAt(time) - thickness / 2, fieldWidth, thickness), bar ? Color.White : EditorBeatDivisor.ColorFor(kind), alpha);
                drawn++;
            }
        }
    }

    private void updateNotes()
    {
        double from = timeAt(DrawHeight + note_height);
        double to = timeAt(-note_height);

        visible.Clear();

        var notes = beatmap.Notes;
        int start = firstIndexFrom(from - maxDuration);

        for (int i = start; i < notes.Count && notes[i].StartTime <= to; i++)
        {
            if (notes[i].EndTime >= from)
                visible.Add(notes[i]);
        }

        foreach (var gone in pieces.Keys.Where(n => !visible.Contains(n)).ToArray())
        {
            var piece = pieces[gone];
            pieces.Remove(gone);
            noteLayer.Remove(piece, false);
            spare.Push(piece);
        }

        var hovered = beatmap.InTransaction ? null : noteAt(mouse);
        bool selecting = session.Tool.Value == ComposeTool.Select;

        foreach (var note in visible)
        {
            if (!pieces.TryGetValue(note, out var piece))
            {
                piece = spare.Count > 0 ? spare.Pop() : new EditorNotePiece();
                piece.Apply(note);
                pieces[note] = piece;
                noteLayer.Add(piece);
            }

            piece.Layout(geometryOf(note), colorOf(note), beatmap.IsSelected(note), selecting && note == hovered);
        }
    }

    private Color colorOf(BeatmapNote note) => note.Type == NoteType.Swipe ? colors.SwipeNote : colors.LaneColor(laneOf(note));

    private int firstIndexFrom(double time)
    {
        var notes = beatmap.Notes;
        int low = 0, high = notes.Count;

        while (low < high)
        {
            int mid = (low + high) / 2;

            if (notes[mid].StartTime < time)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private void updateGhost()
    {
        var tool = session.Tool.Value;

        if (tool == ComposeTool.Select || mouse is not Vector2 position || (!insideField(position) && placing == null))
        {
            ghost.Alpha = 0;
            return;
        }

        var note = placing ?? noteForPlacement(position);
        ghost.Apply(note);

        var color = beatmap.ControlPoints.HasTiming ? colorOf(note) : ColorExtensions.FromHex("eb4747");
        ghost.Layout(geometryOf(note), color, false, false, ghost: placing == null);
    }

    private void updateDragBox()
    {
        if (boxStart is not (double time, float x) || mouse is not Vector2 position)
        {
            dragBox.Alpha = 0;
            return;
        }

        float y0 = yAt(time);
        float left = Math.Min(x, position.X), right = Math.Max(x, position.X);
        float top = Math.Min(y0, position.Y), bottom = Math.Max(y0, position.Y);

        dragBox.Alpha = 1;
        dragBox.Position = new Vector2(left, top);
        dragBox.Size = new Vector2(right - left, bottom - top);
    }

    /// <summary>
    /// While playing, each note's hitsound as the playhead crosses it.
    /// </summary>
    private void playHitSounds()
    {
        double time = now;

        if (!session.Clock.IsRunning || session.Clock.IsSeeking || double.IsNaN(lastHitSoundTime) || time < lastHitSoundTime || time - lastHitSoundTime > 200)
        {
            lastHitSoundTime = time;
            return;
        }

        var notes = beatmap.Notes;

        for (int i = firstIndexFrom(lastHitSoundTime); i < notes.Count && notes[i].StartTime <= time; i++)
        {
            if (notes[i].StartTime > lastHitSoundTime)
                hitSounds?.PlayNote(notes[i]);
        }

        lastHitSoundTime = time;
    }

    #endregion

    #region Hit-testing

    /// <summary>
    /// The note under <paramref name="position"/>: selected notes first, then whichever is closest in time
    /// to the playhead
    /// </summary>
    private BeatmapNote? noteAt(Vector2? position)
    {
        if (position is not Vector2 p)
            return null;

        BeatmapNote? best = null;
        double bestScore = double.MaxValue;

        foreach (var note in visible)
        {
            if (!hit(geometryOf(note), p))
                continue;

            double score = (beatmap.IsSelected(note) ? 0 : 1_000_000) + Math.Abs(note.StartTime - now);

            if (score < bestScore)
            {
                bestScore = score;
                best = note;
            }
        }

        return best;
    }

    private static bool hit(NoteGeometry geometry, Vector2 p, float slop = 3)
    {
        if (inflate(geometry.Head, slop).Contains(p))
            return true;

        if (geometry.Tail is RectangleF tail && inflate(tail, slop).Contains(p))
            return true;

        if (geometry.Body is Vector2[] body)
        {
            // Between the ends in y, and within half the body's width of its centre line at that y.
            float top = body[0].Y, bottom = body[3].Y;

            if (p.Y < top || p.Y > bottom || bottom - top < 1)
                return false;

            float t = (p.Y - top) / (bottom - top);
            float centre = (body[0].X + body[1].X) / 2 * (1 - t) + (body[3].X + body[2].X) / 2 * t;

            return Math.Abs(p.X - centre) <= geometry.BodyWidth / 2 + slop;
        }

        return false;
    }

    private static bool hitTail(NoteGeometry geometry, Vector2 p) => geometry.Tail is RectangleF tail && inflate(tail, 4).Contains(p);

    private static RectangleF inflate(RectangleF rect, float by) => new RectangleF(rect.X - by, rect.Y - by, rect.Width + by * 2, rect.Height + by * 2);

    #endregion

    #region Placement

    // A hold being dragged out, from mouse down to mouse up.
    private BeatmapNote? placing;

    private BeatmapNote noteForPlacement(Vector2 position)
    {
        var tool = session.Tool.Value;
        var type = tool.NoteType() ?? NoteType.Tap;
        double time = session.Snap(timeAt(position.Y));

        return new BeatmapNote
        {
            Type = type,
            Lane = type == NoteType.Swipe ? NoteLane.Lane1 : (NoteLane)laneAt(position.X),
            StartTime = time,
            EndTime = time,
            Bank = type == NoteType.Hold ? SampleBank.Drum : SampleBank.Normal,
            Additions = session.NewNoteAdditions.Value,
            Direction = session.NewSwipeDirection.Value,
            Skill = session.NewSkill.Value
        };
    }

    private void beginPlacement(Vector2 position)
    {
        if (!beatmap.ControlPoints.HasTiming || !insideField(position))
            return;

        var note = noteForPlacement(position);

        if (note.Type == NoteType.Hold)
        {
            placing = note;
            return;
        }

        commit(note);
    }

    private void updatePlacement(Vector2 position)
    {
        if (placing == null)
            return;

        double end = session.Snap(timeAt(position.Y));
        int lane = laneAt(position.X);

        // Dragged the other way: the hold runs from the cursor to where it was pressed.
        placing.EndTime = Math.Max(end, placing.StartTime);
        placing.EndLane = lane == (int)placing.Lane ? null : (NoteLane)lane;
    }

    private void endPlacement()
    {
        if (placing == null)
            return;

        var note = placing;
        placing = null;

        if (note.Duration > 0)
            commit(note);
    }

    /// <summary>
    /// Adds a note, replacing any already within 2 ms in its lane
    /// </summary>
    private void commit(BeatmapNote note)
    {
        beatmap.BeginChange();

        var clashing = beatmap.Notes.Where(n => Math.Abs(n.StartTime - note.StartTime) < 2 && (n.Lane == note.Lane || n.Type == NoteType.Swipe || note.Type == NoteType.Swipe)).ToArray();

        if (clashing.Length > 0)
            beatmap.RemoveRange(clashing);

        beatmap.Add(note);
        beatmap.EndChange();

        if (session.SeekAfterPlacing.Value && note.StartTime > now + 1)
            session.Clock.SeekSmoothlyTo(note.StartTime);
    }

    #endregion

    #region Selection and moving

    // A drag box: where it started, in time (so it stays put as the field scrolls) and x.
    private (double Time, float X)? boxStart;
    private bool boxAdds;

    private enum DragKind
    {
        None,
        Box,
        Move,
        Tail,
        Place
    }

    private DragKind drag;

    // A move in progress: where it started, and every selected note as it was.
    private Vector2 dragOrigin;
    private double dragOriginTime;
    private BeatmapNote? dragReference;
    private Dictionary<BeatmapNote, (double Start, double End, NoteLane Lane, NoteLane? EndLane)> dragStart = new Dictionary<BeatmapNote, (double, double, NoteLane, NoteLane?)>();

    private void beginMove(Vector2 position)
    {
        dragOrigin = position;
        dragOriginTime = timeAt(position.Y);
        dragReference = beatmap.SelectedNotes.OrderBy(n => n.StartTime).FirstOrDefault();
        dragStart = beatmap.SelectedNotes.ToDictionary(n => n, n => (n.StartTime, n.EndTime, n.Lane, n.EndLane));

        beatmap.BeginChange();
    }

    private void updateMove(Vector2 position)
    {
        if (dragReference == null)
            return;

        var original = dragStart[dragReference];

        // The earliest selected note lands on the grid; the rest keep their distance from it.
        double offset = session.Snap(original.Start + (timeAt(position.Y) - dragOriginTime)) - original.Start;

        int laneShift = laneAt(position.X) - laneAt(dragOrigin.X);
        var laned = dragStart.Where(kv => kv.Key.Type != NoteType.Swipe).Select(kv => kv.Value).ToArray();

        if (laned.Length > 0)
        {
            int lowest = laned.Min(v => Math.Min((int)v.Lane, (int)(v.EndLane ?? v.Lane)));
            int highest = laned.Max(v => Math.Max((int)v.Lane, (int)(v.EndLane ?? v.Lane)));
            laneShift = Math.Clamp(laneShift, -lowest, laneCount - 1 - highest);
        }

        beatmap.Update(dragStart.Keys, note =>
        {
            var from = dragStart[note];

            note.StartTime = Math.Max(0, from.Start + offset);
            note.EndTime = Math.Max(note.StartTime, from.End + offset);

            if (note.Type == NoteType.Swipe)
                return;

            note.Lane = (NoteLane)((int)from.Lane + laneShift);
            note.EndLane = from.EndLane is NoteLane end ? (NoteLane)((int)end + laneShift) : null;
        });
    }

    private BeatmapNote? tailDragged;

    private void updateTail(Vector2 position)
    {
        if (tailDragged is not BeatmapNote note)
            return;

        double end = Math.Max(note.StartTime + 1, session.Snap(timeAt(position.Y)));
        int lane = laneAt(position.X);

        beatmap.Update(new[] { note }, n =>
        {
            n.EndTime = end;
            n.EndLane = lane == (int)n.Lane ? null : (NoteLane)lane;
        });
    }

    private void finishBox()
    {
        if (boxStart is not (double time, float x) || mouse is not Vector2 position)
            return;

        double t0 = Math.Min(time, timeAt(position.Y)), t1 = Math.Max(time, timeAt(position.Y));
        float left = Math.Min(x, position.X), right = Math.Max(x, position.X);

        var inBox = beatmap.Notes.Where(n =>
        {
            if (n.StartTime < t0 || n.StartTime > t1)
                return false;

            var head = geometryOf(n).Head;
            float centre = head.X + head.Width / 2;
            return centre >= left && centre <= right;
        });

        beatmap.Select(inBox, boxAdds);
    }

    #endregion

    #region Input

    private bool platformModifier(KeyModifiers modifiers)
    {
        var state = GetContainingInputManager()?.CurrentState;
        return (modifiers & KeyModifiers.Control) != 0 || state?.IsPressed(Key.WinLeft) == true || state?.IsPressed(Key.WinRight) == true;
    }

    private KeyModifiers currentModifiers()
    {
        var state = GetContainingInputManager()?.CurrentState;

        if (state == null)
            return KeyModifiers.None;

        var modifiers = KeyModifiers.None;

        if (state.IsPressed(Key.ControlLeft) || state.IsPressed(Key.ControlRight))
            modifiers |= KeyModifiers.Control;
        if (state.IsPressed(Key.ShiftLeft) || state.IsPressed(Key.ShiftRight))
            modifiers |= KeyModifiers.Shift;
        if (state.IsPressed(Key.AltLeft) || state.IsPressed(Key.AltRight))
            modifiers |= KeyModifiers.Alt;

        return modifiers;
    }

    public override bool OnHover(MouseEvent e)
    {
        mouse = ToLocalSpace(e.ScreenSpaceMousePosition);
        return true;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (drag == DragKind.None)
            mouse = null;

        return true;
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        mouse = ToLocalSpace(e.ScreenSpaceMousePosition);
        return base.OnMouseMove(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        var position = ToLocalSpace(e.ScreenSpaceMousePosition);
        mouse = position;

        var modifiers = currentModifiers();
        var under = noteAt(position);

        switch (e.Button)
        {
            // Middle-click, Shift+right-click, or right-click with a placement tool: delete.
            case MouseButton.Middle:
            case MouseButton.Right when (modifiers & KeyModifiers.Shift) != 0 || session.Tool.Value != ComposeTool.Select:
                if (under != null)
                    beatmap.RemoveRange(beatmap.IsSelected(under) ? beatmap.SelectedNotes.ToArray() : new[] { under });

                return true;

            case MouseButton.Left:
                break;

            default:
                return false;
        }

        if (session.Tool.Value != ComposeTool.Select)
        {
            drag = DragKind.Place;
            beginPlacement(position);
            base.OnMouseDown(e);
            return true;
        }

        bool add = platformModifier(modifiers);

        if (under == null)
        {
            if (!add)
                beatmap.ClearSelection();

            drag = DragKind.Box;
            boxStart = (timeAt(position.Y), position.X);
            boxAdds = add;
        }
        else if (add)
        {
            if (beatmap.IsSelected(under))
                beatmap.Deselect(under);
            else
                beatmap.Select(new[] { under }, true);

            drag = DragKind.None;
        }
        else
        {
            if (!beatmap.IsSelected(under))
                beatmap.Select(new[] { under });

            // Grabbing a selected hold by its tail changes where it ends; anywhere else moves the selection.
            if (under.Type == NoteType.Hold && hitTail(geometryOf(under), position) && beatmap.SelectedNotes.Count == 1)
            {
                drag = DragKind.Tail;
                tailDragged = under;
                beatmap.BeginChange();
            }
            else
            {
                drag = DragKind.Move;
                beginMove(position);
            }
        }

        base.OnMouseDown(e);
        return true;
    }

    public override bool OnDoubleClick(MouseButtonEvent e)
    {
        var under = noteAt(ToLocalSpace(e.ScreenSpaceMousePosition));

        if (under != null && session.Tool.Value == ComposeTool.Select)
            session.Clock.SeekSmoothlyTo(under.StartTime);

        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e) => true;

    public override bool OnDrag(MouseEvent e)
    {
        var position = ToLocalSpace(e.ScreenSpaceMousePosition);
        mouse = position;

        switch (drag)
        {
            case DragKind.Place:
                updatePlacement(position);
                break;

            case DragKind.Move:
                updateMove(position);
                break;

            case DragKind.Tail:
                updateTail(position);
                break;
        }

        return true;
    }

    public override bool OnDragEnd(MouseButtonEvent e)
    {
        finishDrag();
        return true;
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        bool handled = base.OnMouseUp(e);

        // A press that never became a drag still ends what it started.
        if (drag != DragKind.None)
            finishDrag();

        return handled;
    }

    private void finishDrag()
    {
        switch (drag)
        {
            case DragKind.Place:
                endPlacement();
                break;

            case DragKind.Box:
                finishBox();
                boxStart = null;
                break;

            case DragKind.Move:
                dragReference = null;
                dragStart.Clear();
                beatmap.EndChange();
                break;

            case DragKind.Tail:
                tailDragged = null;
                beatmap.EndChange();
                break;
        }

        drag = DragKind.None;
    }

    private float scrollAccumulated;

    public override bool OnScroll(ScrollEvent e)
    {
        var modifiers = currentModifiers();
        float delta = e.ScrollDelta.Y + e.ScrollDelta.X;

        if (platformModifier(modifiers))
        {
            if (delta > 0)
                session.Divisor.Next();
            else if (delta < 0)
                session.Divisor.Previous();

            return true;
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            session.TimelineSpan.Value = Math.Clamp(session.TimelineSpan.Value * (delta > 0 ? 0.9 : 1.1), EditorSession.MIN_TIMELINE_SPAN, EditorSession.MAX_TIMELINE_SPAN);
            return true;
        }

        // Trackpads send fractions; a step is taken per whole unit.
        scrollAccumulated += delta;
        double amount = (modifiers & KeyModifiers.Shift) != 0 ? 4 : 1;

        while (Math.Abs(scrollAccumulated) >= 1)
        {
            // Wheel up goes back in time.
            if (scrollAccumulated > 0)
            {
                session.Clock.SeekBackward(!session.Clock.IsRunning, amount);
                scrollAccumulated -= 1;
            }
            else
            {
                session.Clock.SeekForward(!session.Clock.IsRunning, amount);
                scrollAccumulated += 1;
            }
        }

        return true;
    }

    #endregion

    #region Context menu

    /// <summary>
    /// Right-click on the selection (select tool only; with a placement tool right-click deletes): what can
    /// be changed about the selected notes.
    /// </summary>
    public IReadOnlyList<MenuItem>? ContextMenuItems
    {
        get
        {
            if (session.Tool.Value != ComposeTool.Select)
                return null;

            // Where the right-click is now, rather than the last hover, which a panel over the field can clear.
            var manager = GetContainingInputManager();
            Vector2? at = manager != null ? ToLocalSpace(manager.CurrentState.MousePosition) : mouse;

            if (at is not Vector2 position)
                return null;

            var under = noteAt(position);

            if (under == null)
                return null;

            if (!beatmap.IsSelected(under))
                beatmap.Select(new[] { under });

            var selection = beatmap.SelectedNotes.ToArray();
            var items = new List<MenuItem>();

            if (selection.Any(n => n.Type == NoteType.Swipe))
            {
                items.Add(new MenuItem("Swipe direction", new MenuItem[]
                {
                    swipeItem("Either side", SwipeDirection.Any, selection),
                    swipeItem("Left", SwipeDirection.Left, selection),
                    swipeItem("Right", SwipeDirection.Right, selection)
                }));
            }

            if (selection.Any(n => n.Type == NoteType.Skill))
            {
                items.Add(new MenuItem("Skill", new MenuItem[]
                {
                    skillItem("Heal", SkillType.Heal, selection),
                    skillItem("Break -> Critical", SkillType.BreakToCritical, selection)
                }));
            }

            items.Add(new MenuItem("Turn into", new[]
            {
                typeItem("Tap", NoteType.Tap, selection),
                typeItem("Pass", NoteType.Pass, selection),
                typeItem("Touch", NoteType.Touch, selection),
                typeItem("Skill", NoteType.Skill, selection),
                typeItem("Swipe", NoteType.Swipe, selection)
            }));

            items.Add(new MenuItem("Hitsounds", new MenuItem[]
            {
                additionItem("Whistle", HitSampleAddition.Whistle, selection),
                additionItem("Finish", HitSampleAddition.Finish, selection),
                additionItem("Clap", HitSampleAddition.Clap, selection)
            }));

            items.Add(new MenuItem("Bank", new MenuItem[]
            {
                bankItem("Normal", SampleBank.Normal, selection),
                bankItem("Soft", SampleBank.Soft, selection),
                bankItem("Drum", SampleBank.Drum, selection)
            }));

            items.Add(new MenuSeparator());
            items.Add(new MenuItem("Delete", () => beatmap.RemoveRange(selection)) { ShortcutText = "Delete" });

            return items;
        }
    }

    private MenuItem swipeItem(string text, SwipeDirection direction, BeatmapNote[] selection) =>
        new MenuItem(text, () => beatmap.Update(selection.Where(n => n.Type == NoteType.Swipe), n => n.Direction = direction));

    private MenuItem skillItem(string text, SkillType skill, BeatmapNote[] selection) =>
        new MenuItem(text, () => beatmap.Update(selection.Where(n => n.Type == NoteType.Skill), n => n.Skill = skill));

    private MenuItem bankItem(string text, SampleBank bank, BeatmapNote[] selection) =>
        new MenuItem(text, () => beatmap.Update(selection, n => n.Bank = bank));

    private MenuItem additionItem(string text, HitSampleAddition addition, BeatmapNote[] selection)
    {
        bool all = selection.All(n => n.Additions.HasFlag(addition));
        var item = new ToggleMenuItem(text, all);

        item.State.ValueChanged += e => beatmap.Update(selection, n => n.Additions = e.NewValue ? n.Additions | addition : n.Additions & ~addition);
        return item;
    }

    /// <summary>
    /// Changes the selected tap-like notes' type. Holds keep being holds (they have a length the others
    /// can't carry).
    /// </summary>
    private MenuItem typeItem(string text, NoteType type, BeatmapNote[] selection) =>
        new MenuItem(text, () => beatmap.Update(selection.Where(n => n.Type != NoteType.Hold), n => n.Type = type));

    #endregion
}
