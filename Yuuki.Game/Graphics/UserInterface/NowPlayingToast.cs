// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The song that just started in the corner of the main menu
/// </summary>
public partial class NowPlayingToast : Container
{
    /// <summary>
    /// How long the card stays up before fading, in ms.
    /// </summary>
    private const double display_duration = 4000;

    private const float padding_x = 24;
    private const float padding_y = 14;

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    private SpriteText title = null!;
    private SpriteText artist = null!;
    private Container body = null!;

    private BeatmapSet? shownSet;
    private int showVersion;

    public NowPlayingToast()
    {
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Child = body = new Container
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            AutoSizeAxes = Axes.Both,
            Alpha = 0,
            Children = new Drawable[]
            {
                // Gray and see-through, so the text stays readable on light and dark backgrounds alike.
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 12,
                    Shear = UserInterfaceConstant.GentleShear,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = ColorExtensions.FromHex("#2A2A30"),
                        Alpha = 0.55f
                    }
                },
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(16, 0),
                    Padding = new MarginPadding { Left = padding_x, Right = padding_x, Top = padding_y, Bottom = padding_y },
                    Children = new Drawable[]
                    {
                        // The two lines right-aligned, the artist under the title.
                        new FlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Vertical,
                            Children = new Drawable[]
                            {
                                title = new SpriteText
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 30, FontWeight.Bold),
                                    Color = Color.White
                                },
                                artist = new SpriteText
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Regular),
                                    Color = Color.White.WithAlpha(0.85f)
                                }
                            }
                        },
                        new IconSprite
                        {
                            Margin = new MarginPadding { Top = 10 },
                            Icon = IconUsage.MusicNote,
                            IconSize = 40,
                            Color = Color.White
                        }
                    }
                }
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Tracked, so the shared setting doesn't keep the menu alive.
        if (config != null)
            BindValueChanged(config.Get<bool>(YuukiSetting.UseUnicodeInfo), _ => updateText());
    }

    /// <summary>
    /// Shows <paramref name="set"/> as now playing; again for the same song only when <paramref name="force"/>.
    /// </summary>
    public void Show(BeatmapSet? set, bool force = false)
    {
        if (set == null || (!force && ReferenceEquals(set, shownSet)))
            return;

        shownSet = set;
        updateText();

        body.ClearTransforms();
        body.X = 60;
        body.MoveToX(0, 500, Easing.OutQuint).FadeIn(300, Easing.OutQuint);

        int version = ++showVersion;
        Scheduler.AddDelayed(() =>
        {
            if (version == showVersion)
                body.FadeOut(600, Easing.OutQuint);
        }, display_duration);
    }

    /// <summary>
    /// Fills the title and artist of the shown song, in the script the setting picks.
    /// </summary>
    private void updateText()
    {
        if (shownSet == null)
            return;

        bool unicode = config?.Get<bool>(YuukiSetting.UseUnicodeInfo).Value ?? false;
        title.Text = shownSet.GetDisplayTitle(unicode);
        artist.Text = shownSet.GetDisplayArtist(unicode);
    }

    /// <summary>
    /// Takes the card away now (e.g. when the menu is left).
    /// </summary>
    public void HideNow()
    {
        showVersion++;
        body.ClearTransforms();
        body.FadeOut(200, Easing.OutQuint);
    }

    /// <summary>
    /// Whether the card is up (diagnostics/tests).
    /// </summary>
    public bool IsShowing => body.Alpha > 0;

    /// <summary>
    /// The title it shows (diagnostics/tests).
    /// </summary>
    public string ShownTitle => title.Text;
}
