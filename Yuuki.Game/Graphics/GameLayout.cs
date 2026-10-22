// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Graphics;

/// <summary>
/// Which layout the player wants (the Layout setting): picked from the window, or forced.
/// </summary>
public enum LayoutPreference
{
    Auto,
    Desktop,
    Phone
}

/// <summary>
/// A layout the game can be in.
/// </summary>
public enum DisplayLayout
{
    /// <summary>
    /// The full interface, for a canvas of about 1920×1080.
    /// </summary>
    Desktop,

    /// <summary>
    /// The compact interface: big enough to read and tap on a phone, with layouts made for canvases down
    /// to <see cref="GameLayout.MIN_CANVAS"/>. Also what a desktop gets at a large UI scale.
    /// </summary>
    Phone
}

/// <summary>
/// The layout the game is in, for screens and overlays to follow, and how far the interface is zoomed.
/// </summary>
/// <remarks>
/// The canvas is <see cref="DESKTOP_CANVAS"/> divided by the zoom: the UI scale, times
/// <see cref="PHONE_ZOOM"/> in a phone's window (one short in screen points, not pixels, and not the
/// platform: a tablet is big), so a phone starts at 1280×720. It never zooms past <see cref="MAX_ZOOM"/>,
/// the smallest canvas the compact layouts fit. On Auto, a phone's window or a zoom of
/// <see cref="COMPACT_ZOOM"/> or more gets the compact layout.
/// </remarks>
public class GameLayout
{
    /// <summary>
    /// A window shorter than this (screen points) is a phone's.
    /// </summary>
    public const float PHONE_BELOW = 560;

    /// <summary>
    /// A window taller than this (screen points) is no longer a phone's. Between the two it stays as it
    /// was, so a window resized across the line doesn't flicker between layouts.
    /// </summary>
    public const float DESKTOP_ABOVE = 640;

    public static readonly Vector2 DESKTOP_CANVAS = new Vector2(1920, 1080);

    /// <summary>
    /// The zoom a phone's window starts from, before the UI scale: a 1280×720 canvas.
    /// </summary>
    public const double PHONE_ZOOM = 1.5;

    /// <summary>
    /// From this zoom up, Auto uses the compact layout on a desktop too (a canvas shorter than about 770).
    /// </summary>
    public const double COMPACT_ZOOM = 1.4;

    /// <summary>
    /// The most the interface zooms, whatever the UI scale: the compact layouts fit down to this.
    /// </summary>
    public const double MAX_ZOOM = 1.75;

    /// <summary>
    /// A phone's canvas at UI scale 1 (at least this: a wider screen gets a wider canvas).
    /// </summary>
    public static readonly Vector2 PHONE_CANVAS = DESKTOP_CANVAS / (float)PHONE_ZOOM;

    /// <summary>
    /// The smallest canvas there is, at <see cref="MAX_ZOOM"/>.
    /// </summary>
    public static readonly Vector2 MIN_CANVAS = DESKTOP_CANVAS / (float)MAX_ZOOM;

    public readonly Reactive<DisplayLayout> Current = new Reactive<DisplayLayout>(DisplayLayout.Desktop);

    /// <summary>
    /// Whether the game is played by touch, so there are no keys to hint at: the footer's key actions
    /// hide, and each screen's own buttons do their work. See <see cref="IsTouch"/>.
    /// </summary>
    public readonly Reactive<bool> Touch = new Reactive<bool>(false);

    public bool IsPhone => Current.Value == DisplayLayout.Phone;

    /// <summary>
    /// Whether to take the game as played by touch. The framework doesn't tell a touch from a click yet,
    /// so a mobile platform stands in for it (and the Layout setting forcing the phone layout, to preview
    /// one); once it does, this follows the last input instead. Not the window: a small desktop screen
    /// gets the phone layout but still has its keys.
    /// </summary>
    public static bool IsTouch(LayoutPreference preference, bool mobile) =>
        mobile || preference == LayoutPreference.Phone;

    /// <summary>
    /// Whether a window <paramref name="heightPoints"/> tall is a phone's, given whether it was.
    /// </summary>
    public static bool IsPhoneWindow(float heightPoints, bool wasPhoneWindow) =>
        heightPoints < PHONE_BELOW || (heightPoints <= DESKTOP_ABOVE && wasPhoneWindow);

    /// <summary>
    /// How far the interface is zoomed at <paramref name="uiScale"/>: from the phone's start when the
    /// window is a phone's (or the setting asks for the phone layout), capped at <see cref="MAX_ZOOM"/>.
    /// </summary>
    public static double Zoom(LayoutPreference preference, bool phoneWindow, double uiScale)
    {
        bool phoneSized = preference == LayoutPreference.Phone || (preference == LayoutPreference.Auto && phoneWindow);
        return Math.Min((phoneSized ? PHONE_ZOOM : 1) * uiScale, MAX_ZOOM);
    }

    /// <summary>
    /// The layout for <paramref name="preference"/>, the window and the <paramref name="zoom"/>.
    /// </summary>
    public static DisplayLayout Choose(LayoutPreference preference, bool phoneWindow, double zoom) => preference switch
    {
        LayoutPreference.Desktop => DisplayLayout.Desktop,
        LayoutPreference.Phone => DisplayLayout.Phone,
        _ => phoneWindow || zoom >= COMPACT_ZOOM - 0.001 ? DisplayLayout.Phone : DisplayLayout.Desktop
    };
}
