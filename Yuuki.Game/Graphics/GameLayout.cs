// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Graphics;

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
/// the smallest canvas the compact layouts fit. A phone's window or a zoom of <see cref="COMPACT_ZOOM"/>
/// or more gets the compact layout, so a desktop previews a phone at a UI scale of 1.5.
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
    /// From this zoom up, a desktop gets the compact layout too (a canvas shorter than about 770).
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
    /// hide, and each screen's own buttons do their work. It follows the last pointer, a finger or a
    /// mouse (a phone starts on touch), not the window: a small desktop screen gets the phone layout but
    /// still has its keys, and a touchscreen laptop changes over at the first tap.
    /// </summary>
    public readonly Reactive<bool> Touch = new Reactive<bool>(false);

    public bool IsPhone => Current.Value == DisplayLayout.Phone;

    /// <summary>
    /// Whether the window is a phone's (see <see cref="IsPhoneWindow"/>), whatever the UI scale. Gameplay, which keeps
    /// its own canvas rather than the UI's, zooms in by <see cref="PHONE_ZOOM"/> on one as the menus do, so its HUD is
    /// as readable there; a desktop at a large UI scale keeps gameplay as it is.
    /// </summary>
    public readonly Reactive<bool> PhoneWindow = new Reactive<bool>(false);

    /// <summary>
    /// The canvas gameplay is laid out on: <see cref="DESKTOP_CANVAS"/>, or <see cref="PHONE_CANVAS"/> in a phone's
    /// window.
    /// </summary>
    public Vector2 GameplayCanvas => PhoneWindow.Value ? PHONE_CANVAS : DESKTOP_CANVAS;

    /// <summary>
    /// Whether a window <paramref name="heightPoints"/> tall is a phone's, given whether it was.
    /// </summary>
    public static bool IsPhoneWindow(float heightPoints, bool wasPhoneWindow) =>
        heightPoints < PHONE_BELOW || (heightPoints <= DESKTOP_ABOVE && wasPhoneWindow);

    /// <summary>
    /// How far the interface is zoomed at <paramref name="uiScale"/>: from the phone's start when the
    /// window is a phone's, capped at <see cref="MAX_ZOOM"/>.
    /// </summary>
    public static double Zoom(bool phoneWindow, double uiScale) =>
        Math.Min((phoneWindow ? PHONE_ZOOM : 1) * uiScale, MAX_ZOOM);

    /// <summary>
    /// The layout for the window and the <paramref name="zoom"/>.
    /// </summary>
    public static DisplayLayout Choose(bool phoneWindow, double zoom) =>
        phoneWindow || zoom >= COMPACT_ZOOM - 0.001 ? DisplayLayout.Phone : DisplayLayout.Desktop;
}
