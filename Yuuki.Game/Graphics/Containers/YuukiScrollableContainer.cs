// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.Containers;

public partial class YuukiScrollableContainer : ScrollableContainer
{
    public YuukiScrollableContainer()
    {
        AutoHideScrollbars = true;
        InactiveDuration = 1000;
        ScrollbarFadeDuration = 300;
    }

    protected override Drawable CreateVerticalScrollbar() => new YuukiScrollbar(vertical: true);

    protected override Drawable CreateHorizontalScrollbar() => new YuukiScrollbar(vertical: false);

    private partial class YuukiScrollbar : Container
    {
        private const float rest_thickness = 4;
        private const float hover_thickness = 8;
        private const float rest_alpha = 0.55f;
        private const double duration = 200;

        private readonly bool vertical;
        private readonly Container bar;
        private readonly Box fill;

        private bool hovered;

        public YuukiScrollbar(bool vertical)
        {
            this.vertical = vertical;

            RelativeSizeAxes = Axes.Both;

            Child = bar = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = vertical ? Axes.Y : Axes.X,
                Masking = true,
                CornerRadius = rest_thickness / 2,
                Alpha = rest_alpha,
                Child = fill = new Box
                {
                    RelativeSizeAxes = Axes.Both
                }
            };

            setThickness(rest_thickness);
        }

        [BackgroundDependencyLoader]
        private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
        {
            YuukiColorPalette palette = ScreenPalette.Resolve(screenPalette, colorProvider);

            fill.Color = palette.Control;
        }

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateState();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateState();
            return base.OnHoverLost(e);
        }

        private void updateState()
        {
            bar.FadeTo(hovered ? 1 : rest_alpha, duration, Easing.OutQuint);
            setThickness(hovered ? hover_thickness : rest_thickness);
        }

        private void setThickness(float thickness)
        {
            bar.CornerRadius = thickness / 2;

            // The long axis follows the slot; only the short one is set.
            if (vertical)
                bar.Width = thickness;
            else
                bar.Height = thickness;
        }
    }
}
