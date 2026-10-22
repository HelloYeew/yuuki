// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using Sakura.Framework.Configurations;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.UserInterface;

using Yuuki.Game.Graphics;

namespace Yuuki.Game.Configurations;

public class YuukiConfigManager : ConfigManager<YuukiSetting>
{
    public const int MIN_AUDIO_OFFSET = -300;

    public const int MAX_AUDIO_OFFSET = 300;

    public const double MIN_UI_SCALE = 0.5;
    public const double MAX_UI_SCALE = 1.75;

    public const double MIN_PLAYFIELD_SIZE = 0.5;
    public const double MAX_PLAYFIELD_SIZE = 1;

    public static YuukiSetting LaneKeysSetting(int laneCount) => Math.Clamp(laneCount, 4, 8) switch
    {
        4 => YuukiSetting.LaneKeys4,
        5 => YuukiSetting.LaneKeys5,
        6 => YuukiSetting.LaneKeys6,
        7 => YuukiSetting.LaneKeys7,
        _ => YuukiSetting.LaneKeys8
    };

    public YuukiConfigManager(Storage? storage) : base(storage)
    {
        initializeDefaults();
    }

    private void initializeDefaults()
    {
        Get(YuukiSetting.FirstImport, false);
        Get(YuukiSetting.UseUnicodeInfo, false);
        Get(YuukiSetting.UseYuukiTheme, true);

        Get(YuukiSetting.BackgroundDim, 50);
        Get(YuukiSetting.DisableVideo, false);
        Get(YuukiSetting.DisableGameplayVideo, false);
        Get(YuukiSetting.ScrollSpeed, 750);
        Get(YuukiSetting.CarouselOrientation, CarouselOrientation.Vertical);
        Get(YuukiSetting.BlurBackground, true);
        Get(YuukiSetting.AudioOffset, 0);
        Get(YuukiSetting.BackgroundBlur, 0);
        Get(YuukiSetting.ScrollSpeedFollowsBpm, false);
        Get(YuukiSetting.CursorSize, 1.0);
        Get(YuukiSetting.CursorRotate, true);
        Get(YuukiSetting.SongSelectSort, BeatmapSort.Added);
        Get(YuukiSetting.UIScale, 1.0);
        Get(YuukiSetting.TouchEffects, true);
        Get(YuukiSetting.HitsoundVolume, 1.0);
        Get(YuukiSetting.InactiveVolume, 0.25);
        Get(YuukiSetting.PlayfieldSize, MAX_PLAYFIELD_SIZE);
        Get(YuukiSetting.PlayfieldPositionX, 0.5);
        Get(YuukiSetting.PlayfieldPositionY, 0.5);

        for (int lanes = 4; lanes <= 8; lanes++)
            Get(LaneKeysSetting(lanes), string.Empty);

        Get(YuukiSetting.SwipeLeftKey, Playfield.DEFAULT_SWIPE_LEFT_KEY);
        Get(YuukiSetting.SwipeRightKey, Playfield.DEFAULT_SWIPE_RIGHT_KEY);

        Get(YuukiSetting.ClockDisplayMode, ClockDisplayMode.Full);
        Get(YuukiSetting.Prefer24HourTime, !CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("tt"));
    }
}
