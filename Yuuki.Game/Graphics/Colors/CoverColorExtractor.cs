// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// What a song's focus window paints with: a soft fill and a dark ink of the cover's hue, so the title
/// always reads (about 9:1). <see cref="IsNeutral"/> means the cover has no real colour (greyscale art, a
/// black-and-white photo), and the caller should fall back to something else.
/// </summary>
public readonly record struct CoverSwatch(Color Fill, Color Ink, bool IsNeutral);

/// <summary>
/// Picks the colour a cover "is". The plain average is the wrong answer: a blue sky with an orange sunset
/// averages to grey-brown. This votes by hue instead, so the strongest hue that covers a fair share of the
/// image wins, and greys, near-blacks and near-whites don't vote at all.
/// </summary>
/// <remarks>
/// Works on raw RGBA bytes, so it can read the pixels a cover was already decoded into for display. It
/// samples a grid of at most <see cref="max_samples_per_axis"/> pixels a side, so the cost doesn't grow with
/// the image: the answer doesn't change with more pixels, only the time does.
/// </remarks>
public static class CoverColorExtractor
{
    private const int max_samples_per_axis = 48;

    private const int hue_bins = 24;

    // Pixels this dark or this light carry no usable hue.
    private const float min_lightness = 0.1f;
    private const float max_lightness = 0.94f;
    private const float min_chroma = 0.08f;

    // A cover where fewer than this share of pixels carry a real colour is treated as greyscale. It's a
    // share of pixels rather than a mean, so a dark cover with small bright highlights still counts as
    // coloured.
    private const float min_colored_share = 0.03f;

    // Where the picked hue is placed for the UI: a soft fill and a dark ink.
    private const float fill_lightness = 0.77f;
    private const float ink_lightness = 0.2f;

    public static CoverSwatch Extract(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            return neutral();

        Span<float> weight = stackalloc float[hue_bins];
        Span<float> sumR = stackalloc float[hue_bins];
        Span<float> sumG = stackalloc float[hue_bins];
        Span<float> sumB = stackalloc float[hue_bins];

        int stepX = Math.Max(1, width / max_samples_per_axis);
        int stepY = Math.Max(1, height / max_samples_per_axis);
        int counted = 0, voters = 0;

        for (int y = stepY / 2; y < height; y += stepY)
        {
            for (int x = stepX / 2; x < width; x += stepX)
            {
                int i = (y * width + x) * 4;

                if (rgba[i + 3] < 128)
                    continue;

                float r = rgba[i] / 255f, g = rgba[i + 1] / 255f, b = rgba[i + 2] / 255f;
                float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
                float lightness = (max + min) / 2, chroma = max - min;

                counted++;

                if (lightness < min_lightness || lightness > max_lightness || chroma < min_chroma)
                    continue;

                voters++;

                // Squaring chroma lets a vivid colour outvote a larger muddy one, while area still counts;
                // mid lightness counts most, because that's where a hue is most itself.
                float vote = chroma * chroma * (1 - MathF.Abs(lightness - 0.5f));
                int bin = (int)(hueOf(r, g, b, max, chroma) / 360f * hue_bins) % hue_bins;

                weight[bin] += vote;
                sumR[bin] += r * vote;
                sumG[bin] += g * vote;
                sumB[bin] += b * vote;
            }
        }

        if (counted == 0 || voters < counted * min_colored_share)
            return neutral();

        // Hue is a circle, and one colour can straddle two bins, so each bin is scored with half of each
        // neighbour.
        int best = 0;
        float bestScore = -1;

        for (int bin = 0; bin < hue_bins; bin++)
        {
            float score = weight[bin] + 0.5f * (weight[(bin + hue_bins - 1) % hue_bins] + weight[(bin + 1) % hue_bins]);

            if (score > bestScore)
            {
                bestScore = score;
                best = bin;
            }
        }

        float wr = 0, wg = 0, wb = 0, total = 0;

        for (int offset = -1; offset <= 1; offset++)
        {
            int bin = (best + offset + hue_bins) % hue_bins;
            wr += sumR[bin];
            wg += sumG[bin];
            wb += sumB[bin];
            total += weight[bin];
        }

        if (total <= 0)
            return neutral();

        var (hue, saturation) = hueAndSaturationOf(wr / total, wg / total, wb / total);

        return new CoverSwatch(
            fromHsl(hue, Math.Clamp(saturation, 0.28f, 0.52f), fill_lightness),
            fromHsl(hue, Math.Clamp(saturation, 0.3f, 0.45f), ink_lightness),
            false);
    }

    private static CoverSwatch neutral() => new CoverSwatch(fromHsl(0, 0, fill_lightness + 0.05f), fromHsl(0, 0, ink_lightness - 0.04f), true);

    private static float hueOf(float r, float g, float b, float max, float chroma)
    {
        float h;

        if (max == r)
            h = (g - b) / chroma % 6;
        else if (max == g)
            h = (b - r) / chroma + 2;
        else
            h = (r - g) / chroma + 4;

        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static (float Hue, float Saturation) hueAndSaturationOf(float r, float g, float b)
    {
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        float lightness = (max + min) / 2, chroma = max - min;

        if (chroma <= 0)
            return (0, 0);

        return (hueOf(r, g, b, max, chroma), chroma / (1 - MathF.Abs(2 * lightness - 1)));
    }

    private static Color fromHsl(float hue, float saturation, float lightness)
    {
        float chroma = (1 - MathF.Abs(2 * lightness - 1)) * saturation;
        float x = chroma * (1 - MathF.Abs(hue / 60 % 2 - 1));
        float m = lightness - chroma / 2;

        (float r, float g, float b) = (hue / 60) switch
        {
            < 1 => (chroma, x, 0f),
            < 2 => (x, chroma, 0f),
            < 3 => (0f, chroma, x),
            < 4 => (0f, x, chroma),
            < 5 => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };

        return Color.FromArgb(to8(r + m), to8(g + m), to8(b + m));
    }

    private static int to8(float v) => (int)MathF.Round(Math.Clamp(v, 0, 1) * 255);
}
