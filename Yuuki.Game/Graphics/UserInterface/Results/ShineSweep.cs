// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A glint of light sweeping once across its parent, left to right, as on a stamp just landed: a soft white band, brightest
/// in its middle. Put it in a masked parent, which keeps it to its shape (a sheared one slants it with it).
/// </summary>
/// <remarks>
/// It sweeps when told to (<see cref="Sweep"/>), on its own clock, so a time already past shows it as it would be by now,
/// and again every <see cref="Every"/> ms if that's set. Its height is its parent's.
/// </remarks>
public partial class ShineSweep : Container
{
    /// <summary>
    /// How long a sweep takes, in ms.
    /// </summary>
    public const double DURATION = 420;

    private double sweepsAt = double.PositiveInfinity;

    /// <summary>
    /// How wide the band is, in the parent's units.
    /// </summary>
    public float BandWidth { get; set; } = 160;

    /// <summary>
    /// How bright its middle is, 0–1.
    /// </summary>
    public float Brightness { get; set; } = 0.5f;

    /// <summary>
    /// After the first sweep, another every this many ms (from the start of one to the start of the next); 0 for none.
    /// </summary>
    public double Every { get; set; }

    public ShineSweep()
    {
        RelativeSizeAxes = Axes.Y;
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.Centre;
        // Past the parent's top and bottom, so a slanted parent's corners are crossed too.
        Height = 1.24f;
        // Unseen at first and only ever shown by its alpha: kept present so it's drawn properly once it is.
        Alpha = 0;
        AlwaysPresent = true;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                ColorInfo = ColorInfo.GradientHorizontal(Color.White.WithAlpha(0), Color.White)
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                RelativePositionAxes = Axes.X,
                X = 0.5f,
                ColorInfo = ColorInfo.GradientHorizontal(Color.White, Color.White.WithAlpha(0))
            }
        };
    }

    /// <summary>
    /// Sweeps from <paramref name="time"/> (now if not given).
    /// </summary>
    public void Sweep(double? time = null) => sweepsAt = time ?? Clock.CurrentTime;

    /// <summary>
    /// No more sweeps.
    /// </summary>
    public void Stop() => sweepsAt = double.PositiveInfinity;

    public override void Update()
    {
        base.Update();

        double since = Clock.CurrentTime - sweepsAt;

        if (since >= 0 && Every > 0)
            since %= Every;

        if (!(since >= 0 && since <= DURATION) || Parent == null)
        {
            Alpha = 0;
            return;
        }

        float width = Parent.DrawSize.X;
        float progress = (float)UserInterfaceConstant.GlintSweep.Apply(since / DURATION);

        Width = BandWidth;
        X = -BandWidth + progress * (width + 2 * BandWidth);
        Alpha = Brightness * Math.Min(1, (float)(since / 84));
    }
}
