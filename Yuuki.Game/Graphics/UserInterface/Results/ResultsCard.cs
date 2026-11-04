// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// One of the results' More info cards: a panel with what it shows and the question it answers along its top (and room
/// at the right of that for its controls), and its content under them. While what it needs isn't in yet
/// (<see cref="Waiting"/>), its content waits under a shimmer.
/// </summary>
public partial class ResultsCard : Container
{
    private const float padding = 24;
    private const float head_height = 30;
    private const float question_gap = 14;

    private readonly Container body;
    private readonly FlowContainer shimmer;
    private readonly Box[] shimmerBars = new Box[3];
    private readonly SpriteText question;
    private readonly SpriteText titleText;
    private readonly bool stacked;

    private bool waiting;
    private float waitingShown;

    /// <summary>
    /// The row along its top, right of the title: its controls.
    /// </summary>
    public FlowContainer Controls { get; }

    /// <summary>
    /// What's under its title.
    /// </summary>
    public Container Body => body;

    /// <summary>
    /// The question it answers, by its title.
    /// </summary>
    public string Question
    {
        get => question.Text;
        set => question.Text = value;
    }

    /// <summary>
    /// Whether it's waiting for what it shows: its content hides, and a shimmer shows in its place.
    /// </summary>
    public bool Waiting
    {
        get => waiting;
        set => waiting = value;
    }

    /// <param name="title">What it shows.</param>
    /// <param name="question">The question that answers.</param>
    /// <param name="palette">Its colors.</param>
    /// <param name="stacked">The question under the title rather than beside it (a narrow card).</param>
    public ResultsCard(string title, string question, YuukiColorPalette palette, bool stacked = false)
    {
        this.stacked = stacked;

        Masking = true;
        CornerRadius = 12;

        titleText = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = title.ToUpperInvariant(),
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 15, FontWeight.Bold),
            Color = ResultsInk.Label(palette)
        };

        this.question = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = question,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, stacked ? 15 : 16, FontWeight.SemiBold),
            Color = ResultsInk.Secondary(palette)
        };

        float bodyTop = stacked ? 58 : 16 + head_height + 10;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Surface,
                Alpha = 0.92f
            },
            stacked
                ? new FlowContainer
                {
                    Position = new Vector2(padding, 16),
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Children = new Drawable[] { titleText, this.question }
                }
                : new FlowContainer
                {
                    Position = new Vector2(padding, 16),
                    Height = head_height,
                    AutoSizeAxes = Axes.X,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(question_gap, 0),
                    Children = new Drawable[] { titleText, this.question }
                },
            Controls = new FlowContainer
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-padding, 16),
                Height = head_height,
                AutoSizeAxes = Axes.X,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(6, 0)
            },
            body = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = bodyTop, Left = padding, Right = padding, Bottom = 16 }
            },
            // Three lines of what's to come, glinting in turn.
            shimmer = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Top = bodyTop + 18, Left = padding, Right = padding },
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 20),
                Alpha = 0,
                Children = new[] { shimmerBar(palette, 1, 0), shimmerBar(palette, 0.86f, 1), shimmerBar(palette, 0.64f, 2) }
            }
        };
    }

    private Drawable shimmerBar(YuukiColorPalette palette, float width, int index) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Width = width,
        Height = 14,
        Masking = true,
        CornerRadius = 7,
        Child = shimmerBars[index] = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = palette.Text
        }
    };

    public override void Update()
    {
        // The question gives way where the controls need its room: a narrow window, or a lot of them.
        if (!stacked)
            question.Alpha = padding + titleText.DrawWidth + question_gap + question.DrawWidth + padding > DrawWidth - padding - Controls.DrawWidth ? 0 : 1;

        base.Update();

        // In and out over 300 ms, as the content comes and goes.
        float target = waiting ? 1 : 0;
        float step = (float)(Clock.ElapsedFrameTime / 300);
        waitingShown = waitingShown < target ? Math.Min(target, waitingShown + step) : Math.Max(target, waitingShown - step);

        body.Alpha = snap(1 - waitingShown);
        shimmer.Alpha = snap(waitingShown);

        if (waitingShown <= 0)
            return;

        // The glint runs down the lines and round again every 1.1 s.
        double phase = Clock.CurrentTime / 1100 % 1;

        for (int i = 0; i < shimmerBars.Length; i++)
        {
            float glint = MathF.Max(0, 1 - MathF.Abs((float)(phase * 1.6 - i * 0.3 - 0.2)) * 3);
            shimmerBars[i].Alpha = 0.07f + 0.1f * glint;
        }
    }

    // Eased alphas end on 0 or 1 exactly: the setter skips changes too small to see, which would leave it a hair off.
    private static float snap(float alpha) => alpha < 0.001f ? 0 : alpha > 0.999f ? 1 : alpha;
}
