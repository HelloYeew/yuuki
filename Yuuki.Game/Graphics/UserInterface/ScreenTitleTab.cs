// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A screen's title in a slanted tab at the top left, run out past the screen's edge, as the settings' and the play
/// menu's. Slides in from the left (<see cref="SlideIn"/>), and is smaller and higher on a phone, as theirs are.
/// </summary>
public partial class ScreenTitleTab : Container
{
    private const float height = 100;

    // Past the title on either side: room for the slant, and for the part that's past the screen's edge.
    private const float title_margin = 70;

    // Where it sits shown, and where it slides from.
    private const float shown_x = -40;
    private const float hidden_x = -200;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    /// <summary>
    /// How far down the screen it reaches: what's under it starts below this.
    /// </summary>
    public float Bottom => Y + height * Scale.Y;

    public ScreenTitleTab(string title, YuukiColorPalette palette)
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        AutoSizeAxes = Axes.X;
        Height = height;
        X = hidden_x;
        Alpha = 0;

        Children = new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Shear = UserInterfaceConstant.Shear,
                CornerRadius = 5,
                // Its slanted end goes past the screen's edge, under a notch or camera cutout too.
                Bleed = Edges.Left,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Surface
                }
            },
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Left = title_margin, Right = title_margin },
                Text = title,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 56, FontWeight.Bold),
                Color = palette.Text
            }
        };
    }

    public override void Update()
    {
        // Before the base, which works out where it's drawn from where it is.
        layOut();
        base.Update();
    }

    // Placed first: a slide moves the whole position, holding the height it starts at, so a tab not yet placed
    // would slide in along the top and only drop to its place once it got there.
    public void SlideIn()
    {
        layOut();
        this.MoveToX(shown_x, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
    }

    public void SlideOut()
    {
        layOut();
        this.MoveToX(hidden_x, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
    }

    // As the settings' tab: a phone's screen is short, and has less to spare for it.
    private void layOut()
    {
        bool phone = gameLayout?.IsPhone == true;
        Scale = new Vector2(phone ? 0.75f : 1);
        Y = phone ? 28 : 48;
    }
}
