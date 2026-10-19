// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class DialogButton : ClickableContainer
{
    private const float width = 380;
    private const float height = 60;

    // Room either side of the icon and label, clear of the sheared ends.
    private const float content_padding = 60;

    /// <summary>
    /// How long a dangerous answer is held
    /// </summary>
    public const double HOLD_DURATION = 500;

    // After letting go, the bar stays this long, then drains over the next
    private const double release_pause = 200;
    private const double release_drain = 200;

    /// <summary>The game's error red, for answers that can't be taken back.</summary>
    public static readonly Color DANGER = ColorExtensions.FromHex("F07178");

    private static readonly Color danger_soft = ColorExtensions.FromHex("DDA4A9");

    private readonly YuukiColorPalette palette;
    private readonly bool dangerous;
    private readonly Color? color;
    private readonly Container body;
    private readonly Container shape;
    private readonly Box background;
    private readonly Box holdFill;
    private readonly FlowContainer content;

    private bool mouseHeld;
    private Key? heldKey;
    private bool confirming;
    private bool fired;
    private double holdProgress;
    private double releasedAt = double.NaN;
    private double lastFrameTime = double.NaN;

    // Tinted one by one: a container's colour doesn't reach its children in this framework.
    private readonly IconSprite iconSprite;
    private readonly SpriteText labelText;

    /// <summary>Called when the mouse comes over the button, so the column can select it.</summary>
    public Action? Hovered;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? sounds { get; set; }

    private bool selected;

    public bool Selected
    {
        set
        {
            if (selected == value)
                return;

            selected = value;
            updateState();

            // Moving on to another answer lets go of this one, as sliding the mouse off does.
            if (!value && heldKey != null)
                releaseKey();
        }
    }

    /// <summary>The narrowest it's made, however short its label.</summary>
    public float MinimumWidth { get; init; } = width;

    /// <summary>Room either side of the icon and label.</summary>
    public float ContentPadding { get; init; } = content_padding;

    /// <param name="dangerous">Whether this answer can't be taken back, so it has to be held, by the mouse or its key.</param>
    /// <param name="color">The answer's own color, for the main menu's look (see remarks); none for the palette's.</param>
    public DialogButton(IconUsage icon, string text, YuukiColorPalette palette, bool dangerous = false, Color? color = null)
    {
        this.palette = palette;
        this.dangerous = dangerous;
        this.color = color;
        Size = new Vector2(width, height);

        Child = body = new Container
        {
            // Centred, so the selected button's grow spreads both ways instead of out to the right.
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                shape = new Container
                {
                    // Sheared about its centre (a shear turns about the origin), so the slanted bar sits
                    // centred on the label rather than leaning out from its top edge.
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Shear = UserInterfaceConstant.Shear,
                    CornerRadius = 6,
                    Children = new Drawable[]
                    {
                        background = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = color ?? (dangerous ? danger_soft : palette.Surface)
                        },
                        // How far a held dangerous answer has got.
                        holdFill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(0, 1),
                            Color = Color.White.WithAlpha(0.35f)
                        }
                    }
                },
                content = new FlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        iconSprite = new IconSprite
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = icon,
                            IconSize = 28,
                            Filled = true
                        },
                        labelText = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = text,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 26, FontWeight.Bold)
                        }
                    }
                }
            }
        };

        if (color is Color own)
        {
            shape.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = own.WithAlpha(0),
                Radius = 16
            };
        }

        updateState();

        // The press sound comes from Activate, which the keyboard uses too.
        Add(new HoverClickSounds(null));
    }

    /// <summary>
    /// The width this answer needs for its icon and label: its usual width, or more for a long one.
    /// </summary>
    public float NaturalWidth => Math.Max(MinimumWidth, content.DrawWidth + ContentPadding * 2);

    /// <summary>
    /// Makes the button <paramref name="target"/> wide, shrinking the icon and label when they wouldn't
    /// fit (a long answer on a narrow window) rather than letting them run past the ends.
    /// </summary>
    public void FitTo(float target)
    {
        Width = target;

        float room = target - ContentPadding * 2;
        float scale = content.DrawWidth > room && content.DrawWidth > 0 ? Math.Max(0.1f, room / content.DrawWidth) : 1;
        content.Scale = new Vector2(scale);
    }

    /// <summary>
    /// Gives a column of answers one width: the widest one's, up to <paramref name="maxWidth"/>, so
    /// long labels stay inside their buttons and the column stays even.
    /// </summary>
    public static void FitColumn(IReadOnlyList<DialogButton> buttons, float maxWidth)
    {
        if (buttons.Count == 0)
            return;

        float widest = 0;

        foreach (var button in buttons)
            widest = Math.Max(widest, button.NaturalWidth);

        float target = Math.Min(widest, maxWidth);

        foreach (var button in buttons)
            button.FitTo(target);
    }

    public void SlideIn(double delay)
    {
        body.ClearTransforms();
        body.Alpha = 0;
        body.Y = 30;
        Scheduler.AddDelayed(() =>
        {
            body.MoveToY(0, 500, UserInterfaceConstant.SoftBounce);
            body.FadeIn(250, Easing.OutQuint);
        }, delay);
    }

    /// <summary>Takes this answer now (a plain answer's key, or a hold that has filled).</summary>
    public void Activate()
    {
        fired = true;
        sounds?.Play(dangerous ? UISound.DialogDangerousSelect : UISound.Click);
        background.FlashColor(Color.White, 300, Easing.OutQuint);
        Action?.Invoke();
    }

    /// <summary>
    /// Takes this answer from a key: at once, or for a dangerous answer, by holding <paramref name="key"/>
    /// down for as long as the mouse has to hold it.
    /// </summary>
    public void Press(Key key)
    {
        if (!dangerous)
        {
            Activate();
            return;
        }

        heldKey = key;
        beginHold();
    }

    private void releaseKey()
    {
        heldKey = null;

        if (!mouseHeld || !IsHovered)
            abortHold();
    }

    /// <summary>How far a held dangerous answer has filled, 0 to 1 (eased, as drawn).</summary>
    public float HoldProgress => (float)(1 - Math.Pow(1 - holdProgress, 2));

    public override bool OnHover(MouseEvent e)
    {
        Hovered?.Invoke();

        // Sliding back on while still held picks the hold up where it was.
        if (dangerous && mouseHeld)
            beginHold();

        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (dangerous && mouseHeld && heldKey == null)
            abortHold();

        return base.OnHoverLost(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (dangerous && e.Button == MouseButton.Left)
        {
            mouseHeld = true;
            beginHold();
        }

        return base.OnMouseDown(e);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (dangerous && e.Button == MouseButton.Left)
        {
            mouseHeld = false;

            if (heldKey == null)
                abortHold();
        }

        return base.OnMouseUp(e);
    }

    // A dangerous answer is taken by holding, never by a click.
    public override bool OnClick(MouseButtonEvent e) => dangerous || base.OnClick(e);

    private void beginHold()
    {
        if (fired || !Enabled.Value)
            return;

        confirming = true;
        releasedAt = double.NaN;
    }

    private void abortHold()
    {
        if (!confirming)
            return;

        confirming = false;
        releasedAt = Clock.CurrentTime;
    }

    public override void Update()
    {
        base.Update();

        if (!dangerous)
            return;

        // Looked up rather than waiting for the key's release: whatever else takes that, the hold still
        // ends with the key.
        if (heldKey is Key key && GetContainingInputManager()?.CurrentState.IsPressed(key) != true)
            releaseKey();

        double now = Clock.CurrentTime;
        double elapsed = double.IsNaN(lastFrameTime) ? 0 : Math.Max(0, now - lastFrameTime);
        lastFrameTime = now;

        if (confirming)
        {
            holdProgress = Math.Min(1, holdProgress + elapsed / HOLD_DURATION);

            // A tick each step of the way (UISoundPlayer keeps them 40 ms apart), rising in pitch
            // and volume as the bar fills.
            float progress = HoldProgress;
            sounds?.Play(UISound.DialogDangerousTick, pitch: 1 + progress, volume: 0.1 + progress / 2);

            if (holdProgress >= 1)
            {
                confirming = false;
                Activate();
            }
        }
        else if (!fired && holdProgress > 0 && !double.IsNaN(releasedAt) && now - releasedAt >= release_pause)
        {
            holdProgress = Math.Max(0, holdProgress - elapsed / release_drain);
        }

        holdFill.Width = HoldProgress;
    }

    private void updateState()
    {
        body.ScaleTo(selected ? 1.05f : 1f, 300, UserInterfaceConstant.SoftBounce);

        if (color is Color own)
        {
            // Selected, the answer's color at full strength in a halo of it; the others muted. Never
            // lightened: a light color (the beige of backing out) would wash out toward white. The icon
            // and label stay darker shades of the color either way, as on the main menu's buttons.
            background.Color = own;
            background.FadeTo(selected ? 1 : 0.6f, 150, Easing.OutQuint);
            shape.FadeEdgeEffectTo(own.WithAlpha(selected ? 0.5f : 0), 150, Easing.OutQuint);
            iconSprite.FadeToColor(own.Darken(0.3f), 150, Easing.OutQuint);
            labelText.FadeToColor(own.Darken(0.5f), 150, Easing.OutQuint);
            return;
        }

        Color idle = dangerous ? danger_soft : palette.Surface;
        Color lit = dangerous ? DANGER : palette.Control;

        background.FadeToColor(selected ? lit : idle, 150, Easing.OutQuint);
        Color foreground = selected ? Color.White : palette.Text;
        iconSprite.FadeToColor(foreground, 150, Easing.OutQuint);
        labelText.FadeToColor(foreground, 150, Easing.OutQuint);
    }
}
