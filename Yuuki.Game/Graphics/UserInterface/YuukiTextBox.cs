// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The game's text field based on the framework's <see cref="TextBox"/>
/// </summary>
public partial class YuukiTextBox : TextBox
{
    private YuukiColorPalette palette = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

    private const float text_inset = 14;

    /// <summary>
    /// Beats quicker than this (ms) flash every other beat (or fourth…), so a fast song doesn't turn
    /// the flash into a flicker.
    /// </summary>
    private const double min_blink_length = 500;

    /// <summary>
    /// How much of each beat the caret stays fully lit before it dies away.
    /// </summary>
    private const double caret_hold = 0.2;

    private readonly BeatSyncContainer beatSync;

    public YuukiTextBox()
    {
        Size = new Vector2(360, 38);
        CornerRadius = 8;

        SpriteText.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold);
        SpriteText.Color = Color.White;
        SpriteText.Margin = new MarginPadding { Left = text_inset, Right = text_inset };

        PlaceholderSprite.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Regular);
        PlaceholderSprite.Color = Color.White;
        PlaceholderSprite.Margin = new MarginPadding { Left = text_inset, Right = text_inset };

        ImeText.Font = SpriteText.Font;
        ImeText.Color = JudgementColors.CRITICAL;
        ImeText.Margin = SpriteText.Margin;

        // The caret keeps time with the music (see CaretBlinkTime and GetCaretAlpha). With the music
        // stopped, it keeps the song's tempo off the game clock; with no song, 60 BPM.
        AddInternal(beatSync = new BeatSyncContainer
        {
            AllowBeatSyncWhenStopped = true,
            MinimumBeatLength = min_blink_length
        });
    }

    /// <summary>
    /// Where the caret is in its beat: the time since the last beat, or since the caret moved if that's
    /// sooner, so typing relights it as usual until the next beat takes over.
    /// </summary>
    protected override double CaretBlinkTime => Math.Min(base.CaretBlinkTime, beatSync.TimeSinceBeat);

    /// <summary>
    /// A flash on the beat, as the buttons and the song list keep time: fully lit as the beat lands, held
    /// a moment, then dying away to nothing just as the next lands. The base's glide (out and back in)
    /// peaks on the beat too, but it reads as breathing rather than as the beat.
    /// </summary>
    protected override float GetCaretAlpha(double blinkTime, double blinkDuration)
    {
        // Each fade is half a beat (see Update), so a beat is two.
        double beat = blinkDuration * 2;
        double phase = Math.Clamp(blinkTime / beat, 0, 1);

        if (phase <= caret_hold)
            return 1;

        double fade = (phase - caret_hold) / (1 - caret_hold);
        return (float)Math.Pow(1 - fade, 3);
    }

    public override void Update()
    {
        // Each fade takes half a beat, so a whole blink is one beat. Only set when it changes: setting
        // it restarts the blink.
        double halfBeat = beatSync.BeatLength / 2;
        if (halfBeat > 0 && Math.Abs(CaretBlinkDuration - halfBeat) > 0.5)
            CaretBlinkDuration = halfBeat;

        base.Update();
    }

    /// <summary>Whether the caret is lit right now (diagnostics/tests).</summary>
    public bool CaretLit => Caret.Alpha > 0.5f;

    protected override Drawable CreateBackground() => new Box
    {
        RelativeSizeAxes = Axes.Both,
        Color = palette.Control
    };

    protected override Drawable CreateCaret() => new Box
    {
        Width = 2,
        RelativeSizeAxes = Axes.Y,
        Height = 0.6f,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Color = Color.White,
        Alpha = 0
    };

    protected override Drawable CreateSelectionBox() => new Box
    {
        RelativeSizeAxes = Axes.Y,
        Height = 0.65f,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Color = Color.White.WithAlpha(0.3f),
        Alpha = 0
    };

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        if (Background != null)
            Background.Color = palette.Control;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Only typing makes a sound: text set from code (a search restored, say) arrives unfocused.
        Text.BindValueChanged(e =>
        {
            if (!HasFocus)
                return;

            int change = (e.NewValue?.Length ?? 0) - (e.OldValue?.Length ?? 0);
            if (change > 0)
                uiSounds?.Play(UISound.Type);
            else if (change < 0)
                uiSounds?.Play(UISound.TypeDelete);
        });
    }

    protected override void OnFocusGained() => Background?.FadeToColor(palette.Control.Lighten(0.25f), 150, Easing.OutQuint);

    protected override void OnFocusLost() => Background?.FadeToColor(palette.Control, 150, Easing.OutQuint);
}
