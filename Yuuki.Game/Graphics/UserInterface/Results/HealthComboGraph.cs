// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

public partial class HealthComboGraph : SongTimeGraph
{
    public const float HEIGHT = 128;

    private const float top = 8;
    private const float bottom = 100;

    /// <summary>
    /// The combo's color, behind the health line: the full combo's cyan, as the badge says it. The lilac it was had no
    /// lightness against the card (1.01:1) and all but vanished.
    /// </summary>
    public static readonly Color COMBO = JudgementColors.FULL_COMBO;

    /// <summary>
    /// The combo's top edge, deep enough to read as a line on the card (about 3:1).
    /// </summary>
    public static readonly Color COMBO_EDGE = JudgementColors.FULL_COMBO.Darken(0.45f);

    protected override float PlotTop => top;

    protected override float PlotBottom => bottom;

    protected override string FailLabel => "health ran out";

    protected override bool SaysForTheOthers => false;

    public HealthComboGraph(YuukiColorPalette palette)
        : base(palette, HEIGHT)
    {
    }

    private static float yOf(double health) => bottom - (float)health * (bottom - top);

    protected override void Plot()
    {
        PlayAnalysis run = Run!;
        float left = XOf(0), right = XOf(Length);

        SideLabel("100%", yOf(1));
        SideLabel("50%", yOf(0.5));
        SideLabel("0%", yOf(0));
        Backdrop.Line(new Vector2(PlotLeft, yOf(0.5)), new Vector2(PlotRight, yOf(0.5)), 1, Palette.Text.WithAlpha(0.2f), 5, 6);

        // The combo, behind: it climbs a step at each hit and drops to nothing where it broke.
        float max = System.Math.Max(1, run.MaxCombo);
        var combo = new List<Vector2> { new Vector2(left, bottom) };
        float previous = bottom;

        foreach ((double time, int count) in run.Combo)
        {
            float x = XOf(time), y = bottom - count / max * (bottom - top) * 0.92f;
            combo.Add(new Vector2(x, previous));
            combo.Add(new Vector2(x, y));
            previous = y;
        }

        combo.Add(new Vector2(right, previous));
        Marks.Area(combo, bottom, COMBO.WithAlpha(0.45f));

        // Its steps, drawn along its top so its shape reads (not down to the baseline at its ends).
        Marks.Line(combo.Skip(1).ToList(), 1.5f, COMBO_EDGE.WithAlpha(0.9f));

        // Health over it.
        Marks.Line(run.Health.Select(h => new Vector2(XOf(h.Time), yOf(h.Health))).Append(new Vector2(right, yOf(run.Health[^1].Health))).ToList(), 2.6f, Palette.Text);

        // Where it broke: a line up through it, and a mark at its foot.
        foreach (double broke in run.ComboBreaks)
        {
            float x = XOf(broke);
            Marks.Line(new Vector2(x, top), new Vector2(x, bottom), 1.5f, JudgementColors.MISS.WithAlpha(0.35f));
            Marks.Triangle(new Vector2(x - 5, bottom + 3), new Vector2(x + 5, bottom + 3), new Vector2(x, bottom - 5), JudgementColors.MISS);
        }
    }

    protected override (string Text, Color? Color, Vector2 At, double Time)? Describe(Vector2 at)
    {
        PlayAnalysis run = Run!;
        double time = TimeAt(at.X);
        double health = 1;
        int combo = 0;

        foreach ((double t, double h) in run.Health)
        {
            if (t > time)
                break;

            health = h;
        }

        foreach ((double t, int c) in run.Combo)
        {
            if (t > time)
                break;

            combo = c;
        }

        return ($"{formatTime(time)} · health {health * 100:0}% · combo {combo}", null, new Vector2(at.X, top + 2), time);
    }
}
