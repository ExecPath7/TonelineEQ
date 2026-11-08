// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - a colour wheel: hue around the circle, saturation from the centre out, plus a brightness slider and a hex box.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TonelineEQ
{
    public class ColorWheel : Border
    {
        public event Action<Color> Changed;     // while you drag (cheap: only the wheel's own preview updates)
        public event Action<Color> Committed;   // when you let go, or press Enter in the hex box

        public const int Size = 168;
        double hue, sat, val;
        readonly Ellipse dim, knob;
        readonly Border preview;
        readonly TextBox hexBox;
        readonly Slider bright;
        readonly Grid wheelArea;
        bool dragging, syncing;

        public Color Value { get { return FromHsv(hue, sat, val); } }

        public ColorWheel(Color start)
        {
            ToHsv(start, out hue, out sat, out val);
            Padding = new Thickness(0, 10, 0, 4);

            Grid root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            wheelArea = new Grid { Width = Size, Height = Size, Background = Brushes.Transparent, Cursor = Cursors.Cross, ToolTip = "Drag to pick a color" };
            wheelArea.Children.Add(new Image { Source = WheelBitmap(), Width = Size, Height = Size, IsHitTestVisible = false });
            dim = new Ellipse { Width = Size, Height = Size, Fill = Brushes.Black, IsHitTestVisible = false };
            wheelArea.Children.Add(dim);
            wheelArea.Children.Add(new Ellipse { Width = Size, Height = Size, Stroke = Theme.Br("#38383A"), StrokeThickness = 1, IsHitTestVisible = false });
            Canvas overlay = new Canvas { IsHitTestVisible = false };
            knob = new Ellipse { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 3 };
            knob.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.55 };
            overlay.Children.Add(knob);
            wheelArea.Children.Add(overlay);
            wheelArea.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { dragging = true; wheelArea.CaptureMouse(); PickAt(e.GetPosition(wheelArea)); e.Handled = true; };
            wheelArea.MouseMove += delegate (object s, MouseEventArgs e) { if (dragging) PickAt(e.GetPosition(wheelArea)); };
            wheelArea.MouseLeftButtonUp += delegate { if (!dragging) return; dragging = false; wheelArea.ReleaseMouseCapture(); Commit(); };
            wheelArea.LostMouseCapture += delegate { if (dragging) { dragging = false; Commit(); } };
            root.Children.Add(wheelArea);

            StackPanel side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(side, 2);
            DockPanel top = new DockPanel();
            preview = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), BorderBrush = Theme.Br("#4E4E54"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
            top.Children.Add(preview);
            StackPanel hexCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            hexCol.Children.Add(Ui.Txt("Hex", 11.5, "#8E8E93"));
            hexBox = new TextBox { Width = 96, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0), FontFamily = new FontFamily("Cascadia Code, Consolas") };
            hexBox.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitHex(); e.Handled = true; } };
            hexBox.LostKeyboardFocus += delegate { CommitHex(); };
            hexCol.Children.Add(hexBox);
            top.Children.Add(hexCol);
            side.Children.Add(top);

            TextBlock bl = Ui.Txt("Brightness", 11.5, "#8E8E93");
            bl.Margin = new Thickness(0, 16, 0, 2);
            side.Children.Add(bl);
            bright = new Slider { Minimum = 0, Maximum = 1, SmallChange = 0.01, LargeChange = 0.1, Value = val, ToolTip = "Brightness. Double-click for full." };
            bright.ValueChanged += delegate
            {
                if (syncing) return;
                val = bright.Value;
                Refresh();
                if (Changed != null) Changed(Value);
            };
            bright.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate { Commit(); }));
            bright.PreviewMouseLeftButtonUp += delegate { Commit(); };
            bright.MouseDoubleClick += delegate { bright.Value = 1; Commit(); };
            side.Children.Add(bright);
            root.Children.Add(side);

            Child = root;
            lastCommitted = Value;   // letting go without moving changes nothing
            anyCommit = true;
            Refresh();
        }

        Color lastCommitted;
        bool anyCommit;

        void Commit()
        {
            Color c = Value;
            if (anyCommit && c == lastCommitted) return;
            anyCommit = true;
            lastCommitted = c;
            if (Committed != null) Committed(c);
        }

        void CommitHex()
        {
            Color c;
            if (!Theme.TryParse(hexBox.Text.Trim(), out c)) { hexBox.Text = Theme.Hex(Value); return; }
            if (Theme.Hex(c) == Theme.Hex(Value)) return;
            ToHsv(c, out hue, out sat, out val);
            syncing = true; bright.Value = val; syncing = false;
            Refresh();
            Commit();
        }

        void PickAt(Point p)
        {
            double r = Size / 2.0;
            double dx = p.X - r, dy = p.Y - r;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            sat = Math.Min(1, dist / r);
            hue = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
            if (val < 0.08) { val = 1; syncing = true; bright.Value = 1; syncing = false; }   // picking on a black wheel brings the light back
            Refresh();
            if (Changed != null) Changed(Value);
        }

        void Refresh()
        {
            Color c = Value;
            dim.Opacity = 1 - val;
            double r = Size / 2.0, a = hue * Math.PI / 180;
            Canvas.SetLeft(knob, r + Math.Cos(a) * sat * r - knob.Width / 2);
            Canvas.SetTop(knob, r - Math.Sin(a) * sat * r - knob.Height / 2);
            knob.Fill = new SolidColorBrush(c);
            preview.Background = new SolidColorBrush(c);
            if (!hexBox.IsKeyboardFocused) hexBox.Text = Theme.Hex(c);
            LinearGradientBrush track = new LinearGradientBrush(Colors.Black, FromHsv(hue, sat, 1), 0);
            bright.Background = track;
        }

        // ------------------------------------------------------------ colour maths
        static WriteableBitmap wheel;
        static BitmapSource WheelBitmap()
        {
            if (wheel != null) return wheel;
            int n = Size * 2;   // drawn at 2x so it stays crisp on high-DPI screens
            WriteableBitmap wb = new WriteableBitmap(n, n, 192, 192, PixelFormats.Pbgra32, null);
            int[] px = new int[n * n];
            double r = n / 2.0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double dx = x + 0.5 - r, dy = y + 0.5 - r;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    double alpha = Math.Max(0, Math.Min(1, r - d));
                    if (alpha <= 0) continue;
                    double h = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
                    Color c = FromHsv(h, Math.Min(1, d / r), 1);
                    int A = (int)Math.Round(alpha * 255);
                    int R = (int)Math.Round(c.R * alpha), G = (int)Math.Round(c.G * alpha), B = (int)Math.Round(c.B * alpha);
                    px[y * n + x] = (A << 24) | (R << 16) | (G << 8) | B;
                }
            wb.WritePixels(new Int32Rect(0, 0, n, n), px, n * 4, 0);
            wb.Freeze();
            wheel = wb;
            return wheel;
        }

        public static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        public static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
    }
}
