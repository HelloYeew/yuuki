// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.Cursor;

public partial class YuukiCursorContainer : CursorContainer
{
    /// <summary>
    /// Whether the cursor is tucked away (during play). Bound to the current screen's wish.
    /// </summary>
    public ReactiveBool Hidden { get; } = new ReactiveBool(false);

    public YuukiCursorContainer()
    {
        HideOsCursor = true;
    }

    /// <summary>
    /// Whether a main button is held on the cursor (diagnostics/tests).
    /// </summary>
    public bool IsHeld => (ActiveCursor as MenuCursor)?.IsHeld ?? false;

    /// <summary>
    /// The cursor's current turn, in degrees (diagnostics/tests).
    /// </summary>
    public float CursorRotation => (ActiveCursor as MenuCursor)?.BodyRotation ?? 0;

    /// <summary>
    /// The cursor's current click squash, 1 at rest (diagnostics/tests).
    /// </summary>
    public float CursorScale => (ActiveCursor as MenuCursor)?.BodyScale ?? 1;

    // Called from the base constructor; Hidden is already set up by then (field initialisers run first).
    protected override Drawable CreateCursor() => new MenuCursor(Hidden);

    private partial class MenuCursor : Container, ICursorDrawable
    {
        private const string texture = "Cursor/menu-cursor.png";
        private const string additive_texture = "Cursor/menu-cursor-additive.png";

        /// <summary>
        /// On-screen width at cursor size 1; the height follows the texture's 312×442 shape.
        /// </summary>
        private const float width = 28;

        private static readonly Vector2 texture_size = new Vector2(312, 442);

        /// <summary>
        /// Where the tip sits in the texture, as a fraction of its size.
        /// </summary>
        private static readonly Vector2 tip = new Vector2(0.08f, 0.04f);

        /// <summary>
        /// How far a held drag has to travel before the cursor starts turning.</summary>
        private const float rotation_start_distance = 80;

        /// <summary>Past this distance the rotation centre trails after the pointer.</summary>
        private const float rotation_centre_leash = 60;

        /// <summary>
        /// How quickly the trailing rotation centre catches up, as a time constant in ms.
        /// </summary>
        private const double rotation_centre_follow = 150;

        /// <summary>
        /// The cursor's resting tilt, so it points along the drag rather than past it.
        /// </summary>
        private const float rotation_offset = 24.3f;

        private readonly ReactiveBool hidden;

        [Resolved(canBeNull: true)]
        private ITextureManager? textureManager { get; set; }

        [Resolved(canBeNull: true)]
        private YuukiConfigManager? config { get; set; }

        [Resolved(canBeNull: true)]
        private UISoundPlayer? uiSounds { get; set; }

        private Container sizeContainer = null!;
        // Hide/show: alpha and scale.
        private Container visibility = null!;
        // Drag rotation only.
        private Container rotator = null!;
        // Click squash only.
        private Container squash = null!;
        // The glow: alpha only.
        private Sprite additive = null!;

        private Reactive<double>? cursorSize;
        private Reactive<bool>? cursorRotate;

        private bool pressed;
        private DragState dragState;
        private Vector2 pressPosition;
        private Vector2 lastMovePosition;

        private enum DragState
        {
            None,
            Started,
            Rotating
        }

        public MenuCursor(ReactiveBool hidden)
        {
            this.hidden = hidden;

            // Nominal sizes throughout: the sprites hang off the origin (the tip), and a zero-size
            // container has tripped drawing up before (see the drawable sizing notes).
            Size = Vector2.One;
        }

        /// <summary>
        /// The cursor isn't something the pointer points at.
        /// </summary>
        public override bool HandlePositionalInput => false;

        public bool IsHeld => pressed;

        public float BodyRotation => rotator?.Rotation ?? 0;

        public float BodyScale => squash?.Scale.X ?? 1;

        [BackgroundDependencyLoader]
        private void load([CanBeNull] IYuukiColorProvider? colorProvider)
        {
            YuukiColorPalette palette = colorProvider.IsNotNull()
                ? colorProvider!.GetPalette(YuukiScreenPart.MainMenu)
                : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.MainMenu);

            var size = new Vector2(width, width * texture_size.Y / texture_size.X);

            // The setting's scale sits outside the animated layers so they never fight over it.
            Child = sizeContainer = new Container
            {
                Size = Vector2.One,
                Child = visibility = new Container
                {
                    Size = Vector2.One,
                    Child = rotator = new Container
                    {
                        Size = Vector2.One,
                        Child = squash = new Container
                        {
                            Size = Vector2.One,
                            Children = new Drawable[]
                            {
                                new Sprite
                                {
                                    Position = -tip * size,
                                    Size = size,
                                    Texture = textureManager?.Get(texture)
                                },
                                additive = new Sprite
                                {
                                    Position = -tip * size,
                                    Size = size,
                                    Texture = textureManager?.Get(additive_texture),
                                    Blending = BlendingMode.Additive,
                                    Color = palette.Accent,
                                    Alpha = 0
                                }
                            }
                        }
                    }
                }
            };

