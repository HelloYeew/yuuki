// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Loading spinner spin right now right right now
/// </summary>
public partial class LoadingSpinner : Container
{
    private const float turn_speed = 0.45f;

    private readonly RingArc arc;

    public LoadingSpinner(Color color, float size = 28)
    {
        Size = new Vector2(size);

        Child = arc = new RingArc
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            InnerRadius = 0.72f,
            Progress = 0.3f,
            Color = color
        };
    }

    public override void Update()
    {
        base.Update();

        double time = Clock.CurrentTime;
        arc.Rotation = (float)(time * turn_speed % 360);

        // The arc grows and shrinks a little as it turns, so it reads as busy rather than stuck.
        arc.Progress = 0.2f + 0.15f * (0.5f + 0.5f * System.MathF.Sin((float)(time / 260)));
    }
}
