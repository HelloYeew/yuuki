// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit.Components;

namespace Yuuki.Game.Edit.Compose;

/// <summary>
/// Where each part of a note is in the composition playfield, in its local pixels.
/// </summary>
public readonly record struct NoteGeometry(RectangleF Head, RectangleF? Tail, Vector2[]? Body, float BodyWidth)
{
    /// <summary>
    /// Everything the note covers, for culling and hit-testing a straight hold's body.
    /// </summary>
    public RectangleF Bounds
    {
        get
        {
            float left = Head.X, top = Head.Y, right = Head.X + Head.Width, bottom = Head.Y + Head.Height;

            if (Tail is { } tail)
            {
                left = System.Math.Min(left, tail.X);
                top = System.Math.Min(top, tail.Y);
                right = System.Math.Max(right, tail.X + tail.Width);
                bottom = System.Math.Max(bottom, tail.Y + tail.Height);
            }

            return new RectangleF(left, top, right - left, bottom - top);
        }
    }
}

/// <summary>
/// One note in the compose playfield, drawn the way the game draws it, flattened: a bar in its lane, a
/// hollow ring for a pass note, a thin bar for a touch note, a full-width bar with arrows for a swipe, an
/// icon on a skill note, and a body running to the tail for a hold (drifting across lanes for a
/// lane-changing one). Laid out by the playfield every frame; takes no input itself.
/// </summary>
public partial class EditorNotePiece : Container
{
    public BeatmapNote? Note { get; private set; }

    private readonly QuadShape body;
    private readonly Container tail;
    private readonly Box tailFill;
    private readonly Container head;
    private readonly Box headFill;
    private readonly IconSprite icon;

    public override bool HandlePositionalInput => false;

    public EditorNotePiece()
    {
        RelativeSizeAxes = Axes.Both;
        Size = Vector2.One;

        Children = new Drawable[]
        {
            body = new QuadShape { RelativeSizeAxes = Axes.Both, Size = Vector2.One },
            tail = new Container
            {
                Masking = true,
                CornerRadius = 3,
                Child = tailFill = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One }
            },
            head = new Container
            {
                Masking = true,
                CornerRadius = 4,
                Child = headFill = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One }
            },
            icon = new IconSprite
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                IconSize = 14,
                Color = Color.White
            }
        };
    }

    public void Apply(BeatmapNote note) => Note = note;

    /// <summary>
    /// Places and colours the parts. <paramref name="ghost"/> is a placement preview.
    /// </summary>
    public void Layout(NoteGeometry geometry, Color color, bool selected, bool hovered, bool ghost = false)
    {
        if (Note == null)
            return;

        bool pass = Note.Type == NoteType.Pass;

        head.Position = new Vector2(geometry.Head.X, geometry.Head.Y);
        head.Size = new Vector2(geometry.Head.Width, geometry.Head.Height);
        headFill.Color = color;
        headFill.Alpha = pass ? 0.12f : 1;

        // A pass note is the ring alone; selection and hover are rings in their own colours on top.
        head.BorderThickness = selected ? 3 : pass ? 3 : hovered ? 2 : 0;
        head.BorderColor = selected ? ColorExtensions.FromHex("ffcc22") : pass ? color : Color.White;

        if (geometry.Body is Vector2[] corners)
        {
            body.Alpha = ghost ? 0.35f : 0.55f;
            body.Color = selected ? color.Lighten(0.25f) : color;
            body.SetCorners(corners[0], corners[1], corners[2], corners[3]);
        }
        else
        {
            body.Alpha = 0;
        }

        if (geometry.Tail is RectangleF tailRect)
        {
            tail.Alpha = 1;
            tail.Position = new Vector2(tailRect.X, tailRect.Y);
            tail.Size = new Vector2(tailRect.Width, tailRect.Height);
            tailFill.Color = color.Lighten(0.2f);
            tail.BorderThickness = selected ? 2 : 0;
            tail.BorderColor = ColorExtensions.FromHex("ffcc22");
        }
        else
        {
            tail.Alpha = 0;
        }

        var iconUsage = iconFor(Note);
        icon.Alpha = iconUsage.HasValue ? 1 : 0;

        if (iconUsage is IconUsage usage)
        {
            icon.Icon = usage;
            icon.Position = new Vector2(geometry.Head.X + geometry.Head.Width / 2, geometry.Head.Y + geometry.Head.Height / 2);
        }

        Alpha = ghost ? 0.5f : 1;
    }

    private static IconUsage? iconFor(BeatmapNote note) => note.Type switch
    {
        NoteType.Swipe => note.Direction switch
        {
            SwipeDirection.Left => IconUsage.KeyboardDoubleArrowLeft,
            SwipeDirection.Right => IconUsage.KeyboardDoubleArrowRight,
            _ => IconUsage.SwapHoriz
        },
        NoteType.Skill => note.Skill == SkillType.Heal ? IconUsage.Favorite : IconUsage.AutoAwesome,
        _ => null
    };
}
