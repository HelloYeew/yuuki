// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.Video;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Setup;

/// <summary>
/// The set's video, playing on a loop and silent
/// </summary>
public partial class VideoPreview : Container
{
    private readonly SpriteText placeholderText;
    private VideoSprite? video;
    private string? wantedPath;
    private string? shownPath;

    /// <summary>
    /// Whether it may play (the tab it's on is showing).
    /// </summary>
    public bool Active { get; set; } = true;

    public VideoPreview()
    {
        Masking = true;
        CornerRadius = 8;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = Color.Black.WithAlpha(0.35f)
            },
            placeholderText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "No video",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                Color = Color.White.WithAlpha(0.5f)
            }
        };
    }

    /// <summary>
    /// Shows the video at <paramref name="fullPath"/> (on disk), or none, from when it's next active.
    /// </summary>
    public void SetVideo(string? fullPath) => wantedPath = fullPath;

    private void show(string? fullPath)
    {
        shownPath = fullPath;

        if (video != null)
        {
            Remove(video);
            video = null;
        }

        placeholderText.Alpha = string.IsNullOrEmpty(fullPath) ? 1 : 0;

        if (string.IsNullOrEmpty(fullPath))
            return;

        try
        {
            Add(video = new VideoSprite(fullPath)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                FillMode = TextureFillMode.Fill,
                Alpha = 0
            });

            video.FadeIn(300, Easing.OutQuint);
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't preview the video: {e.Message}");
            video = null;
            placeholderText.Alpha = 1;
        }
    }

    public override void Update()
    {
        base.Update();

        if (!Active)
        {
            if (video?.IsPlaying == true)
                video.Pause();

            return;
        }

        if (wantedPath != shownPath)
            show(wantedPath);

        if (video == null)
            return;

        if (!video.IsPlaying)
            video.Play();

        // Round again from the start once it ends.
        if (video.Duration > 0 && video.CurrentTime >= video.Duration - 30)
            video.Seek(0);
    }
}
