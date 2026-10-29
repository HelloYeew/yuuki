// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Text in the Hero Club rainbow, moving: a Full Bloom's SS and its FULL BLOOM cut-in. Each letter takes the
/// rainbow's color where it stands, as if the rainbow (<see cref="RainbowColors.PATTERN_WIDTH"/> times the text's width)
/// slid along behind the letters.
/// </summary>
/// <remarks>
/// A letter is one color: text draws its glyphs in a single color, so the rainbow steps from letter to letter rather
/// than running across each one. The letters are their own drawables (<see cref="Letters"/>), so they can be stamped
/// in one by one.
/// </remarks>
public partial class RainbowText : FlowContainer
{
    private readonly List<SpriteText> letters = new List<SpriteText>();

    private string text = string.Empty;
    private FontUsage font;

    /// <summary>
    /// The colors it runs through: <see cref="RainbowColors.INK"/> by default, for letters on a light surface.
    /// </summary>
    public Color[] Stops { get; set; } = RainbowColors.INK;

    /// <summary>
    /// How fast it moves, as a multiple of one run per <see cref="RainbowColors.PERIOD"/>; 0 holds it still.
    /// </summary>
    public double Speed { get; set; } = 1;

    /// <summary>
    /// When it started moving, on its own clock; it starts when first shown unless set.
    /// </summary>
    public double StartTime { get; set; } = double.NaN;

    /// <summary>
    /// The letters, in order, each its own text.
    /// </summary>
    public IReadOnlyList<SpriteText> Letters => letters;

    public RainbowText()
    {
        Direction = FlowDirection.Horizontal;
        AutoSizeAxes = Axes.Both;
    }

    public string Text
    {
        get => text;
        set
        {
            if (text == value)
                return;

            text = value;
            rebuild();
        }
    }

    public FontUsage Font
    {
        get => font;
        set
        {
            font = value;

            foreach (SpriteText letter in letters)
                letter.Font = value;
        }
    }

    public override bool HandlePositionalInput => false;

    private void rebuild()
    {
        Clear();
        letters.Clear();

        foreach (char c in text)
        {
            var letter = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = c.ToString(),
                Font = font
            };

            letters.Add(letter);
            Add(letter);
        }
    }

    public override void Update()
    {
        base.Update();

        if (double.IsNaN(StartTime))
            StartTime = Clock.CurrentTime;

        float width = DrawWidth;
        if (width <= 0)
            return;

        double pattern = width * RainbowColors.PATTERN_WIDTH;
        double offset = RainbowColors.Phase(Clock.CurrentTime - StartTime, Speed) * pattern;

        foreach (SpriteText letter in letters)
            letter.Color = RainbowColors.Sample(Stops, (letter.X + letter.DrawWidth / 2 + offset) / pattern);
    }
}
