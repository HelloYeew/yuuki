// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Text in the Hero Club rainbow, moving: a Bloom's word in the song. The rainbow (<see cref="RainbowColors.PATTERN_WIDTH"/>
/// times the text's width) slides along behind the letters, its colors running across each one.
/// </summary>
public partial class RainbowText : SpriteText
{
    private double pattern;
    private double offset;

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

    public RainbowText()
    {
        ColorAt = colorAt;
    }

    public override bool HandlePositionalInput => false;

    public override void Update()
    {
        if (double.IsNaN(StartTime))
            StartTime = Clock.CurrentTime;

        pattern = ContentSize.X * RainbowColors.PATTERN_WIDTH;
        offset = RainbowColors.Phase(Clock.CurrentTime - StartTime, Speed) * pattern;

        // Before its own update, so this frame's colors are what's drawn.
        if (!IsHidden)
            Invalidate(InvalidationFlags.Color);

        base.Update();
    }

    private Color colorAt(Vector2 at) => pattern > 0 ? RainbowColors.Sample(Stops, (at.X + offset) / pattern) : Stops[0];
}