            if (config != null)
            {
                cursorSize = config.Get<double>(YuukiSetting.CursorSize);
                cursorRotate = config.Get<bool>(YuukiSetting.CursorRotate);

                // Both settings outlive this cursor only as long as the game does, as does the cursor.
                cursorSize.BindValueChanged(e => sizeContainer.Scale = new Vector2((float)e.NewValue), true);
            }

            hidden.BindValueChanged(e =>
            {
                visibility.ClearTransforms();

                if (e.NewValue)
                    visibility.FadeTo(0, 250, Easing.OutQuint).ScaleTo(0.6f, 250, Easing.InQuad);
                else
                    visibility.FadeTo(1, 250, Easing.OutQuint).ScaleTo(1f, 400, Easing.OutQuint);

                if (dragState == DragState.None)
                    rotate(0, 400, Easing.OutQuint);
            }, true);
        }

        public void ChangeCursor(CursorState state)
        {

        }

        /// <summary>A press anywhere (the container hears every one): squash, glow, maybe start a drag.</summary>
        public void ButtonPressed(MouseButton button)
        {
            if (button is not (MouseButton.Left or MouseButton.Right) || hidden.Value)
                return;

            pressed = true;
            uiSounds?.Play(UISound.CursorTap);

            squash.ClearTransforms();
            squash.Scale = Vector2.One;
            squash.ScaleTo(0.9f, 800, Easing.OutQuint);

            additive.ClearTransforms();
            additive.Alpha = 0;
            additive.FadeIn(800, Easing.OutQuint);

            // Already turning: keep the center it's turning about.
            if ((cursorRotate?.Value ?? true) && dragState != DragState.Rotating)
            {
                pressPosition = lastMovePosition = mousePosition();
                dragState = DragState.Started;
            }
        }

        public override void Update()
        {
            base.Update();

            if (dragState != DragState.None)
            {
                Vector2 mouse = mousePosition();

                if (mouse != lastMovePosition)
                    mouseMoved(mouse);
            }

            if (pressed)
            {
                InputState? state = GetContainingInputManager()?.CurrentState;

                // The container only reports presses, so the release is found by watching the buttons.
                if (state == null || (!state.IsPressed(MouseButton.Left) && !state.IsPressed(MouseButton.Right)))
                    release();
            }

            // A long drag drags the rotation centre along behind it, so the cursor points the way the
            // pointer is going now rather than back at where the press was.
            if (dragState != DragState.None && Vector2.Distance(pressPosition, lastMovePosition) > rotation_centre_leash)
            {
                float amount = (float)(1 - Math.Exp(-Clock.ElapsedFrameTime / rotation_centre_follow));
                pressPosition = Vector2.Lerp(pressPosition, lastMovePosition, amount);
            }
        }

        private void mouseMoved(Vector2 mouse)
        {
            lastMovePosition = mouse;

            Vector2 offset = mouse - pressPosition;
            float distance = offset.Length();

            // Not until it's moved a fair way from the press, or every click would twitch it.
            if (dragState == DragState.Started && distance > rotation_start_distance)
                dragState = DragState.Rotating;

            if (dragState != DragState.Rotating || distance <= 0)
                return;

            float degrees = MathF.Atan2(-offset.X, offset.Y) * 180 / MathF.PI + rotation_offset;

            // Always turn the short way round.
            float difference = (degrees - rotator.Rotation) % 360;
            if (difference < -180) difference += 360;
            if (difference > 180) difference -= 360;

            rotate(rotator.Rotation + difference, 120, Easing.OutQuint);
        }

        private void release()
        {
            pressed = false;
            uiSounds?.Play(UISound.CursorTap, 0.8, 0.8);

            additive.ClearTransforms();
            additive.Alpha = 1;
            additive.FadeOut(500, Easing.OutQuint);

            squash.ClearTransforms();
            squash.ScaleTo(1f, 500, Easing.OutElastic);

            if (dragState != DragState.None)
            {
                // A bigger turn takes a little longer to swing home.
                rotate(0, 400 * (0.5f + Math.Abs(rotator.Rotation / 960)), Easing.OutElasticQuarter);
                dragState = DragState.None;
            }
        }

        private void rotate(float degrees, double duration, Easing easing)
        {
            rotator.ClearTransforms();
            rotator.RotateTo(degrees, duration, easing);
        }

        private Vector2 mousePosition()
        {
            InputState? state = GetContainingInputManager()?.CurrentState;
            return state == null || Parent == null ? Vector2.Zero : Parent.ToLocalSpace(state.MousePosition);
        }
    }
}
