// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Notifications;

/// <summary>
/// A message with an icon.
/// </summary>
public partial class SimpleNotification : Notification
{
    private string text = string.Empty;

    public override string Text
    {
        get => text;
        set
        {
            text = value;
            textFlow.Text = value;
        }
    }

    public IconUsage Icon
    {
        get => iconSprite.Icon;
        set => iconSprite.Icon = value;
    }

    /// <summary>
    /// The badge's color; the palette's accent when unset.
    /// </summary>
    public Color IconBackgroundColor
    {
        get => AccentColor;
        set => AccentColor = value;
    }

    private readonly TextFlowContainer textFlow;
    private readonly IconSprite iconSprite;

    public SimpleNotification()
    {
        IconContent.Add(iconSprite = new IconSprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Icon = IconUsage.Info,
            IconSize = 20,
            Color = Color.White
        });

        TextContent.Add(textFlow = new TextFlowContainer(t =>
        {
            t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Bold);
            t.Color = Color.White;
        })
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            AutoSizeAxes = Axes.Y
        });
    }
}

/// <summary>
/// What a finished <see cref="ProgressNotification"/> turns into: a tick on green.
/// </summary>
public partial class ProgressCompletionNotification : SimpleNotification
{
    public override UISound? PopInSound => UISound.NotificationDone;

    public ProgressCompletionNotification()
    {
        Icon = IconUsage.Check;
        IconBackgroundColor = NotificationColors.GREEN;
    }
}

/// <summary>
/// Something went wrong: a warning on red.
/// </summary>
public partial class SimpleErrorNotification : SimpleNotification
{
    public override UISound? PopInSound => UISound.NotificationError;

    public SimpleErrorNotification()
    {
        Icon = IconUsage.Error;
        IconBackgroundColor = NotificationColors.RED;
    }
}
