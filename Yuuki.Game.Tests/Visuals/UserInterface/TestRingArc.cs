// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestRingArc : YuukiTestScene
{
    private Container zoom = null!;
    private RingArc plain = null!;
    private RingArc smooth = null!;
    private RingArc thinPlain = null!;
    private RingArc thinSmooth = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show rings", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(zoom = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Child = new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(60, 0),
                    Children = new Drawable[]
                    {
                        column("Plain", plain = arc(false, 0.86f), thinPlain = arc(false, 0.97f)),
                        column("Smooth", smooth = arc(true, 0.86f), thinSmooth = arc(true, 0.97f))
                    }
                }
            });
        });
    }

    [Test]
    public void TestEdges()
    {
        AddSliderStep("zoom", 0.5f, 6f, 1f, scale => zoom.Scale = new Vector2(scale));
        AddSliderStep("start", 0f, 1f, 0.1f, start =>
        {
            foreach (RingArc ring in new[] { plain, smooth, thinPlain, thinSmooth })
                ring.Start = start;
        });
        AddSliderStep("end", 0f, 1f, 0.82f, end =>
        {
            foreach (RingArc ring in new[] { plain, smooth, thinPlain, thinSmooth })
                ring.Progress = end;
        });
        AddSliderStep("thickness (inner radius)", 0f, 0.995f, 0.86f, inner =>
        {
            plain.InnerRadius = inner;
            smooth.InnerRadius = inner;
        });
    }

    /// <summary>
    /// The thick smooth ring, told to start where it ends and then past it, is gone (nothing to draw) until it's given
    /// an arc again.
    /// </summary>
    [Test]
    public void TestAnArcWithNothingToDraw()
    {
        AddStep("start where it ends", () =>
        {
            smooth.Start = 0.5f;
            smooth.Progress = 0.5f;
        });
        AddStep("start past where it ends", () => smooth.Start = 0.7f);
        AddWaitStep("draws nothing, and doesn't throw", 500);
        AddStep("an arc again", () =>
        {
            smooth.Start = 0.1f;
            smooth.Progress = 0.82f;
        });
    }

    private static RingArc arc(bool smoothEdges, float innerRadius) => new RingArc
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Size = new Vector2(300),
        Start = 0.1f,
        Progress = 0.82f,
        InnerRadius = innerRadius,
        SmoothEdges = smoothEdges,
        Color = HeroColors.ITSUKI.Main
    };

    private static Drawable column(string title, RingArc thick, RingArc thin) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 20),
        Children = new Drawable[]
        {
            new SpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = title,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.Bold),
                Color = Color.White
            },
            thick,
            thin
        }
    };
}
