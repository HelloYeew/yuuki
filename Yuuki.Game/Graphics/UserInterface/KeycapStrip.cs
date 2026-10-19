// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Allocation;
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

/// <summary>
/// A row of keys as keycaps that can be rebound: click one, then press the key you want for it
/// (Escape cancels). A key already on another cap of the same row swaps over, so no two share one;
/// a key the host reserves (<see cref="IsReserved"/>) is refused with the "denied" sound. The host
/// owns the keyboard (the game's screens and overlays keep focus), so it forwards key presses to
/// <see cref="HandleKey"/> while <see cref="IsCapturing"/>.
/// </summary>
public partial class KeycapStrip : FlowContainer
{
    private readonly YuukiColorPalette palette;
    private readonly Key[] defaults;
    private readonly KeycapCaptureGroup? group;
    private Key[] keys;

    // The cap waiting for a key, if any.
    private int? capturing;

    /// <summary>
    /// Fired with the new keys after a rebind or a reset.
    /// </summary>
    public event Action<Key[]>? KeysChanged;

    /// <summary>
    /// Keys this row refuses, e.g. the swipe keys for a lane layout. Null refuses none.
    /// </summary>
    public Func<Key, bool>? IsReserved { get; set; }

    /// <summary>
    /// How each cap reads, by its index and key; <see cref="KeyLabels.For"/> by default.
    /// </summary>
    public Func<int, Key, string>? Format { get; set; }

    /// <summary>
    /// Whether caps can be clicked to rebind. A fixed row just shows its keys.
    /// </summary>
    public bool Editable { get; init; } = true;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    /// <param name="palette">The colours of the caps.</param>
    /// <param name="keys">The keys shown, left to right.</param>
    /// <param name="defaults">What <see cref="ResetToDefault"/> goes back to.</param>
    /// <param name="group">Rows that share one capture at a time: starting one stops the others.</param>
    public KeycapStrip(YuukiColorPalette palette, Key[] keys, Key[]? defaults = null, KeycapCaptureGroup? group = null)
    {
        this.palette = palette;
        this.keys = keys.ToArray();
        this.defaults = (defaults ?? keys).ToArray();
        this.group = group;

        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(8, 0);
    }

    /// <summary>The keys, left to right.</summary>
    public Key[] Keys => keys.ToArray();

    public bool IsDefault => keys.SequenceEqual(defaults);

    public bool IsCapturing => capturing != null;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        refresh();
    }

    /// <summary>
    /// Shows <paramref name="newKeys"/> (the setting changed somewhere else), without firing <see cref="KeysChanged"/>.
    /// </summary>
    public void SetKeys(Key[] newKeys)
    {
        if (newKeys.SequenceEqual(keys))
            return;

        keys = newKeys.ToArray();
        capturing = null;
        refresh();
    }

    public void ResetToDefault()
    {
        capturing = null;
        keys = defaults.ToArray();
        refresh();
        KeysChanged?.Invoke(Keys);
    }

    public void CancelCapture()
    {
        if (capturing == null)
            return;

        capturing = null;
        refresh();
    }

    /// <summary>
    /// Takes a key press while a cap waits for one. True when it was used (or refused).
    /// </summary>
    public bool HandleKey(KeyEvent e)
    {
        if (capturing is not int index || e.IsRepeat)
            return false;

        if (e.Key == Key.Escape)
        {
            CancelCapture();
            return true;
        }

        if (IsReserved?.Invoke(e.Key) == true)
        {
            uiSounds?.Play(UISound.Denied);
            flashRefused(index);
            return true;
        }

        // A key already on another cap swaps over.
        int other = Array.IndexOf(keys, e.Key);
        if (other >= 0 && other != index)
            keys[other] = keys[index];

        keys[index] = e.Key;
        capturing = null;
        refresh();
        KeysChanged?.Invoke(Keys);
        return true;
    }

    private void startCapture(int index)
    {
        if (group?.Capturing is KeycapStrip previous && previous != this)
            previous.CancelCapture();

        capturing = capturing == index ? null : index;

        if (group != null)
            group.Capturing = capturing == null ? null : this;

        refresh();
    }

    private void refresh()
    {
        if (group?.Capturing == this && capturing == null)
            group.Capturing = null;

        Clear();

        for (int i = 0; i < keys.Length; i++)
        {
            int index = i;
            bool waiting = capturing == index;
            string label = waiting ? "…" : Format?.Invoke(index, keys[index]) ?? KeyLabels.For(keys[index]);

            Add(new Keycap(palette, label, waiting, Editable ? () => startCapture(index) : null));
        }
    }

    private void flashRefused(int index)
    {
        if (index < Children.Count && Children[index] is Keycap cap)
            cap.FlashRefused();
    }

    /// <summary>
    /// One key: a rounded cap, amber while it waits for a key.
    /// </summary>
    private partial class Keycap : ClickableContainer
    {
        private readonly Box background;
        private readonly Color restColor;

        public Keycap(YuukiColorPalette palette, string label, bool waiting, Action? action)
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 8;
            restColor = waiting ? JudgementColors.CRITICAL : palette.Control;

            if (action != null)
                Action = action;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = restColor
                },
                new SpriteText
                {
                    Margin = new MarginPadding { Left = 14, Right = 14, Top = 6, Bottom = 7 },
                    Text = label,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                    Color = waiting ? Color.White : palette.TextLight
                }
            };

            if (action != null)
                Add(new HoverClickSounds());
        }

        public void FlashRefused()
        {
            background.ClearTransforms();
            background.Color = JudgementColors.MISS;
            background.FadeToColor(restColor, 400, Easing.OutQuint);
        }
    }
}

/// <summary>Keycap rows that take turns capturing: starting a capture in one stops any other.</summary>
public class KeycapCaptureGroup
{
    /// <summary>The row waiting for a key, if any.</summary>
    public KeycapStrip? Capturing { get; set; }

    /// <summary>Gives a key press to the row waiting for one. True when it was used.</summary>
    public bool HandleKey(KeyEvent e) => Capturing?.HandleKey(e) ?? false;

    public void CancelCapture() => Capturing?.CancelCapture();
}

/// <summary>
/// Short, readable names for keys, as on a keycap ("L Shift", ";", "Space").
/// </summary>
public static class KeyLabels
{
    public static string For(Key key) => key switch
    {
        Key.ShiftLeft => "L Shift",
        Key.ShiftRight => "R Shift",
        Key.ControlLeft => "L Ctrl",
        Key.ControlRight => "R Ctrl",
        Key.AltLeft => "L Alt",
        Key.AltRight => "R Alt",
        Key.Semicolon => ";",
        Key.Quote => "'",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        Key.BackSlash => "\\",
        Key.Tilde => "`",
        Key.Minus => "-",
        Key.Plus => "=",
        Key.BracketLeft => "[",
        Key.BracketRight => "]",
        Key.Escape => "Esc",
        >= Key.Number0 and <= Key.Number9 => (key - Key.Number0).ToString(),
        _ => key.ToString()
    };
}
