// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - the EQ graph (curve, draggable bands) and the live spectrum under it, styled after the AirPods EQ:
// colour follows level, not frequency. Bars run blue at the centre line through cyan, mint and yellow to orange
// at their tips; the curve is mint at 0 dB, turns yellow and orange as it boosts and cyan and blue as it cuts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TonelineEQ
{
    public static class Plot
    {
        public const double MinF = 20, MaxF = 20000;
        public static readonly double LMin = Math.Log10(MinF), LMax = Math.Log10(MaxF);
        public static readonly Stopwatch Clock = Stopwatch.StartNew();

        // Drawing area shared by the spectrum and the graph: no axis numbers, region names underneath.
        public static Rect For(double w, double h) { return new Rect(12, 12, Math.Max(10, w - 24), Math.Max(10, h - 44)); }
        public static double X(Rect p, double f) { return p.Left + (Math.Log10(f) - LMin) / (LMax - LMin) * p.Width; }

        static Typeface bold;
        static string boldFont;
        static Typeface Bold
        {
            get
            {
                if (bold == null || boldFont != Theme.FontName) { bold = new Typeface(Theme.Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal); boldFont = Theme.FontName; }
                return bold;
            }
        }

        public static FormattedText Text(string s, double size, Brush br) { return Text(s, size, br, false); }

        public static FormattedText Text(string s, double size, Brush br, bool semiBold)
        {
#pragma warning disable 618
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, semiBold ? Bold : Theme.Face, size, br);
#pragma warning restore 618
        }

        public static void DrawText(DrawingContext dc, string s, double size, Brush br, Point at, double alignX)
        {
            FormattedText ft = Text(s, size, br);
            dc.DrawText(ft, new Point(at.X - ft.Width * alignX, at.Y - ft.Height / 2));
        }

        public static StreamGeometry Line(IList<Point> pts, bool closedFill)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(pts[0], closedFill, closedFill);
                c.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            g.Freeze();
            return g;
        }

        public static string Freq(double f)
        {
            return f >= 1000 ? (f / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " kHz" : f.ToString("0", CultureInfo.InvariantCulture) + " Hz";
        }

        public static Color WithAlpha(Color c, double a)
        {
            c.A = (byte)Math.Max(0, Math.Min(255, Math.Round(255 * a)));
            return c;
        }

        public static double EaseOut(double t) { t = Math.Max(0, Math.Min(1, t)); return 1 - Math.Pow(1 - t, 3); }
    }

    // The level palette: index 0 is the quietest/lowest (blue), 4 the loudest/highest (orange).
    public static class LevelColors
    {
        public static Color[] Stops(int mode)
        {
            if (mode == 3)
            {
                if (Theme.VizStops != null && Theme.VizStops.Length == 5) return (Color[])Theme.VizStops.Clone();
                mode = 0;
            }
            if (mode == 1)
            {
                Color a = Theme.Accent;
                return new Color[] { Theme.Lerp(a, Theme.Bg, 0.3), a, Theme.Lerp(a, Colors.White, 0.25), Theme.Lerp(a, Colors.White, 0.45), Theme.Lerp(a, Colors.White, 0.62) };
            }
            if (mode == 2)
            {
                Color t = Theme.Text;
                return new Color[] { Plot.WithAlpha(t, 0.35), Plot.WithAlpha(t, 0.5), Plot.WithAlpha(t, 0.68), Plot.WithAlpha(t, 0.84), t };
            }
            if (Theme.IsLight)
                return new Color[] { Theme.Parse("#007AFF"), Theme.Parse("#12A8E0"), Theme.Parse("#1FBF8F"), Theme.Parse("#E3B400"), Theme.Parse("#F5850A") };
            return new Color[] { Theme.Parse("#2F7BFF"), Theme.Parse("#3CC8F5"), Theme.Parse("#7CEBB4"), Theme.Parse("#F5E25C"), Theme.Parse("#FF9F43") };
        }

        // u from -1 (deepest cut, blue) through 0 (flat, mint) to 1 (biggest boost, orange).
        public static Color At(double u, int mode)
        {
            Color[] s = Stops(mode);
            double x = (Math.Max(-1, Math.Min(1, u)) + 1) * 2;   // 0..4
            int i = Math.Min(3, (int)Math.Floor(x));
            Color c = Theme.Lerp(s[i], s[i + 1], x - i);
            c.A = (byte)Math.Round(s[i].A + (s[i + 1].A - s[i].A) * (x - i));
            return c;
        }
    }

    // Live spectrum, redrawn every frame. Sits under the EQ graph and ignores the mouse.
    public class SpectrumLayer : FrameworkElement
    {
        public double[] Levels;
        public double[] Freqs;
        public double RangeDb = 80;
        public bool Bars = true;
        public int ColorMode;

        Brush barBrush;
        double brushTop = double.NaN, brushBottom;
        Pen barPen;
        double penWidth = -1;
        int[] groupStart, groupEnd;
        int groupN = -1, groupSrc = -1;

        public SpectrumLayer() { IsHitTestVisible = false; }

        public void ResetColors() { barBrush = null; barPen = null; InvalidateVisual(); }

        // Mirrored gradient: blue at the centre line, orange at the very tips, so taller bars reach warmer colours.
        Brush BarBrush(double top, double bottom)
        {
            if (barBrush != null && top == brushTop && bottom == brushBottom) return barBrush;
            Color[] s = LevelColors.Stops(ColorMode);
            LinearGradientBrush g = new LinearGradientBrush();
            g.MappingMode = BrushMappingMode.Absolute;
            g.StartPoint = new Point(0, top);
            g.EndPoint = new Point(0, bottom);
            double[] at = { 0, 0.35, 0.55, 0.75, 1 };
            for (int k = 4; k >= 0; k--) g.GradientStops.Add(new GradientStop(s[k], 0.5 - at[k] / 2));
            for (int k = 1; k <= 4; k++) g.GradientStops.Add(new GradientStop(s[k], 0.5 + at[k] / 2));
            g.Freeze();
            barBrush = g; brushTop = top; brushBottom = bottom; barPen = null;
            return g;
        }

        // Which analyser columns feed each bar; the columns are evenly spaced in log frequency, like the bars.
        void Group(int n, int src)
        {
            if (n == groupN && src == groupSrc) return;
            groupStart = new int[n];
            groupEnd = new int[n];
            for (int j = 0; j < n; j++)
            {
                int a = (int)Math.Floor((double)j * src / n), b = (int)Math.Floor((double)(j + 1) * src / n);
                a = Math.Min(a, src - 1);
                groupStart[j] = a;
                groupEnd[j] = Math.Max(a + 1, Math.Min(b, src));
            }
            groupN = n; groupSrc = src;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Levels == null || Freqs == null || Levels.Length != Freqs.Length || Levels.Length < 2) return;
            Rect p = Plot.For(ActualWidth, ActualHeight);
            double cy = p.Top + p.Height / 2, maxHalf = p.Height / 2;
            if (Bars)
            {
                int n = (int)ApoFormat.Clamp(Math.Round(p.Width / 12.5), 24, 110);
                Group(n, Levels.Length);
                double step = p.Width / n, w = Math.Max(2, step * 0.6);
                Brush br = BarBrush(cy - maxHalf, cy + maxHalf);
                if (barPen == null || penWidth != w)
                {
                    barPen = new Pen(br, w);
                    barPen.StartLineCap = PenLineCap.Round;
                    barPen.EndLineCap = PenLineCap.Round;
                    barPen.Freeze();
                    penWidth = w;
                }
                StreamGeometry geo = new StreamGeometry();
                using (StreamGeometryContext c = geo.Open())
                {
                    for (int j = 0; j < n; j++)
                    {
                        double lv = double.MinValue;
                        for (int k = groupStart[j]; k < groupEnd[j]; k++) if (Levels[k] > lv) lv = Levels[k];
                        double t = ApoFormat.Clamp((lv + RangeDb) / RangeDb, 0, 1);
                        // One continuous bar, mirrored around the centre line; round caps add w/2 at each end.
                        double half = Math.Max(0.01, t * maxHalf - w / 2);
                        double x = p.Left + step * (j + 0.5);
                        c.BeginFigure(new Point(x, cy - half), false, false);
                        c.LineTo(new Point(x, cy + half), true, false);
                    }
                }
                geo.Freeze();
                dc.PushOpacity(0.9);
                dc.DrawGeometry(null, barPen, geo);
                dc.Pop();
            }
            else
            {
                List<Point> pts = new List<Point>(Levels.Length);
                for (int i = 0; i < Levels.Length; i++)
                {
                    double t = ApoFormat.Clamp((Levels[i] + RangeDb) / RangeDb, 0, 1);
                    pts.Add(new Point(Plot.X(p, Freqs[i]), p.Bottom - t * p.Height * 0.95));
                }
                dc.PushClip(new RectangleGeometry(p));
                Pen pen = new Pen(Theme.Solid(Plot.WithAlpha(LevelColors.Stops(ColorMode)[1], 0.85)), 2);
                pen.LineJoin = PenLineJoin.Round;
                dc.DrawGeometry(null, pen, Plot.Line(pts, false));
                dc.Pop();
            }
        }
    }

    // EQ curve with draggable band dots. Animates: the curve morphs between presets, dots grow under the mouse.
    public class ResponseGraph : FrameworkElement
    {
        public List<Band> Bands = new List<Band>();
        public int Selected = -1;
        public double RangeDb = 15;
        public PageKind Kind = PageKind.Output;
        public int ColorMode;
        public bool SimpleLabels;   // LOW / MID / HIGH under the three dots of a simple EQ
        public event Action<int> BandDragged;
        public event Action<int> BandSelected;
        public event Action<double, double> AddRequested;

        int dragging = -1;
        int hover = -1;

        const double DotR = 8, DotHoverR = 10.5;
        const double MorphMs = 260;

        // Each band's curve is only recalculated when that band or the graph size changes.
        class CurveCache { public FilterType T; public double F, G, Q, W, L; public int N; public double[] Db; }
        readonly Dictionary<Band, CurveCache> curves = new Dictionary<Band, CurveCache>();

        // Animation state
        double[] lastDb, morphFrom;
        Point[] lastDots, morphDots;
        double morphStart;
        readonly List<double> radius = new List<double>();
        double lastTick;
        bool ticking;

        double[] BandCurve(Band b, double[] fs, int n, Rect p)
        {
            CurveCache c;
            if (curves.TryGetValue(b, out c) && c.T == b.Type && c.F == b.Freq && c.G == b.Gain && c.Q == b.Q && c.N == n && c.W == p.Width && c.L == p.Left) return c.Db;
            if (curves.Count > Bands.Count * 2 + 8) curves.Clear();
            double[] co = Dsp.Coeffs(b);
            double[] db = new double[n + 1];
            for (int i = 0; i <= n; i++) db[i] = Dsp.MagDb(co, fs[i]);
            if (c == null) { c = new CurveCache(); curves[b] = c; }
            c.T = b.Type; c.F = b.Freq; c.G = b.Gain; c.Q = b.Q; c.N = n; c.W = p.Width; c.L = p.Left; c.Db = db;
            return db;
        }

        Rect Area() { return Plot.For(ActualWidth, ActualHeight); }
        double FAt(Rect p, double x)
        {
            double t = ApoFormat.Clamp((x - p.Left) / p.Width, 0, 1);
            return Math.Pow(10, Plot.LMin + t * (Plot.LMax - Plot.LMin));
        }
        double Y(Rect p, double db)
        {
            db = ApoFormat.Clamp(db, -RangeDb - 2, RangeDb + 2);
            return p.Top + p.Height / 2 - db / RangeDb * (p.Height / 2);
        }
        double DbAt(Rect p, double y) { return -(y - (p.Top + p.Height / 2)) / (p.Height / 2) * RangeDb; }

        // Shelves are drawn on the curve, at half their gain (where a shelf crosses its own frequency).
        static bool IsShelf(Band b) { return b.Type == FilterType.LowShelf || b.Type == FilterType.HighShelf; }
        double HandleDb(Band b) { return b.HasGain ? (IsShelf(b) ? b.Gain / 2 : b.Gain) : -3; }
        Point HandlePoint(Rect p, Band b) { return new Point(Plot.X(p, b.Freq), Y(p, HandleDb(b))); }

        int HitTestBand(Rect p, Point pt)
        {
            int best = -1;
            double bestD = 14;
            for (int i = Bands.Count - 1; i >= 0; i--)
            {
                Point h = HandlePoint(p, Bands[i]);
                double d = Math.Sqrt((h.X - pt.X) * (h.X - pt.X) + (h.Y - pt.Y) * (h.Y - pt.Y));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Call before the bands change (preset, undo, reset): the curve then glides to its new shape.
        public void Morph()
        {
            if (lastDb == null) return;
            morphFrom = (double[])lastDb.Clone();
            morphDots = lastDots != null ? (Point[])lastDots.Clone() : null;
            morphStart = Plot.Clock.Elapsed.TotalMilliseconds;
            StartTicking();
        }

        void StartTicking()
        {
            if (ticking) return;
            ticking = true;
            lastTick = Plot.Clock.Elapsed.TotalMilliseconds;
            CompositionTarget.Rendering += Tick;
        }

        void Tick(object sender, EventArgs e)
        {
            // Stop if the graph was removed or hidden (window closed to the tray, theme rebuilt).
            if (!IsVisible || PresentationSource.FromVisual(this) == null) { morphFrom = null; morphDots = null; StopTicking(); return; }
            InvalidateVisual();
        }

        void StopTicking()
        {
            if (!ticking) return;
            ticking = false;
            CompositionTarget.Rendering -= Tick;
        }

        protected override void OnRender(DrawingContext dc)
        {
            Rect p = Area();
            double now = Plot.Clock.Elapsed.TotalMilliseconds, dt = Math.Min(0.1, Math.Max(0, (now - lastTick) / 1000.0));
            lastTick = now;
            bool keepTicking = false;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            Color text = Theme.Text;
            double cy = p.Top + p.Height / 2;

            // Soft light behind the selected band.
            if (Selected >= 0 && Selected < Bands.Count)
            {
                double sx = Plot.X(p, Bands[Selected].Freq);
                RadialGradientBrush glow = new RadialGradientBrush();
                glow.MappingMode = BrushMappingMode.Absolute;
                glow.Center = new Point(sx, cy);
                glow.GradientOrigin = glow.Center;
                glow.RadiusX = 80;
                glow.RadiusY = p.Height * 0.62;
                glow.GradientStops.Add(new GradientStop(Plot.WithAlpha(text, Theme.IsLight ? 0.05 : 0.07), 0));
                glow.GradientStops.Add(new GradientStop(Plot.WithAlpha(text, 0), 1));
                glow.Freeze();
                dc.DrawRectangle(glow, null, new Rect(sx - 80, p.Top, 160, p.Height));
            }

            // Quiet dB grid: the 0 dB line a little stronger, +-6 and +-12 dB faint, labels on the right.
            Pen gridZero = new Pen(Theme.Solid(Plot.WithAlpha(text, 0.13)), 1);
            Pen gridLine = new Pen(Theme.Solid(Plot.WithAlpha(text, 0.055)), 1);
            Brush gridLabel = Theme.Solid(Plot.WithAlpha(text, 0.4));
            List<KeyValuePair<FormattedText, Point>> gridTexts = new List<KeyValuePair<FormattedText, Point>>();
            foreach (double mark in new double[] { 12, 6, 0, -6, -12 })
            {
                if (Math.Abs(mark) > RangeDb) continue;
                double gy = Math.Round(Y(p, mark)) + 0.5;
                dc.DrawLine(mark == 0 ? gridZero : gridLine, new Point(p.Left, gy), new Point(p.Right, gy));
                string gl = (mark > 0 ? "+" : "") + mark.ToString("0", CultureInfo.InvariantCulture) + (mark == 0 ? " dB" : "");
                FormattedText gft = Plot.Text(gl, 9.5, gridLabel);
                double ly = gy - gft.Height - 3;
                if (ly < 1) ly = gy + 3;   // the top line (e.g. +12 on a +-12 dB graph): label goes underneath
                gridTexts.Add(new KeyValuePair<FormattedText, Point>(gft, new Point(p.Right - gft.Width - 2, ly)));
            }

            // Dashed guide at every band; region names underneath, like LOW / MID / HIGH.
            Pen dash = new Pen(Theme.Solid(Plot.WithAlpha(text, 0.22)), 1);
            dash.DashStyle = new DashStyle(new double[] { 3, 4 }, 0);
            for (int k = 0; k < Bands.Count; k++)
            {
                if (!Bands[k].Enabled) continue;
                double x = Math.Round(Plot.X(p, Bands[k].Freq)) + 0.5;
                dc.DrawLine(dash, new Point(x, p.Top), new Point(x, p.Bottom));
            }
            string[] regionNames = Help.RegionLabels(Kind);
            double[] regionEdges = Help.RegionEdges(Kind);
            Brush label = Theme.Br("#8E8E93");
            if (SimpleLabels && Bands.Count == 3)
            {
                string[] three = { "LOW", "MID", "HIGH" };
                for (int k = 0; k < 3; k++)
                {
                    FormattedText ft = Plot.Text(three[k], 14, label, true);
                    dc.DrawText(ft, new Point(Plot.X(p, Bands[k].Freq) - ft.Width / 2, p.Bottom + 9));
                }
                regionNames = new string[0];
            }
            for (int ri = 0; ri < regionNames.Length; ri++)
            {
                double rx = (Plot.X(p, regionEdges[ri]) + Plot.X(p, regionEdges[ri + 1])) / 2;
                FormattedText ft = Plot.Text(regionNames[ri], 11, label, true);
                dc.DrawText(ft, new Point(rx - ft.Width / 2, p.Bottom + 13));
            }

            // Sum the bands.
            int n = (int)Math.Max(64, p.Width / 2);
            double[] xs = new double[n + 1], fs = new double[n + 1], total = new double[n + 1];
            for (int i = 0; i <= n; i++) { xs[i] = p.Left + p.Width * i / n; fs[i] = FAt(p, xs[i]); }
            for (int k = 0; k < Bands.Count; k++)
            {
                Band b = Bands[k];
                if (!b.Enabled) continue;
                double[] bc = BandCurve(b, fs, n, p);
                for (int i = 0; i <= n; i++) total[i] += bc[i];
            }
            Point[] dots = new Point[Bands.Count];
            for (int k = 0; k < Bands.Count; k++) dots[k] = HandlePoint(p, Bands[k]);

            // Glide from the previous shape after a preset change.
            if (morphFrom != null)
            {
                double e = Plot.EaseOut((now - morphStart) / MorphMs);
                if (morphFrom.Length == total.Length)
                    for (int i = 0; i <= n; i++) total[i] = morphFrom[i] + (total[i] - morphFrom[i]) * e;
                if (morphDots != null && morphDots.Length == dots.Length)
                    for (int k = 0; k < dots.Length; k++) dots[k] = new Point(morphDots[k].X + (dots[k].X - morphDots[k].X) * e, morphDots[k].Y + (dots[k].Y - morphDots[k].Y) * e);
                if (e >= 1) { morphFrom = null; morphDots = null; } else keepTicking = true;
            }
            lastDb = total;
            lastDots = dots;

            // The curve: a dark outline so it reads over the bars, then the level-coloured line.
            List<Point> curve = new List<Point>(n + 1);
            for (int i = 0; i <= n; i++) curve.Add(new Point(xs[i], Y(p, total[i])));
            StreamGeometry geo = Plot.Line(curve, false);
            Color[] st = LevelColors.Stops(ColorMode);

            // A soft glow fills the space between the curve and 0 dB: warm above, cool below.
            List<Point> area = new List<Point>(curve);
            area.Add(new Point(xs[n], cy));
            area.Add(new Point(xs[0], cy));
            LinearGradientBrush fill = new LinearGradientBrush();
            fill.MappingMode = BrushMappingMode.Absolute;
            fill.StartPoint = new Point(0, Y(p, RangeDb * 0.8));
            fill.EndPoint = new Point(0, Y(p, -RangeDb * 0.8));
            fill.GradientStops.Add(new GradientStop(Plot.WithAlpha(st[4], 0.30), 0));
            fill.GradientStops.Add(new GradientStop(Plot.WithAlpha(st[2], 0.04), 0.5));
            fill.GradientStops.Add(new GradientStop(Plot.WithAlpha(st[0], 0.30), 1));
            fill.Freeze();
            dc.DrawGeometry(fill, null, Plot.Line(area, true));

            Pen outline = new Pen(Theme.Br("#1C1C1E"), 8);
            outline.LineJoin = PenLineJoin.Round; outline.StartLineCap = PenLineCap.Round; outline.EndLineCap = PenLineCap.Round;
            dc.DrawGeometry(null, outline, geo);
            LinearGradientBrush tone = new LinearGradientBrush();
            tone.MappingMode = BrushMappingMode.Absolute;
            tone.StartPoint = new Point(0, Y(p, RangeDb));
            tone.EndPoint = new Point(0, Y(p, -RangeDb));
            for (int k = 0; k < 5; k++) tone.GradientStops.Add(new GradientStop(st[4 - k], k * 0.25));
            tone.Freeze();
            Pen curvePen = new Pen(tone, 3.5);
            curvePen.LineJoin = PenLineJoin.Round; curvePen.StartLineCap = PenLineCap.Round; curvePen.EndLineCap = PenLineCap.Round;
            dc.DrawGeometry(null, curvePen, geo);

            // Dots in the colour of their level, ringed in the card colour; they grow smoothly under the mouse.
            while (radius.Count < Bands.Count) radius.Add(DotR);
            if (radius.Count > Bands.Count) radius.RemoveRange(Bands.Count, radius.Count - Bands.Count);
            double follow = 1 - Math.Exp(-dt / 0.05);
            Brush ring = Theme.Br("#1C1C1E");
            for (int k = 0; k < Bands.Count; k++)
            {
                Band b = Bands[k];
                double want = (k == hover || k == dragging) ? DotHoverR : (k == Selected ? DotR + 1 : DotR);
                radius[k] += (want - radius[k]) * (dt > 0 ? follow : 1);
                if (Math.Abs(want - radius[k]) > 0.05) keepTicking = true; else radius[k] = want;
                Color hc = LevelColors.At((b.HasGain ? b.Gain : -3) / RangeDb, ColorMode);
                dc.PushOpacity(b.Enabled ? 1.0 : 0.35);
                dc.DrawEllipse(Theme.Solid(hc), new Pen(ring, 3), dots[k], radius[k], radius[k]);
                dc.Pop();
            }

            // dB labels last, on a soft backdrop, so the curve never hides them.
            Brush labelBack = Theme.Solid(Plot.WithAlpha(Theme.Col("#1C1C1E"), 0.7));
            foreach (KeyValuePair<FormattedText, Point> gt in gridTexts)
                dc.DrawRoundedRectangle(labelBack, null, new Rect(gt.Value.X - 3, gt.Value.Y, gt.Key.Width + 6, gt.Key.Height), 4, 4);
            foreach (KeyValuePair<FormattedText, Point> gt in gridTexts) dc.DrawText(gt.Key, gt.Value);

            // Values appear only while you point at or drag a dot.
            int info = dragging >= 0 ? dragging : hover;
            if (info >= 0 && info < Bands.Count)
            {
                Band sb = Bands[info];
                Point sh = dots[info];
                string txt = "Band " + (info + 1) + "  ·  " + Plot.Freq(sb.Freq) +
                    (sb.HasGain ? "  ·  " + (sb.Gain > 0 ? "+" : "") + sb.Gain.ToString("0.0", CultureInfo.InvariantCulture) + " dB" : "") +
                    "  ·  Q " + sb.Q.ToString("0.##", CultureInfo.InvariantCulture);
                FormattedText ft = Plot.Text(txt, 11.5, Theme.Br("#FFFFFF"));
                double tw = ft.Width + 22, th = ft.Height + 10;
                double tx = ApoFormat.Clamp(sh.X - tw / 2, p.Left, Math.Max(p.Left, p.Right - tw));
                double ty = sh.Y - 24 - th;
                if (ty < 0) ty = sh.Y + 24;
                dc.DrawRoundedRectangle(Theme.Br("#2C2C2E"), null, new Rect(tx, ty, tw, th), th / 2, th / 2);
                dc.DrawText(ft, new Point(tx + 11, ty + 5));
            }

            if (keepTicking) StartTicking(); else StopTicking();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Rect p = Area();
            Point pt = e.GetPosition(this);
            int hit = HitTestBand(p, pt);
            if (e.ClickCount == 2 && hit < 0)
            {
                if (p.Contains(pt) && AddRequested != null) AddRequested(FAt(p, pt.X), DbAt(p, pt.Y));
                e.Handled = true;
                return;
            }
            if (hit >= 0) { dragging = hit; CaptureMouse(); }
            Selected = hit;
            if (BandSelected != null) BandSelected(hit);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Rect p = Area();
            Point pt = e.GetPosition(this);
            if (dragging >= 0 && dragging < Bands.Count && IsMouseCaptured)
            {
                Band b = Bands[dragging];
                b.Freq = Math.Round(ApoFormat.Clamp(FAt(p, pt.X), 20, 20000));
                if (b.HasGain) b.Gain = Math.Round(ApoFormat.Clamp(DbAt(p, pt.Y) * (IsShelf(b) ? 2 : 1), -24, 24), 1);
                if (BandDragged != null) BandDragged(dragging);
                InvalidateVisual();
            }
            else
            {
                int hv = HitTestBand(p, pt);
                Cursor = hv >= 0 ? Cursors.Hand : Cursors.Arrow;
                if (hv != hover) { hover = hv; InvalidateVisual(); }
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (hover != -1) { hover = -1; InvalidateVisual(); }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            dragging = -1;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Rect p = Area();
            int target = HitTestBand(p, e.GetPosition(this));
            if (target < 0) target = Selected;
            if (target < 0 || target >= Bands.Count) return;
            Band b = Bands[target];
            b.Q = Math.Round(ApoFormat.Clamp(e.Delta > 0 ? b.Q * 1.1 : b.Q / 1.1, 0.1, 20), 2);
            if (BandDragged != null) BandDragged(target);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // A tiny picture of a preset's EQ curve, shown next to its name in the presets list.
    public class PresetSpark : FrameworkElement
    {
        readonly Preset preset;
        public PresetSpark(Preset p)
        {
            preset = p;
            Width = 46;
            Height = 22;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (preset == null) return;
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            const int n = 40;
            const double range = 12;
            double[] db = new double[n + 1];
            foreach (Band b in preset.Bands)
            {
                if (!b.Enabled) continue;
                double[] co = Dsp.Coeffs(b);
                for (int i = 0; i <= n; i++)
                    db[i] += Dsp.MagDb(co, Math.Pow(10, Plot.LMin + (Plot.LMax - Plot.LMin) * i / n));
            }
            Color text = Theme.Text;
            double mid = Math.Round(h / 2) + 0.5;
            dc.DrawLine(new Pen(Theme.Solid(Plot.WithAlpha(text, 0.12)), 1), new Point(1, mid), new Point(w - 1, mid));
            List<Point> pts = new List<Point>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double v = Math.Max(-range, Math.Min(range, db[i]));
                pts.Add(new Point(1.5 + (w - 3) * i / n, h / 2 - v / range * (h / 2 - 2)));
            }
            Color[] st = LevelColors.Stops(0);
            LinearGradientBrush tone = new LinearGradientBrush();
            tone.MappingMode = BrushMappingMode.Absolute;
            tone.StartPoint = new Point(0, 2);
            tone.EndPoint = new Point(0, h - 2);
            for (int k = 0; k < 5; k++) tone.GradientStops.Add(new GradientStop(st[4 - k], k * 0.25));
            tone.Freeze();
            Pen pen = new Pen(tone, 1.6);
            pen.LineJoin = PenLineJoin.Round; pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round;
            dc.DrawGeometry(null, pen, Plot.Line(pts, false));
        }
    }
}
