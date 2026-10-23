// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Gameplay.HUD;

/// <summary>
/// The menu over a stopped run — "Paused" (continue / retry / quit) or "Failed" (retry / quit)
/// </summary>
public partial class GameplayMenuOverlay : Container
{
    private const float title_gap = 10;
    private const float buttons_gap = 38;

    private readonly List<DialogButton> buttons = new List<DialogButton>();

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    private readonly FlowContainer content;
    private readonly FlowContainer buttonFlow;

    private int selected;

    /// <summary>
    /// Shown under the buttons, as the song's offset is while paused. Set before load.
    /// </summary>
    public Drawable? Footer
    {
        init
        {
            if (value == null)
                return;

            value.Anchor = Anchor.TopCentre;
            value.Origin = Anchor.TopCentre;
            value.Margin = new MarginPadding { Top = buttons_gap };
            content.Add(value);
        }
    }

    public GameplayMenuOverlay(string title, string subtitle, YuukiColorPalette palette, params (IconUsage Icon, string Label, Action Action)[] options)
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Bleed = Edges.All,
                Color = palette.Backdrop,
                Alpha = 0.75f
            },
            // Title, subtitle and buttons, centred on each other.
            content = new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = title.ToUpperInvariant(),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 72, FontWeight.Bold),
                        Color = palette.TextLight
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = subtitle,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.Regular),
                        Color = palette.TextLight,
                        Margin = new MarginPadding { Top = title_gap }
                    },
                    buttonFlow = new FlowContainer
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Margin = new MarginPadding { Top = buttons_gap },
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 14)
                    }
                }
            }
        };

        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            (IconUsage icon, string label, Action action) = options[i];

            var button = new DialogButton(icon, label, palette)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Action = action,
                Hovered = () => select(index)
            };

            buttons.Add(button);
            buttonFlow.Add(button);
        }
    }

    /// <summary>
    /// Fades the menu in with the first option selected, the buttons settling in one by one.
    /// </summary>
    public override void Show()
    {
        uiSounds?.Play(UISound.OverlayOpen);
        select(0);
        this.FadeIn(200, Easing.OutQuint);

        for (int i = 0; i < buttons.Count; i++)
            buttons[i].SlideIn(i * 60);
    }

    public override void Hide()
    {
        if (IsShown)
            uiSounds?.Play(UISound.OverlayClose);

        this.FadeOut(200, Easing.OutQuint);
    }

    public bool IsShown => Alpha > 0;

    public override void Update()
    {
        base.Update();

        // One even column, wide enough for the longest answer.
        DialogButton.FitColumn(buttons, Math.Max(100, (Parent?.DrawWidth ?? 1920) - 120));
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!IsShown || e.IsRepeat)
            return false;

        switch (e.Key)
        {
            case Key.Up:
                select((selected + buttons.Count - 1) % buttons.Count);
                uiSounds?.Play(UISound.Hover);
                return true;

            case Key.Down:
                select((selected + 1) % buttons.Count);
                uiSounds?.Play(UISound.Hover);
                return true;

            case Key.Enter:
                buttons[selected].Activate();
                return true;
        }

        return base.OnKeyDown(e);
    }

    private void select(int index)
    {
        selected = index;

        for (int i = 0; i < buttons.Count; i++)
            buttons[i].Selected = i == index;
    }
}
