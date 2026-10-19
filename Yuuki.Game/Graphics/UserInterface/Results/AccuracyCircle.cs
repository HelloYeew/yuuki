// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

public partial class AccuracyCircle : Container
{
    private const float ring_inner_radius = 0.84f;

    private readonly RingArc fill;
    private readonly SpriteText rankText;
    private readonly SpriteText accuracyText;
    private readonly Circle[] markers;
    private readonly SpriteText[] markerLabels;

    // A slash through each marker's letter, shown when that rank is lost.
    private readonly Box[] markerSlashes;

    // The highest threshold the fill has passed so far, as an index into THRESHOLD_RANKS (−1 = none).
    private int crossedThreshold = -1;

    /// <summary>
    /// Fired once per rank threshold as the fill passes it, in ascending order.
    /// </summary>
    public event Action<ScoreRank>? ThresholdCrossed;

    public AccuracyCircle(YuukiColorPalette palette)
    {
        Children = new Drawable[]
        {
            // A dark backdrop reaching past the rank letters, so they read on any beatmap background.
            new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1.36f),
                Color = palette.Backdrop,
                Alpha = 0.55f
            },
            new RingArc
            {
                RelativeSizeAxes = Axes.Both,
                InnerRadius = ring_inner_radius,
                Color = palette.Control,
                Alpha = 0.8f
            },
            fill = new RingArc
            {
                RelativeSizeAxes = Axes.Both,
                InnerRadius = ring_inner_radius,
                Progress = 0,
                Color = JudgementColors.For(ScoreRank.D)
            },
            new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.76f),
                Color = palette.Surface,
                Alpha = 0.92f
            },
            rankText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Y = -14,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 150, FontWeight.Bold),
                Alpha = 0
            },
            accuracyText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Y = 72,
                Text = "0.00%",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 28, FontWeight.Bold),
                Color = palette.Text
            }
        };

        // A dot on the ring and a letter just outside it for every rank threshold.
        markers = new Circle[ScoreRankExtensions.THRESHOLD_RANKS.Length];
        markerLabels = new SpriteText[markers.Length];
        markerSlashes = new Box[markers.Length];

        for (int i = 0; i < markers.Length; i++)
        {
            ScoreRank rank = ScoreRankExtensions.THRESHOLD_RANKS[i];
            float angle = -MathF.PI / 2 + MathF.Tau * (float)rank.MinimumAccuracy();
            float ringRadius = (1 + ring_inner_radius) / 4;

            Add(markers[i] = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(ringRadius * MathF.Cos(angle), ringRadius * MathF.Sin(angle)),
                Size = new Vector2(12),
                Color = palette.TextLight
            });

            Add(markerLabels[i] = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(0.58f * MathF.Cos(angle), 0.58f * MathF.Sin(angle)),
                Text = rank.ToString(),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                Color = palette.TextLight,
                Alpha = 0.85f
            });

            Add(markerSlashes[i] = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.Both,
                Position = markerLabels[i].Position,
                Size = new Vector2(34, 4),
                Rotation = -35,
                Color = JudgementColors.MISS,
                Alpha = 0
            });
        }
    }

    /// <summary>The filled fraction of the ring, i.e. the accuracy displayed so far (0–1).</summary>
    public float Progress
    {
        get => fill.Progress;
        set
        {
            fill.Progress = value;
            accuracyText.Text = $"{value * 100:0.00}%";

            // Compared as floats: the fill is a float, so an accuracy of exactly 95% would otherwise stop
            // a hair short of the S threshold and never light it.
            while (crossedThreshold + 1 < markers.Length
                   && value >= (float)ScoreRankExtensions.THRESHOLD_RANKS[crossedThreshold + 1].MinimumAccuracy())
            {
                crossedThreshold++;
                lightMarker(crossedThreshold);
            }
        }
    }

    private void lightMarker(int index)
    {
        ScoreRank rank = ScoreRankExtensions.THRESHOLD_RANKS[index];
        Color color = JudgementColors.For(rank);

        // The fill takes on the color of the rank it has reached.
        fill.Color = color;

        markers[index].FadeToColor(color, 150, Easing.OutQuint);
        markers[index].Scale = new Vector2(1.8f);
        markers[index].ScaleTo(1f, 400, Easing.OutQuint);
        markerLabels[index].FadeToColor(color, 150, Easing.OutQuint).FadeIn(150, Easing.OutQuint);
        markerLabels[index].Scale = new Vector2(1.5f);
        markerLabels[index].ScaleTo(1f, 400, Easing.OutQuint);

        ThresholdCrossed?.Invoke(rank);
    }

    /// <summary>
    /// Takes back a rank the fill reached but the run didn't earn: its marker flashes and turns red, its
    /// letter shakes "no" and gets slashed through, and the fill settles on the colour of the rank kept.
    /// </summary>
    public void LoseRank(ScoreRank lost, ScoreRank kept)
    {
        int index = Array.IndexOf(ScoreRankExtensions.THRESHOLD_RANKS, lost);
        if (index < 0)
            return;

        fill.FadeToColor(JudgementColors.For(kept), 400, Easing.OutQuint);

        Circle marker = markers[index];
        marker.ClearTransforms();
        marker.Color = Color.White;
        marker.Scale = new Vector2(2f);
        marker.FadeToColor(JudgementColors.MISS, 300, Easing.OutQuint);
        marker.ScaleTo(1f, 450, Easing.OutQuint);

        // The shake goes first: Then() waits for every transform on the label, so anything added
        // before it would hold the shake back.
        SpriteText label = markerLabels[index];
        label.ClearTransforms();
        label.RotateTo(-18, 45)
             .Then().RotateTo(14, 70)
             .Then().RotateTo(-8, 70)
             .Then().RotateTo(0, 120, Easing.OutQuint);
        label.FadeToColor(JudgementColors.MISS, 200, Easing.OutQuint);
        label.FadeTo(0.55f, 600, Easing.OutQuint);
        // Settles a pop from lightMarker that the clear may have cut short (a threshold passed late).
        label.ScaleTo(1f, 200, Easing.OutQuint);

        // The slash cuts across once the shake has started.
        Box slash = markerSlashes[index];
        slash.ClearTransforms();
        slash.Alpha = 0;
        slash.Scale = new Vector2(0.01f, 1);
        slash.Wait(90).FadeIn().Then().ScaleTo(Vector2.One, 220, Easing.OutQuint);
    }

    /// <summary>
    /// Slams the rank letter into the middle of the ring.
    /// </summary>
    public void RevealRank(ScoreRank rank)
    {
        rankText.Text = rank.ToString();
        rankText.Color = JudgementColors.For(rank);
        rankText.Scale = new Vector2(2.2f);
        rankText.ScaleTo(1f, 500, Easing.OutQuint);
        rankText.FadeIn(200, Easing.OutQuint);
    }
}
