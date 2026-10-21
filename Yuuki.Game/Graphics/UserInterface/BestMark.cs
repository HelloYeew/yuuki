// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Your best result on a song or difficulty as a small outlined mark ("S", "A·FC", "AP"), hidden when you
/// haven't cleared it.
/// </summary>
public partial class BestMark : Container
{
    private readonly SpriteText text;

    public BestMark(float textSize = 12.5f)
    {
        AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = textSize * 0.48f;
        BorderThickness = 1.5f;
        Alpha = 0;

        Child = text = new SpriteText
        {
            Margin = new MarginPadding { Left = textSize * 0.45f, Right = textSize * 0.45f, Top = textSize * 0.3f, Bottom = textSize * 0.3f },
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)textSize, FontWeight.ExtraBold)
        };
    }

    public new Color Color
    {
        get => text.Color;
        set
        {
            text.Color = value;
            BorderColor = value;
        }
    }

    /// <summary>
    /// The mark for <paramref name="best"/>: AP beats everything, then the rank with FC added.
    /// </summary>
    public static string TextFor(ScoreInfo? best)
    {
        if (best == null)
            return string.Empty;

        if (best.IsAllPerfect)
            return "AP";

        return best.IsFullCombo ? $"{best.Rank}·FC" : best.Rank.ToString();
    }

    public void SetBest(ScoreInfo? best)
    {
        text.Text = TextFor(best);
        Alpha = best == null ? 0 : 0.8f;
    }
}
