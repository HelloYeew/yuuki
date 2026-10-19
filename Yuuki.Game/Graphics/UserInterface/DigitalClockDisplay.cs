// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The time in digits, with how long the game has been running under it
/// </summary>
/// <remarks>
/// Set in Nunito, whose digits are all one width, so the readout doesn't shuffle as the seconds tick
/// </remarks>
public partial class DigitalClockDisplay : ClockDisplay
{
    private readonly SpriteText realTime;
    private readonly SpriteText gameTime;
    private readonly SpriteText runningLabel;
    private readonly FlowContainer runningText;

    public string ShownTime => realTime.Text;

    public string ShownRunningTime => gameTime.Text;

    private bool showRuntime = true;

    public bool ShowRuntime
    {
        get => showRuntime;
        set
        {
            if (showRuntime == value)
                return;

            showRuntime = value;
            updateMetrics();
        }
    }

    private bool use24HourDisplay;

    public bool Use24HourDisplay
    {
        get => use24HourDisplay;
        set
        {
            if (use24HourDisplay == value)
                return;

            use24HourDisplay = value;

            updateMetrics();
            // Rewritten now rather than at the next second.
            UpdateDisplay(DateTimeOffset.Now);
        }
    }

    public DigitalClockDisplay()
    {
        AutoSizeAxes = Axes.Both;

        Children = new Drawable[]
        {
            realTime = new SpriteText
            {
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Medium)
            },
            runningText = new FlowContainer
            {
                Y = 15,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(2, 0),
                Children = new Drawable[]
                {
                    runningLabel = new SpriteText
                    {
                        Text = "running",
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 10, FontWeight.SemiBold)
                    },
                    gameTime = new SpriteText
                    {
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 10, FontWeight.SemiBold)
                    }
                }
            }
        };

        updateMetrics();
    }

    /// <summary>
    /// Recolours the time (<paramref name="foreground"/>) and the running time (<paramref name="highlight"/>).
    /// </summary>
    public void SetColors(Color foreground, Color highlight, double duration = 0)
    {
        realTime.FadeToColor(foreground, duration, Easing.OutQuint);
        runningLabel.FadeToColor(highlight, duration, Easing.OutQuint);
        gameTime.FadeToColor(highlight, duration, Easing.OutQuint);
    }

    protected override void UpdateDisplay(DateTimeOffset now)
    {
        // The game is in English whatever the system's language, so AM and PM are too.
        realTime.Text = now.ToString(use24HourDisplay ? "HH:mm:ss" : "h:mm:ss tt", CultureInfo.InvariantCulture);
        gameTime.Text = $"{new TimeSpan(TimeSpan.TicksPerSecond * (int)(Clock.CurrentTime / 1000)):c}";
    }

    private void updateMetrics()
    {
        runningText.Alpha = showRuntime ? 1 : 0;
    }
}
