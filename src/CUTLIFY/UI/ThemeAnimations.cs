using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cutlify.Themes;

namespace Cutlify.UI
{
    /// <summary>
    /// Microinteracciones de todos los botones según el estilo: suaves (Clásico/Retro), esponjosas con rebote (Cozy),
    /// a saltos de píxel (Pixel art) o ninguna (Minimalista). Se registra una sola vez para toda la aplicación.
    /// </summary>
    public static class ThemeAnimations
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseEnterEvent, new MouseEventHandler((s, e) => To(s, Hover)));
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent, new MouseEventHandler((s, e) => To(s, 1.0)));
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => To(s, Press)));
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) => To(s, Hover)));
        }

        private static AnimationKind Kind => ThemeManager.Style.Animation;

        private static double Hover => Kind switch { AnimationKind.Bouncy => 1.06, AnimationKind.Smooth => 1.02, _ => 1.0 };
        private static double Press => Kind switch { AnimationKind.Bouncy => 0.9, AnimationKind.Stepped => 0.94, AnimationKind.Smooth => 0.97, _ => 1.0 };

        private static void To(object sender, double scale)
        {
            if (!(sender is FrameworkElement el) || el is RepeatButton || el.ActualWidth > 420) return;
            var st = EnsureScale(el);
            if (st == null) return;
            if (Kind == AnimationKind.None) { st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); return; }

            AnimationTimeline anim;
            switch (Kind)
            {
                case AnimationKind.Bouncy:
                    anim = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(scale < 1 ? 110 : 520))
                    {
                        EasingFunction = scale < 1 ? new QuadraticEase { EasingMode = EasingMode.EaseOut } : new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut }
                    };
                    break;
                case AnimationKind.Stepped:
                    // Dos «fotogramas», como una animación de sprites.
                    var k = new DoubleAnimationUsingKeyFrames();
                    double from = st.ScaleX;
                    k.KeyFrames.Add(new DiscreteDoubleKeyFrame((from + scale) / 2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    k.KeyFrames.Add(new DiscreteDoubleKeyFrame(scale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
                    anim = k;
                    break;
                default:
                    anim = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    break;
            }
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim.Clone());
        }

        private static ScaleTransform EnsureScale(FrameworkElement el)
        {
            if (el.RenderTransform is ScaleTransform s && !s.IsFrozen) return s;
            if (el.RenderTransform != null && el.RenderTransform != Transform.Identity) return null; // no pisar transformaciones existentes
            var st = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st;
            return st;
        }

        /// <summary>Animación de entrada de notificaciones según el estilo.</summary>
        public static IEasingFunction EntranceEasing => Kind switch
        {
            AnimationKind.Bouncy => new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
            AnimationKind.None => null,
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        public static TimeSpan EntranceDuration => Kind switch
        {
            AnimationKind.Bouncy => TimeSpan.FromMilliseconds(480),
            AnimationKind.Stepped => TimeSpan.FromMilliseconds(1),
            AnimationKind.None => TimeSpan.FromMilliseconds(1),
            _ => TimeSpan.FromMilliseconds(260)
        };
    }
}
