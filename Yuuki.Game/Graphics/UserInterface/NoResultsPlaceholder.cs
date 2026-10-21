// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class NoResultsPlaceholder : Container
{
    private const float icon_size = 56;

    /// <summary>
    /// The icon floats up this far and back, every <see cref="bob_duration"/> ms
    /// </summary>
    private const float bob_height = 10;

    private const double bob_duration = 4000;


    /// <summary>
    /// Clears the search text (the "clearing" link).
    /// </summary>
    public Action? RequestClearSearch { get; init; }

    /// <summary>
    /// Opens the level range back up (the "removing" link).
    /// </summary>
    public Action? RequestClearLevelRange { get; init; }

    /// <summary>
    /// Whether it's showing (or on its way in).
    /// </summary>
    public bool IsPresented { get; private set; }

    private readonly YuukiColorPalette palette;

    private readonly Container body;
    private readonly Container iconHolder;
    private readonly IconSprite icon;
    private readonly SpriteText title;
    private readonly SpriteText description;
    private readonly Container suggestions;

    private bool vertical = true;

    // A presentation asked for before load, applied once loaded.
    private bool pendingPresent;
    private BeatmapFilter? pendingFilter;
    private bool pendingLibraryEmpty;

    public NoResultsPlaceholder(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.Both;
        Alpha = 0;

        Child = body = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                iconHolder = new Container
                {
                    Anchor = Anchor.Centre,
                    Size = new Vector2(icon_size),
                    Child = icon = new IconSprite
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = IconUsage.SearchOff,
                        IconSize = icon_size
                    }
                },
                title = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Text = "No matching songs",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 30, FontWeight.Bold)
                },
                description = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 19, FontWeight.Regular),
                    Alpha = 0.85f
                },
                suggestions = new Container
                {
                    Anchor = Anchor.Centre,
                    AutoSizeAxes = Axes.Both
                }
            }
        };

        applyColors();
    }

    // Purely a picture apart from its links, which take their own clicks.
    public override bool HandleNonPositionalInput => false;

    /// <summary>
    /// Stacked for the vertical list; a row for the horizontal strip. Ink either way, on the glass band.
    /// </summary>
    public void SetLayout(CarouselOrientation orientation)
    {
        vertical = orientation == CarouselOrientation.Vertical;
        applyColors();

        foreach (Drawable row in suggestions.Children)
            ((SuggestionRow)row).SetColors(textColor, linkColor);
    }

    // Both lists sit on the light glass band, so it's ink either way.
    private Color textColor => palette.Text;

    private Color linkColor => palette.Control;

    private void applyColors()
    {
        icon.Color = textColor;
        title.Color = textColor;
        description.Color = textColor;
    }

    /// <summary>
    /// Shows it for <paramref name="filter"/>: suggestions for whatever the filter is narrowing by, or,
    /// with nothing in the library at all, just that. Showing it again (the filter changed while it was
    /// up) refreshes the suggestions with a little bounce.
    /// </summary>
    public void Present(BeatmapFilter filter, bool libraryEmpty)
    {
        if (!IsLoaded)
        {
            pendingPresent = true;
            pendingFilter = filter;
            pendingLibraryEmpty = libraryEmpty;
            IsPresented = true;
            return;
        }

        bool appearing = !IsPresented || Alpha < 0.01f;
        IsPresented = true;

        rebuildSuggestions(filter, libraryEmpty);

        body.ClearTransforms();
        body.Scale = new Vector2(0.9f);
        body.ScaleTo(1f, 1000, Easing.OutQuint);

        suggestions.ClearTransforms();
        suggestions.Alpha = 0;
        suggestions.FadeIn(800, Easing.OutQuint);

        if (appearing)
        {
            ClearTransforms();
            this.FadeIn(600, Easing.OutQuint);
        }
    }

    /// <summary>
    /// Hides it (something matches again).
    /// </summary>
    public void Dismiss()
    {
        pendingPresent = false;

        if (!IsPresented)
            return;

        IsPresented = false;

        if (!IsLoaded)
            return;

        ClearTransforms();
        this.FadeOut(200, Easing.OutQuint);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (pendingPresent && pendingFilter != null)
        {
            IsPresented = false;
            Present(pendingFilter, pendingLibraryEmpty);
        }

        pendingPresent = false;
        pendingFilter = null;
    }

    private void rebuildSuggestions(BeatmapFilter filter, bool libraryEmpty)
    {
        suggestions.Clear();

        var rows = new List<SuggestionRow>();

        if (libraryEmpty)
        {
            description.Text = "There are no songs in your library yet.";
        }
        else
        {
            description.Text = "No songs match your search and filters!";

            if (!string.IsNullOrWhiteSpace(filter.Search))
                rows.Add(new SuggestionRow("Try ", "clearing", " your current search.", RequestClearSearch));

            if (filter.MinLevel > 0 || filter.MaxLevel < BeatmapFilter.MAX_LEVEL)
            {
                string upper = filter.MaxLevel >= BeatmapFilter.MAX_LEVEL ? "∞" : filter.MaxLevel.ToString();
                rows.Add(new SuggestionRow("Try ", "removing", $" the Lv {filter.MinLevel} – {upper} level filter.", RequestClearLevelRange));
            }
        }

        foreach (SuggestionRow row in rows)
        {
            row.SetColors(textColor, linkColor);
            suggestions.Add(row);
        }
    }

    public override void Update()
    {
        base.Update();

        icon.Y = -bob_height * (1 - MathF.Cos(MathF.Tau * (float)(Clock.CurrentTime % bob_duration / bob_duration))) / 2;

        layOut();
    }

    /// <summary>
    /// Places everything about the middle of the area. Positioned by hand: it switches between two
    /// arrangements (stacked and centred, or beside the icon) with different gaps between the pieces.
    /// </summary>
    private void layOut()
    {
        // The suggestion rows stack inside their own box, left-aligned in a row, centred when stacked.
        float rowsWidth = 0, rowsHeight = 0;

        foreach (Drawable row in suggestions.Children)
            rowsWidth = Math.Max(rowsWidth, row.DrawSize.X);

        foreach (Drawable row in suggestions.Children)
        {
            row.Anchor = row.Origin = vertical ? Anchor.TopCentre : Anchor.TopLeft;
            row.Y = rowsHeight;
            rowsHeight += row.DrawSize.Y + 2;
        }

        bool hasRows = suggestions.Children.Count > 0;
        float rowsBlock = hasRows ? rowsHeight : 0;

        if (vertical)
        {
            const float icon_gap = 14, title_gap = 6, rows_gap = 18;

            float height = icon_size + icon_gap + title.DrawSize.Y + title_gap + description.DrawSize.Y + (hasRows ? rows_gap + rowsBlock : 0);
            float y = -height / 2;

            place(iconHolder, Anchor.TopCentre, 0, ref y, icon_size + icon_gap);
            place(title, Anchor.TopCentre, 0, ref y, title.DrawSize.Y + title_gap);
            place(description, Anchor.TopCentre, 0, ref y, description.DrawSize.Y + rows_gap);
            place(suggestions, Anchor.TopCentre, 0, ref y, 0);
        }
        else
        {
            // The icon beside a left-aligned column of text, the pair centred on the strip.
            const float icon_gap = 24, line_gap = 2;

            float columnWidth = Math.Max(Math.Max(title.DrawSize.X, description.DrawSize.X), rowsWidth);
            float columnHeight = title.DrawSize.Y + line_gap + description.DrawSize.Y + (hasRows ? 6 + rowsBlock : 0);
            float left = -(icon_size + icon_gap + columnWidth) / 2;
            float textLeft = left + icon_size + icon_gap;

            iconHolder.Origin = Anchor.CentreLeft;
            iconHolder.Position = new Vector2(left, 0);

            float y = -columnHeight / 2;
            place(title, Anchor.TopLeft, textLeft, ref y, title.DrawSize.Y + line_gap);
            place(description, Anchor.TopLeft, textLeft, ref y, description.DrawSize.Y + 6);
            place(suggestions, Anchor.TopLeft, textLeft, ref y, 0);
        }
    }

    private static void place(Drawable drawable, Anchor origin, float x, ref float y, float advance)
    {
        drawable.Origin = origin;
        drawable.Position = new Vector2(x, y);
        y += advance;
    }

    /// <summary>One suggestion: a bullet, then text with a clickable word in it ("Try <u>clearing</u> …").</summary>
    private partial class SuggestionRow : FlowContainer
    {
        private readonly Circle bullet;
        private readonly SpriteText before;
        private readonly SpriteText after;
        private readonly Link link;

        public SuggestionRow(string beforeText, string linkText, string afterText, Action? action)
        {
            AutoSizeAxes = Axes.Both;
            Direction = FlowDirection.Horizontal;

            var font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Regular);

            Children = new Drawable[]
            {
                // Sized like a line of text so it sits on the line's middle.
                new Container
                {
                    Size = new Vector2(16, 24),
                    Child = bullet = new Circle
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(5)
                    }
                },
                before = new SpriteText { Text = beforeText, Font = font },
                link = new Link(linkText, YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold)) { Action = action },
                after = new SpriteText { Text = afterText, Font = font }
            };
        }

        public void SetColors(Color text, Color linkColor)
        {
            bullet.Color = text;
            before.Color = text;
            after.Color = text;
            link.SetColor(linkColor);
        }
    }

    /// <summary>A clickable word, underlined, that lights up under the cursor.</summary>
    private partial class Link : ClickableContainer
    {
        private readonly SpriteText text;
        private readonly Box underline;
        private Color color = Color.White;

        public Link(string label, Sakura.Framework.Graphics.Text.FontUsage font)
        {
            AutoSizeAxes = Axes.Both;

            Children = new Drawable[]
            {
                text = new SpriteText { Text = label, Font = font },
                underline = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Alpha = 0.5f
                }
            };
        }

        public void SetColor(Color value)
        {
            color = value;
            text.Color = value;
            underline.Color = value;
        }

        public override bool OnHover(MouseEvent e)
        {
            text.FadeToColor(color.Lighten(0.3f), 100, Easing.OutQuint);
            underline.FadeTo(1, 100, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            text.FadeToColor(color, 200, Easing.OutQuint);
            underline.FadeTo(0.5f, 200, Easing.OutQuint);
            return base.OnHoverLost(e);
        }
    }
}
