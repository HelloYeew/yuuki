// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Configurations;

namespace Yuuki.Game.Configurations;

[SettingSource("game.ini")]
public enum YuukiSetting
{
    /// <summary>
    /// Whether the bundled starter beatmaps have already been imported into game storage.
    /// </summary>
    FirstImport,

    /// <summary>
    /// Whether to display the localized (unicode) title/artist/source instead of the romanized text.
    /// </summary>
    UseUnicodeInfo,

    /// <summary>
    /// Whether to play the bundled Yuuki theme on the menu.
    /// </summary>
    UseYuukiTheme,

    /// <summary>
    /// How much to dim the playfield background, 0-100 (%).
    /// </summary>
    BackgroundDim,

    /// <summary>
    /// Whether to disable beatmap background videos. When enabled, only the background image is
    /// shown, and no video is decoded/played.
    /// </summary>
    DisableVideo,

    /// <summary>
    /// Note scroll speed as the time (ms) a note is visible before the judgment line. Lower is
    /// faster. Consumed by the gameplay playfield's <c>Preempt</c>.
    /// </summary>
    ScrollSpeed,

    /// <summary>
    /// The direction the song-selection carousels lay out in (a horizontal strip vs. a vertical rail).
    /// Backed by <see cref="Yuuki.Game.Graphics.UserInterface.CarouselOrientation"/>.
    /// </summary>
    CarouselOrientation,

    /// <summary>
    /// Whether to blur the beatmap background on the song-selection screen.
    /// </summary>
    BlurBackground,

    /// <summary>
    /// Global audio offset in ms, added to the gameplay clock (<c>GameplayClock.Offset</c>). It covers
    /// the output latency the audio backend can't see (display, device, Bluetooth). Hitting late on
    /// average means it should go down; hitting early, up.
    /// </summary>
    AudioOffset,

    /// <summary>
    /// How much to blur the background during play, 0-100 (%)
    /// </summary>
    BackgroundBlur,

    /// <summary>
    /// Whether note speed follows the song's BPM: <see cref="ScrollSpeed"/> then means the time on
    /// screen at 120 BPM, and faster songs scroll faster.
    /// </summary>
    ScrollSpeedFollowsBpm,

    /// <summary>
    /// Saved lane keys per lane count ("D,F,J,K"). Empty means the default layout
    /// (<c>LaneKeyLayouts</c>); rebound on the setup screen by clicking a keycap.
    /// </summary>
    LaneKeys4,
    LaneKeys5,
    LaneKeys6,
    LaneKeys7,
    LaneKeys8,

    /// <summary>The cursor's size, as a multiplier (0.5–2).</summary>
    CursorSize,

    /// <summary>Whether the cursor turns to point along a drag while a button is held.</summary>
    CursorRotate,

    /// <summary>How song select orders the songs. Backed by <see cref="Yuuki.Game.Beatmaps.BeatmapSort"/>.</summary>
    SongSelectSort,

    /// <summary>
    /// How big the interface is, as a multiplier (<see cref="YuukiConfigManager.MIN_UI_SCALE"/>–
    /// <see cref="YuukiConfigManager.MAX_UI_SCALE"/>)
    /// </summary>
    UIScale,

    /// <summary>
    /// How loud the gameplay hitsounds are (notes, holds, the combo break), 0–1, on top of the
    /// framework's effects volume. Interface sounds and the count-in don't follow it.
    /// </summary>
    HitsoundVolume,

    /// <summary>
    /// How big the playfield is, as a fraction (<see cref="YuukiConfigManager.MIN_PLAYFIELD_SIZE"/>–
    /// <see cref="YuukiConfigManager.MAX_PLAYFIELD_SIZE"/>) of the largest that fits the window. The
    /// highway keeps its shape at any size or window; see <c>PlayfieldAdjustmentContainer</c>.
    /// </summary>
    PlayfieldSize,

    /// <summary>
    /// Where the playfield sits across the room left beside it, 0–1 (left to right, 0.5 centred)
    /// has no effect when the playfield already spans that axis.
    /// </summary>
    PlayfieldPositionX,

    /// <summary>Where the playfield sits in the room left above and below it, 0–1 (top to bottom).</summary>
    PlayfieldPositionY,

    /// <summary>
    /// Whether to hide the beatmap's video during play (and replays), showing only the background
    /// image. Menus and song select keep it; <see cref="DisableVideo"/> turns it off everywhere.
    /// </summary>
    DisableGameplayVideo,

    SwipeLeftKey,

    SwipeRightKey,

    /// <summary>
    /// How loud the game gets while its window isn't the active one
    /// </summary>
    InactiveVolume,

    /// <summary>
    /// What the footer's clock shows. Backed by <see cref="Yuuki.Game.Graphics.UserInterface.ClockDisplayMode"/>;
    /// clicking the clock steps through the modes too.
    /// </summary>
    ClockDisplayMode,

    /// <summary>
    /// Whether the clock reads 15:30 rather than 3:30 PM. Defaults to the system's own preference
    /// </summary>
    Prefer24HourTime
}
