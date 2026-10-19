// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Drawables;

public partial class FooterKeybindItem : FlowContainer
{
    public string KeybindName { get; private set; }
    public Action? Action { get; private set; }
    public KeyCombination KeyCombination { get; private set; }
    private Box backgroundBox = null!;
    private SpriteText keybindText = null!;
    private Container mainContainer = null!;
    private static readonly Color default_background_color = ColorExtensions.FromHex("#C8645C");

    public FooterKeybindItem(string keybindName, Action? action, KeyCombination keyCombination)
    {
        KeybindName = keybindName;
        Action = action;
        KeyCombination = keyCombination;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.CentreLeft;
        AutoSizeAxes = Axes.Both;
        Spacing = new Vector2(8, 0);
        Children = new Drawable[]
        {
            // The key cap fits its label, with 7.5px either side.
            mainContainer = new Container()
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.X,
                Height = 30,
                Children = new Drawable[]
                {
                    // On the key, not the whole item: the item is a flow, which would lay this out as an entry.
                    new HoverClickSounds(),
                    new Container()
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        Shear = new Vector2(-0.33f, 0),
                        CornerRadius = 4f,
                        Child = backgroundBox = new Box()
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Color = default_background_color
                        }
                    },
                    keybindText = new SpriteText()
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Margin = new MarginPadding { Left = 7.5f, Right = 7.5f },
                        Text = KeyCombination.ToString(),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                        Color = ColorExtensions.FromHex("#E4C4A4")
                    }
                }
            },
            new SpriteText()
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = KeybindName,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20),
                Color = Color.White
            }
        };
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        Action?.Invoke();
        backgroundBox.FlashColor(default_background_color.Lighten(0.75f), 250, Easing.OutQuint);

        return base.OnClick(e);
    }
}
