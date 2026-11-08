// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - app-wide motion: smooth (eased) mouse-wheel scrolling, menus and tooltips that pop in,
// panels that slide and fade in/out, and rows that cascade in. Follows Windows' "Animation effects" setting.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TonelineEQ
{
    public static class Motion
    {
        // Off when the user turned off "Animation effects" in Windows (Settings > Accessibility > Visual effects).
        public static bool On
        {
            get { try { return SystemParameters.ClientAreaAnimation; } catch { return true; } }
        }

        static bool hooked;
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
        }

        // ------------------------------------------------------------ pop-in for menus and tooltips
        static void OnPopupOpened(object sender, RoutedEventArgs e)
        {
            FrameworkElement el = sender as FrameworkElement;
            if (el == null || e.OriginalSource != el || !On) return;
            bool menu = el is ContextMenu;
            el.RenderTransformOrigin = new Point(0.5, 0);
            ScaleTransform st = new ScaleTransform(menu ? 0.94 : 0.97, menu ? 0.94 : 0.97);
            el.RenderTransform = st;
            Duration d = new Duration(TimeSpan.FromMilliseconds(menu ? 300 : 180));
            DoubleAnimation s = new DoubleAnimation(1, d);
            s.EasingFunction = menu ? (IEasingFunction)new SpringEase() : new CubicEase { EasingMode = EasingMode.EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, s);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, s);
            DoubleAnimation o = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(menu ? 140 : 160)));
            o.FillBehavior = FillBehavior.Stop;
            el.BeginAnimation(UIElement.OpacityProperty, o);
        }

        // ------------------------------------------------------------ smooth scrolling
        class ScrollState
        {
            public ScrollViewer Sv;
            public double Target, LastSet;
            public TimeSpan LastTick;
            public bool Running;
        }

        static readonly Dictionary<ScrollViewer, ScrollState> scrolls = new Dictionary<ScrollViewer, ScrollState>();
        static readonly List<ScrollState> running = new List<ScrollState>();
        static bool ticking;

        static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer sv = sender as ScrollViewer;
            if (sv == null || e.Handled || !On) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (sv.ScrollableHeight <= 0 || !PixelScroll(sv)) return;
            // Let an inner scroll area or the EQ graph (wheel = band width) take the wheel first.
            DependencyObject d = e.OriginalSource as DependencyObject;
            while (d != null && d != sv)
            {
                if (d is ResponseGraph) return;
                ScrollViewer inner = d as ScrollViewer;
                if (inner != null && inner.ScrollableHeight > 0) return;
                d = Parent(d);
            }
            if (d == null) return;

            ScrollState s;
            if (!scrolls.TryGetValue(sv, out s))
            {
                s = new ScrollState { Sv = sv };
                scrolls[sv] = s;
                sv.Unloaded += delegate { Stop(s); scrolls.Remove(sv); };
            }
            if (!s.Running || Math.Abs(sv.VerticalOffset - s.LastSet) > 1) s.Target = sv.VerticalOffset;
            double step = e.Delta / 120.0 * Math.Max(48, Math.Min(140, sv.ViewportHeight * 0.18));
            double target = Math.Max(0, Math.Min(sv.ScrollableHeight, s.Target - step));
            e.Handled = true;
            if (Math.Abs(target - sv.VerticalOffset) < 0.5) return;
            s.Target = target;
            s.LastSet = sv.VerticalOffset;
            s.LastTick = TimeSpan.Zero;
            if (!s.Running) { s.Running = true; running.Add(s); }
            if (!ticking) { ticking = true; CompositionTarget.Rendering += Tick; }
        }

        // Pixel scrolling only: list boxes are switched to pixel units in Theme, plain content always is.
        static bool PixelScroll(ScrollViewer sv)
        {
            if (!sv.CanContentScroll) return true;
            ItemsControl ic = sv.TemplatedParent as ItemsControl;
            return ic != null && VirtualizingPanel.GetScrollUnit(ic) == ScrollUnit.Pixel;
        }

        static DependencyObject Parent(DependencyObject d)
        {
            if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
            {
                DependencyObject p = VisualTreeHelper.GetParent(d);
                if (p != null) return p;
            }
            return LogicalTreeHelper.GetParent(d);
        }

        static void Tick(object sender, EventArgs e)
        {
            RenderingEventArgs re = e as RenderingEventArgs;
            TimeSpan now = re != null ? re.RenderingTime : TimeSpan.Zero;
            for (int i = running.Count - 1; i >= 0; i--)
            {
                ScrollState s = running[i];
                ScrollViewer sv = s.Sv;
                // The user grabbed the scrollbar or something else scrolled: stop following.
                if (!sv.IsLoaded || Math.Abs(sv.VerticalOffset - s.LastSet) > 1.5) { Stop(s); continue; }
                double dt = s.LastTick == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, Math.Max(0.001, (now - s.LastTick).TotalSeconds));
                s.LastTick = now;
                double diff = s.Target - sv.VerticalOffset;
                double next;
                if (Math.Abs(diff) < 0.6) next = s.Target;
                else next = sv.VerticalOffset + diff * (1 - Math.Exp(-dt * 16));
                sv.ScrollToVerticalOffset(next);
                s.LastSet = next;
                if (next == s.Target) Stop(s);
            }
            if (running.Count == 0 && ticking) { ticking = false; CompositionTarget.Rendering -= Tick; }
        }

        static void Stop(ScrollState s)
        {
            s.Running = false;
            running.Remove(s);
        }

        public static int ActiveScrolls { get { return running.Count; } }

        // ------------------------------------------------------------ show / hide with motion
        // Shows or hides an element: fades and slides in, or fades out and then collapses.
        public static void Reveal(UIElement el, bool show)
        {
            if (el == null) return;
            bool visible = el.Visibility == Visibility.Visible;
            bool pending = hiding.ContainsKey(el);
            if (show)
            {
                if (visible && !pending) return;
                hiding.Remove(el);
                el.Visibility = Visibility.Visible;
                Slide(el, 0, -6, 260, 0);
                return;
            }
            if (!visible || pending) return;
            if (!On || !el.IsVisible) { el.Visibility = Visibility.Collapsed; return; }
            hiding[el] = true;
            DoubleAnimation o = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(150)));
            o.Completed += delegate
            {
                if (!hiding.ContainsKey(el)) return;
                hiding.Remove(el);
                el.BeginAnimation(UIElement.OpacityProperty, null);
                el.Visibility = Visibility.Collapsed;
            };
            el.BeginAnimation(UIElement.OpacityProperty, o);
        }
        static readonly Dictionary<UIElement, bool> hiding = new Dictionary<UIElement, bool>();

        // Fades an element in while it glides dx/dy pixels into place, after an optional delay.
        public static void Slide(UIElement el, double dx, double dy, double ms, double delayMs)
        {
            if (el == null) return;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            if (!On) { el.Opacity = 1; return; }
            Duration d = new Duration(TimeSpan.FromMilliseconds(ms));
            TimeSpan delay = TimeSpan.FromMilliseconds(delayMs);
            DoubleAnimation op = new DoubleAnimation(0, 1, d);
            op.BeginTime = delay;
            op.FillBehavior = FillBehavior.Stop;
            if (delayMs > 0) el.Opacity = 0;
            op.Completed += delegate { el.Opacity = 1; };
            el.BeginAnimation(UIElement.OpacityProperty, op);
            if (dx == 0 && dy == 0) return;
            TranslateTransform t = new TranslateTransform(dx, dy);
            el.RenderTransform = t;
            IEasingFunction ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            if (dx != 0)
            {
                DoubleAnimation mx = new DoubleAnimation(dx, 0, d) { BeginTime = delay, EasingFunction = ease };
                t.BeginAnimation(TranslateTransform.XProperty, mx);
            }
            if (dy != 0)
            {
                DoubleAnimation my = new DoubleAnimation(dy, 0, d) { BeginTime = delay, EasingFunction = ease };
                t.BeginAnimation(TranslateTransform.YProperty, my);
            }
        }

        // Rows cascade in one after another (the whole cascade stays under ~0.3 s).
        public static void Cascade(Panel p)
        {
            if (p == null || !On) return;
            int n = p.Children.Count;
            double gap = n <= 1 ? 0 : Math.Min(28, 220.0 / (n - 1));
            for (int i = 0; i < n; i++) Slide(p.Children[i], 0, 8, 240, i * gap);
        }

        // Briefly scales an element up and back, e.g. when a value is applied.
        public static void Pulse(UIElement el)
        {
            if (el == null || !On) return;
            ScaleTransform st = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st;
            DoubleAnimation a = new DoubleAnimation(1.06, 1, new Duration(TimeSpan.FromMilliseconds(380)));
            a.EasingFunction = new SpringEase();
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }
}
