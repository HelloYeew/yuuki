// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.UserInterface.Dialog;

/// <summary>One of a <see cref="PopupDialog"/>'s answers.</summary>
/// <param name="IsCancel">Whether Escape (or a click outside the dialog) takes this answer.</param>
/// <param name="IsDangerous">Whether it can't be taken back, so the mouse has to hold it and the dialog never takes it for you.</param>
public record PopupDialogButton(IconUsage Icon, string Text, Action? Action, bool IsCancel = false, bool IsDangerous = false);

public partial class PopupDialog : Container
{
    private const float icon_size = 96;
    private const float header_gap = 20;
    private const float body_gap = 8;
    private const float buttons_gap = 38;

    private const float button_min_width = 300;
    private const float button_padding = 40;

    private readonly IconUsage icon;
    private readonly string header;
    private readonly string body;
    private readonly PopupDialogButton[] answers;

    private readonly List<DialogButton> buttons = new List<DialogButton>();

    private Container iconCircle = null!;
    private SpriteText headerText = null!;
    private SpriteText bodyText = null!;
    private FlowContainer buttonFlow = null!;

    private int selected;
    private bool answered;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    /// <summary>
    /// Called once an answer is taken, for the overlay to put the dialog away.
    /// </summary>
    internal Action? Dismissed;

    public IReadOnlyList<PopupDialogButton> Answers => answers;

    protected PopupDialog(IconUsage icon, string header, string body, params PopupDialogButton[] answers)
    {
        this.icon = icon;
        this.header = header;
        this.body = body;
        this.answers = answers;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        YuukiColorPalette palette = colorProvider.IsNotNull()
            ? colorProvider!.GetPalette(YuukiScreenPart.MainMenu)
            : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.MainMenu);

        // Stacked by hand in Update rather than by a flow: a header or line too long for the window
        // shrinks (Scale) to fit, and a flow's slots don't shrink with it.
        Children = new Drawable[]
        {
            iconCircle = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
                Size = new Vector2(icon_size),
                Y = icon_size / 2,
                Masking = true,
                CornerRadius = icon_size / 2,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Accent
                    },
                    new IconSprite
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = icon,
                        IconSize = icon_size * 0.55f,
                        Filled = true,
                        Color = palette.Accent.Darken(0.3f)
                    }
                }
            },
            headerText = new SpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = header,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 44, FontWeight.Bold),
                Color = palette.TextLight
            },
            bodyText = new SpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = body,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.Regular),
                // Under the header, not level with it.
                Color = palette.TextLight,
                Alpha = 0.75f
            },
            buttonFlow = new FlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                // Room for the selected one's grow and halo.
                Spacing = new Vector2(0, 18)
            }
        };

        for (int i = 0; i < answers.Length; i++)
        {
            int index = i;

            var button = new DialogButton(answers[i].Icon, answers[i].Text, palette, answers[i].IsDangerous, colorFor(answers[i], palette))
            {
                MinimumWidth = button_min_width,
                ContentPadding = button_padding,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Action = () => take(index),
                Hovered = () => select(index)
            };

            buttons.Add(button);
            buttonFlow.Add(button);
        }

        select(0);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        iconCircle.Scale = new Vector2(0.6f);
        iconCircle.ScaleTo(1, 600, UserInterfaceConstant.SoftBounce);

        for (int i = 0; i < buttons.Count; i++)
            buttons[i].SlideIn(100 + i * 60);
    }

    // Kept clear of the window's edges by a long header or answer.
    private const float side_margin = 60;

    public override void Update()
    {
        base.Update();

        float room = Math.Max(100, (Parent?.DrawWidth ?? 1920) - side_margin * 2);

        // A sprite text doesn't wrap, so a header or line too long for the window shrinks to fit it.
        fitWidth(headerText, room);
        fitWidth(bodyText, room);
        DialogButton.FitColumn(buttons, room);

        headerText.Y = icon_size + header_gap;
        bodyText.Y = headerText.Y + headerText.DrawSize.Y * headerText.Scale.Y + body_gap;
        buttonFlow.Y = bodyText.Y + bodyText.DrawSize.Y * bodyText.Scale.Y + buttons_gap;
    }

    private static Color colorFor(PopupDialogButton answer, YuukiColorPalette palette)
    {
        if (answer.IsDangerous)
            return DialogButton.DANGER;

        return answer.IsCancel ? palette.Surface : palette.Accent;
    }

    private static void fitWidth(SpriteText text, float room)
    {
        float scale = text.DrawWidth > room && text.DrawWidth > 0 ? room / text.DrawWidth : 1;
        text.Scale = new Vector2(scale);
    }

    /// <summary>
    /// Takes the first plain answer, neither the cancel one nor a dangerous one, as if it had been
    /// clicked. Does nothing when there's none: a dangerous answer is never taken for you.
    /// </summary>
    public void PerformOkAction()
    {
        int index = Array.FindIndex(answers, a => !a.IsCancel && !a.IsDangerous);

        if (index >= 0)
            buttons[index].Activate();
    }

    /// <summary>
    /// Takes the cancel answer, as if it had been clicked (Escape, or a click outside the dialog). Returns
    /// false, doing nothing, for a dialog with no cancel answer, which has to be answered.
    /// </summary>
    public bool PerformCancelAction()
    {
        int cancel = Array.FindIndex(answers, a => a.IsCancel);

        if (answered || cancel < 0)
            return false;

        buttons[cancel].Activate();
        return true;
    }

    /// <summary>
    /// The overlay hands its keys on to the dialog it shows. Returns whether the key did something.
    /// </summary>
    public bool HandleKey(KeyEvent e)
    {
        if (e.IsRepeat || answered || buttons.Count == 0)
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

            // A dangerous answer is held, by its key as by the mouse.
            case Key.Enter:
                buttons[selected].Press(e.Key);
                return true;

            case Key.Escape:
                return PerformCancelAction();

            case >= Key.Number1 and <= Key.Number9:
                int number = e.Key - Key.Number1;

                if (number >= buttons.Count)
                    return false;

                select(number);
                buttons[number].Press(e.Key);
                return true;
        }

        return false;
    }

    private void select(int index)
    {
        selected = index;

        for (int i = 0; i < buttons.Count; i++)
            buttons[i].Selected = i == index;
    }

    private void take(int index)
    {
        // Once: a double click or a key held through the fade shouldn't answer twice.
        if (answered)
            return;

        answered = true;
        Dismissed?.Invoke();
        answers[index].Action?.Invoke();
    }
}
