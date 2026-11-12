// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - one equalizer page (headphones/speakers or microphone): presets, graph, bands, effects and visualizer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TonelineEQ
{
    public static class Ui
    {
        public static TextBlock Txt(string s, double size, string color)
        {
            TextBlock t = new TextBlock();
            t.Text = s; t.FontSize = size; t.Foreground = Theme.Br(color);
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        public static Button Btn(string s, RoutedEventHandler click)
        {
            Button b = new Button();
            b.Content = s;
            if (click != null) b.Click += click;
            return b;
        }

        public static Border Card(UIElement child)
        {
            Border b = new Border();
            b.Background = Theme.Panel();
            b.CornerRadius = new CornerRadius(16);
            b.Padding = new Thickness(16);
            b.Child = child;
            return b;
        }

        // Apple segmented control: a rounded track whose selection pill slides to the chosen segment.
        public static Border SegBox(string trackHex, params RadioButton[] items)
        {
            bool light = Theme.IsLight;
            Grid g = new Grid();
            Border thumb = new Border { Background = light ? Brushes.White : Theme.Br("#636366"), CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Opacity = 0 };
            TranslateTransform tt = new TranslateTransform();
            thumb.RenderTransform = tt;
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (RadioButton rb in items) sp.Children.Add(rb);
            g.Children.Add(thumb);
            g.Children.Add(sp);
            Action<bool> place = delegate (bool animate)
            {
                RadioButton sel = null;
                foreach (RadioButton rb in items) if (rb.IsChecked == true) sel = rb;
                if (sel == null || sel.ActualWidth <= 0 || !sp.IsAncestorOf(sel)) return;
                double x = sel.TranslatePoint(new Point(0, 0), sp).X, w = sel.ActualWidth;
                if (animate && thumb.Opacity > 0)
                {
                    Duration d = new Duration(TimeSpan.FromMilliseconds(420));
                    DoubleAnimation mx = new DoubleAnimation(x, d);
                    mx.EasingFunction = new SpringEase();
                    DoubleAnimation mw = new DoubleAnimation(w, d);
                    mw.EasingFunction = mx.EasingFunction;
                    tt.BeginAnimation(TranslateTransform.XProperty, mx);
                    thumb.BeginAnimation(FrameworkElement.WidthProperty, mw);
                }
                else
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                    thumb.BeginAnimation(FrameworkElement.WidthProperty, null);
                    tt.X = x;
                    thumb.Width = w;
                }
                thumb.Opacity = 1;
            };
            foreach (RadioButton rb in items) rb.Checked += delegate { place(true); };
            sp.SizeChanged += delegate { place(false); };
            return new Border { Child = g, Background = Theme.Br(light ? "#3A3A3C" : trackHex), CornerRadius = new CornerRadius(9), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        }

        // Fades an element in, optionally rising a few pixels, like Apple's view transitions.
        public static void FadeIn(UIElement el, double rise, double ms)
        {
            if (el == null) return;
            if (!Motion.On) { el.BeginAnimation(UIElement.OpacityProperty, null); return; }
            Duration d = new Duration(TimeSpan.FromMilliseconds(ms));
            DoubleAnimation op = new DoubleAnimation(0, 1, d);
            op.FillBehavior = FillBehavior.Stop;
            el.BeginAnimation(UIElement.OpacityProperty, op);
            if (rise != 0)
            {
                TranslateTransform t = new TranslateTransform(0, rise);
                el.RenderTransform = t;
                DoubleAnimation mv = new DoubleAnimation(rise, 0, d);
                mv.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                t.BeginAnimation(TranslateTransform.YProperty, mv);
            }
        }

        public static void FadeTo(UIElement el, double to, double ms)
        {
            if (el == null) return;
            DoubleAnimation a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms)));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        // Small button showing a Windows icon glyph (Segoe Fluent Icons / MDL2).
        public static Button Icon(string glyph, string tip, RoutedEventHandler click)
        {
            Button b = Btn("", click);
            b.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            b.Padding = new Thickness(11, 7, 11, 7);
            b.ToolTip = tip;
            AutomationProperties.SetName(b, ShortName(tip));
            return b;
        }

        // The first words of a tooltip, used as a control's name for screen readers (an icon alone says nothing).
        public static string ShortName(string tip)
        {
            if (string.IsNullOrEmpty(tip)) return "";
            int cut = tip.IndexOfAny(new char[] { ':', '(', '.', '\n' });
            return (cut > 0 ? tip.Substring(0, cut) : tip).Trim();
        }

        // Grey placeholder text inside an empty text box.
        public static Grid WithHint(TextBox tb, string hint)
        {
            Grid g = new Grid();
            TextBlock h = new TextBlock { Text = hint, Foreground = Theme.Br("#8E8E93"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Action upd = delegate { h.Visibility = tb.Text.Length == 0 && !tb.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed; };
            tb.TextChanged += delegate { upd(); };
            tb.GotKeyboardFocus += delegate { upd(); };
            tb.LostKeyboardFocus += delegate { upd(); };
            g.Children.Add(tb);
            g.Children.Add(h);
            upd();
            return g;
        }

        // Filled accent button for the main action in a group.
        public static Button Primary(Button b)
        {
            b.Background = Theme.Br("#D4B470");
            b.Foreground = Theme.Br("#111113");
            b.FontWeight = FontWeights.SemiBold;
            return b;
        }

        // App mark: a dark rounded square with mirrored blue bars, like the app icon.
        public static Border Logo(double size)
        {
            Border logo = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = Theme.Grad("#2C2C2E", "#1C1C1E") };
            StackPanel bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            LinearGradientBrush br = new LinearGradientBrush(Theme.BarStart, Theme.BarEnd, 90);
            br.Freeze();
            double u = size / 34.0;
            foreach (double bh in new double[] { 8, 15, 22, 13, 18, 9 })
                bars.Children.Add(new Border { Width = 2.6 * u, Height = bh * u, CornerRadius = new CornerRadius(1.3 * u), Background = br, Margin = new Thickness(1.1 * u, 0, 1.1 * u, 0), VerticalAlignment = VerticalAlignment.Center });
            logo.Child = bars;
            return logo;
        }

        public static TextBox NumBox(Action<double> commit)
        {
            TextBox tb = new TextBox();
            Style vs = Application.Current.TryFindResource("Value") as Style;
            if (vs != null) tb.Style = vs;
            tb.HorizontalContentAlignment = HorizontalAlignment.Right;
            tb.Padding = new Thickness(4, 3, 5, 3);
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.GotKeyboardFocus += delegate { tb.Tag = tb.Text; };
            Action doCommit = delegate
            {
                if ((tb.Tag as string) == tb.Text) return;
                tb.Tag = tb.Text;
                string s = tb.Text.Trim().Replace(',', '.').ToLowerInvariant();
                double mul = 1;
                if (s.EndsWith("k")) { mul = 1000; s = s.Substring(0, s.Length - 1); }
                double v;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) commit(v * mul);
            };
            tb.KeyDown += delegate (object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { doCommit(); e.Handled = true; } };
            tb.LostKeyboardFocus += delegate { doCommit(); };
            return tb;
        }

        public static Grid RowGrid()
        {
            Grid g = new Grid();
            double[] w = { 30, 40, 104, -1.4, 80, -1, 70, -0.8, 66, 34 };
            foreach (double x in w)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = x > 0 ? new GridLength(x) : new GridLength(-x, GridUnitType.Star) });
            return g;
        }

        // Label shown next to a page's on/off switch.
        public static object PowerContent(bool on, string label)
        {
            return new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br(on ? "#FFFFFF" : "#8E8E93"), VerticalAlignment = VerticalAlignment.Center };
        }

        public static RadioButton Radio(string text, string styleKey, string group)
        {
            RadioButton rb = new RadioButton();
            Style st = Application.Current.TryFindResource(styleKey) as Style;
            if (st != null) rb.Style = st;
            rb.GroupName = group;
            rb.Content = text;
            return rb;
        }

        // Window background: base gradient, optional picture, soft accent glow.
        public static UIElement WrapBackground(UIElement content) { return WrapBackground(content, false); }

        // glass: leave the background clear so Windows 11's Mica material shows through.
        public static UIElement WrapBackground(UIElement content, bool glass)
        {
            Grid g = new Grid();
            g.ClipToBounds = true;
            g.Background = glass ? (Brush)Brushes.Transparent : Theme.Br("#000000");
            if (!string.IsNullOrEmpty(Theme.ImagePath) && File.Exists(Theme.ImagePath))
            {
                try
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 2560;
                    bi.UriSource = new Uri(Theme.ImagePath);
                    bi.EndInit();
                    bi.Freeze();
                    g.Children.Add(new Image { Source = bi, Stretch = Stretch.UniformToFill, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    Color dim = Theme.Bg;
                    dim.A = (byte)Math.Round(255 * Theme.ImageDim);
                    g.Children.Add(new Border { Background = new SolidColorBrush(dim), IsHitTestVisible = false });
                }
                catch (Exception ex) { Store.LogError(ex); }
            }
            if (Theme.Glow > 0)
            {
                Color a = Theme.GlowColor ?? Theme.Accent;
                RadialGradientBrush glow = new RadialGradientBrush();
                glow.Center = new Point(0.1, -0.08);
                glow.GradientOrigin = glow.Center;
                glow.RadiusX = 0.75;
                glow.RadiusY = 0.9;
                glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Min(255, 0x2A * Theme.Glow), a.R, a.G, a.B), 0));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Min(255, 0x0A * Theme.Glow), a.R, a.G, a.B), 0.45));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, a.R, a.G, a.B), 1));
                g.Children.Add(new Border { Background = glow, IsHitTestVisible = false });
                Color t = Theme.Text;
                RadialGradientBrush cool = new RadialGradientBrush();
                cool.Center = new Point(0.95, 1.08);
                cool.GradientOrigin = cool.Center;
                cool.RadiusX = 0.6;
                cool.RadiusY = 0.7;
                cool.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Min(255, 0x10 * Theme.Glow), t.R, t.G, t.B), 0));
                cool.GradientStops.Add(new GradientStop(Color.FromArgb(0, t.R, t.G, t.B), 1));
                g.Children.Add(new Border { Background = cool, IsHitTestVisible = false });
            }
            g.Children.Add(content);
            return g;
        }
    }

    public class BandRow
    {
        public Band Band;
        public Border Root;
        public TextBlock Num;
        public CheckBox On;
        public Button TypeBtn;
        public Slider FS, GS, QS;
        public TextBox FT, GT, QT;
    }

    public class EqPage
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        const int MaxBands = 20;
        const int FftSize = 4096, VizCols = 150;

        public readonly PageKind Kind;
        readonly MainWindow host;
        readonly List<Preset> builtIns;

        public Preset Current;
        public bool Dirty, Bypass, VizOn = true;
        public string DeviceGuid = "", DeviceId = "", DeviceName = "";
        public List<FxItem> Effects = new List<FxItem>();
        public UIElement View;

        ResponseGraph graph;
        SpectrumLayer spectrum;
        StackPanel bandPanel, fxPanel;
        ListBox presetList;
        TextBox saveName;
        Slider preampSlider;
        TextBox preampBox;
        Button autoBtn;
        TextBlock clipText, presetTitle, deviceText;
        ToggleButton power, vizToggle;
        UIElement bandsView, fxView;
        RadioButton tabBands, tabFx, tabSimple;
        bool showSimple;
        UIElement simpleView;
        StackPanel simplePanel;
        Slider[] simpleSliders;
        TextBlock[] simpleValues;
        bool syncingSimple;
        // Now Playing (headphones page)
        Border npCard, npArt;
        TextBlock npTitle, npArtist;
        Button npPlay;
        System.Windows.Threading.DispatcherTimer npTimer;
        string npKey = "";
        bool npBusy, npShown, npFailed;
        List<BandRow> rows = new List<BandRow>();
        int selected = -1;
        bool syncing, listUpdating, showFx;
        Button linkBtn, undoBtn, redoBtn;
        Border apoWarn;
        TextBlock apoWarnText;

        readonly List<string> undo = new List<string>(), redo = new List<string>();
        string stable;
        DateTime lastEditAt = DateTime.MinValue;

        readonly AudioCapture capture;
        bool vizRunning;
        TimeSpan lastFrame;
        string lastVizErr = "";
        readonly float[] vizSamples = new float[FftSize];
        readonly double[] vizRe = new double[FftSize], vizIm = new double[FftSize], vizWin = new double[FftSize];
        readonly double[] vizFreqs = new double[VizCols], vizLevels = new double[VizCols];

        public EqPage(PageKind kind, MainWindow host)
        {
            Kind = kind;
            this.host = host;
            builtIns = BuiltIns.All(kind);
            Current = builtIns[0].Clone();
            capture = new AudioCapture(kind == PageKind.Output);
            for (int i = 0; i < FftSize; i++) vizWin[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
            for (int c = 0; c < VizCols; c++)
            {
                vizFreqs[c] = Math.Pow(10, Math.Log10(20) + (c + 0.5) / VizCols * 3);
                vizLevels[c] = -120;
            }
        }

        public string Title { get { return Kind == PageKind.Mic ? "Microphone" : "Headphones & speakers"; } }
        public string ShortTitle { get { return Kind == PageKind.Mic ? "Mic EQ" : "EQ"; } }
        string Prefix { get { return Kind == PageKind.Mic ? "mic." : "out."; } }
        string CurrentFile { get { return Kind == PageKind.Mic ? "current-mic.txt" : "current.txt"; } }
        string FxFile { get { return Kind == PageKind.Mic ? "fx-mic.txt" : "fx-output.txt"; } }

        // ---------------------------------------------------------------- persistence
        string Get(Dictionary<string, string> s, string key)
        {
            string v;
            if (s.TryGetValue(Prefix + key, out v)) return v;
            if (Kind == PageKind.Output && s.TryGetValue(key, out v)) return v; // settings from older versions
            return null;
        }

        public void LoadState(Dictionary<string, string> s)
        {
            Preset p = null;
            string text = Store.ReadText(CurrentFile);
            if (text != null) p = ApoFormat.Parse(text, "Restored");
            if (p == null) p = builtIns[0].Clone();
            string v = Get(s, "name");
            if (!string.IsNullOrEmpty(v)) p.Name = v;
            v = Get(s, "group");
            if (!string.IsNullOrEmpty(v)) p.Group = v;
            Current = p;
            Dirty = Get(s, "dirty") == "1";
            Bypass = Get(s, "bypass") == "1";
            VizOn = Get(s, "viz") != "0";
            DeviceGuid = Get(s, "device") ?? "";
            DeviceId = Get(s, "deviceId") ?? "";
            DeviceName = Get(s, "deviceName") ?? "";
            string fx = Store.ReadText(FxFile);
            Effects = fx != null ? FxItem.ParseList(fx) : new List<FxItem>();
            stable = Snapshot();
        }

        public void SaveState(Dictionary<string, string> s)
        {
            Store.WriteText(CurrentFile, ApoFormat.ToText(Current, null));
            Store.WriteText(FxFile, FxItem.Serialize(Effects));
            s[Prefix + "name"] = Current.Name;
            s[Prefix + "group"] = Current.Group;
            s[Prefix + "dirty"] = Dirty ? "1" : "0";
            s[Prefix + "bypass"] = Bypass ? "1" : "0";
            s[Prefix + "viz"] = VizOn ? "1" : "0";
            s[Prefix + "device"] = DeviceGuid;
            s[Prefix + "deviceId"] = DeviceId;
            s[Prefix + "deviceName"] = DeviceName;
        }

        // What this page contributes to Equalizer APO's config.
        public string ApoBody()
        {
            if (Bypass)
            {
                // Level-matched: keep the preamp so "EQ off" isn't louder, which would make the EQ sound worse than it is.
                string off = "# " + Title + " EQ is switched off in Toneline EQ\r\n";
                if (host.LevelMatch && Current.Preamp < 0) off += "Preamp: " + ApoFormat.N(Current.Preamp) + " dB\r\n";
                return off;
            }
            StringBuilder sb = new StringBuilder();
            foreach (FxItem f in Effects)
                if (f.Enabled && f.BeforeEq) { string t = f.ToApo(); if (t.Length > 0) sb.Append(t).Append("\r\n"); }
            sb.Append(ApoFormat.ToText(Current, null));
            foreach (FxItem f in Effects)
                if (f.Enabled && !f.BeforeEq) { string t = f.ToApo(); if (t.Length > 0) sb.Append(t).Append("\r\n"); }
            return sb.ToString();
        }

        public List<Preset> AllPresets()
        {
            List<Preset> all = new List<Preset>(builtIns);
            all.AddRange(Store.LoadUserPresets(Kind));
            return all;
        }

        // ---------------------------------------------------------------- view
        public UIElement Build()
        {
            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Presets sidebar: hidden unless opened from the header, slides in and out.
            FrameworkElement sideCard = (FrameworkElement)BuildSidebar();
            sideCard.Width = SideWidth;
            sideCard.HorizontalAlignment = HorizontalAlignment.Left;
            sideHost = new Border { Child = sideCard, ClipToBounds = true, Width = host.SidebarOpen ? SideWidth : 0, Opacity = host.SidebarOpen ? 1 : 0 };
            Grid.SetColumn(sideHost, 0);
            body.Children.Add(sideHost);

            Grid main = new Grid();
            main.Margin = new Thickness(host.SidebarOpen ? SideGap : 0, 0, 0, 0);
            mainArea = main;
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star), MinHeight = 230 });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 170 });
            Grid.SetColumn(main, 1);
            body.Children.Add(main);

            // top bar: preset name, output/device, page power and visualizer
            DockPanel top = new DockPanel { Margin = new Thickness(4, 0, 0, 10) };
            power = new CheckBox();
            power.IsChecked = !Bypass;
            power.Content = Ui.PowerContent(!Bypass, ShortTitle);
            power.ToolTip = Kind == PageKind.Mic ? "Turn the microphone EQ on or off" : "Turn the headphone/speaker EQ on or off. Compare with and without to judge your changes.";
            power.Checked += delegate { SetBypassInternal(false); };
            power.Unchecked += delegate { SetBypassInternal(true); };
            DockPanel.SetDock(power, Dock.Right);
            top.Children.Add(power);
            vizToggle = new CheckBox { Content = new TextBlock { Text = "Visualizer", VerticalAlignment = VerticalAlignment.Center }, Margin = new Thickness(0, 0, 24, 0) };
            vizToggle.IsChecked = VizOn;
            vizToggle.ToolTip = Kind == PageKind.Mic ? "Live spectrum of your microphone. Windows shows the mic-in-use icon while it runs." : "Live spectrum of what Windows is playing";
            vizToggle.Checked += delegate { VizOn = true; host.UpdateVisualizers(); host.SaveAll(); };
            vizToggle.Unchecked += delegate { VizOn = false; host.UpdateVisualizers(); host.SaveAll(); };
            DockPanel.SetDock(vizToggle, Dock.Right);
            top.Children.Add(vizToggle);
            if (Kind == PageKind.Mic)
            {
                micTestBtn = Ui.Btn("", delegate { TestMic(); });
                micTestBtn.FontSize = 12;
                micTestBtn.Padding = new Thickness(12, 5, 12, 5);
                micTestBtn.Margin = new Thickness(0, 0, 10, 0);
                micTestBtn.VerticalAlignment = VerticalAlignment.Center;
                micTestBtn.ToolTip = "Records 5 seconds of your mic with the EQ applied, then plays it back so you hear what others hear.";
                SetMicTestLabel("\uE720", "Test my mic");
                DockPanel.SetDock(micTestBtn, Dock.Right);
                top.Children.Add(micTestBtn);
            }
            linkBtn = Ui.Btn("", delegate { host.LinkPreset(this); });
            linkBtn.FontSize = 12;
            linkBtn.Padding = new Thickness(12, 5, 12, 5);
            linkBtn.Margin = new Thickness(0, 0, 24, 0);
            linkBtn.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(linkBtn, Dock.Right);
            top.Children.Add(linkBtn);
            StackPanel titles = new StackPanel();
            presetTitle = Ui.Txt("", 24, "#FFFFFF");
            presetTitle.FontWeight = FontWeights.Bold;
            presetTitle.Cursor = Cursors.Hand;
            presetTitle.ToolTip = "Show or hide your presets";
            presetTitle.MouseLeftButtonUp += delegate { host.ToggleSidebar(); };
            presetTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            titles.Children.Add(presetTitle);
            deviceText = Ui.Txt("", 12.5, "#8E8E93");
            deviceText.Margin = new Thickness(0, 2, 0, 0);
            deviceText.ToolTip = "Change which device gets this EQ in Settings.";
            titles.Children.Add(deviceText);
            DockPanel warnRow = new DockPanel();
            Button fix = Ui.Btn("Fix in Configurator", delegate { host.OpenConfigurator(); });
            fix.FontSize = 12;
            fix.Padding = new Thickness(10, 3, 10, 3);
            fix.Margin = new Thickness(10, 0, 0, 0);
            fix.ToolTip = "Opens Equalizer APO's Configurator. Tick this device, press OK, then restart Windows (or the audio service).";
            DockPanel.SetDock(fix, Dock.Right);
            warnRow.Children.Add(fix);
            apoWarnText = Ui.Txt("", 12, "#FF6961");
            apoWarnText.TextTrimming = TextTrimming.CharacterEllipsis;
            warnRow.Children.Add(apoWarnText);
            apoWarn = new Border { Child = warnRow, Background = Theme.Br("#312323"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 4, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
            titles.Children.Add(apoWarn);
            top.Children.Add(titles);
            Grid.SetRow(top, 0);
            main.Children.Add(top);

            // graph
            Grid gwrap = new Grid();
            spectrum = new SpectrumLayer();
            gwrap.Children.Add(spectrum);
            graph = new ResponseGraph();
            graph.Kind = Kind;
            graph.BandDragged += delegate (int i) { if (i < rows.Count) RefreshRow(rows[i]); SelectBand(i, false); Edited(); };
            graph.BandSelected += delegate (int i) { SelectBand(i, false); };
            graph.AddRequested += delegate (double f, double g) { AddBand(f, Math.Round(g, 1)); };
            graph.Opacity = Bypass ? 0.4 : 1;
            gwrap.Children.Add(graph);
            Grid gstack = new Grid();
            gstack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gstack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (Kind == PageKind.Output && NowPlaying.Available) gstack.Children.Add(BuildNowPlaying());
            Grid.SetRow(gwrap, 1);
            gstack.Children.Add(gwrap);
            Border gcard = Ui.Card(gstack);
            gcard.Padding = new Thickness(8, 8, 14, 6);
            Grid.SetRow(gcard, 1);
            main.Children.Add(gcard);

            // preamp row
            DockPanel pre = new DockPanel { Margin = new Thickness(4, 12, 4, 12) };
            StackPanel acts = new StackPanel { Orientation = Orientation.Horizontal };
            Button add = Ui.Btn("+ Band", delegate { AddBand(1000, 0); });
            add.ToolTip = "Add a bell filter at 1 kHz. You can also double-click the graph where you want it.";
            Button flat = Ui.Btn("Reset", delegate { LoadPreset(builtIns[0]); SelectInList(builtIns[0].Name); });
            flat.Margin = new Thickness(8, 0, 0, 0);
            flat.ToolTip = "Go back to flat bands (no change to the sound).";
            undoBtn = Ui.Icon("\uE7A7", "Undo the last change (Ctrl+Z)", delegate { Undo(); });
            undoBtn.Margin = new Thickness(0, 0, 6, 0);
            redoBtn = Ui.Icon("\uE7A6", "Redo (Ctrl+Y)", delegate { Redo(); });
            redoBtn.Margin = new Thickness(0, 0, 14, 0);
            acts.Children.Add(undoBtn);
            acts.Children.Add(redoBtn);
            acts.Children.Add(add);
            acts.Children.Add(flat);
            DockPanel.SetDock(acts, Dock.Right);
            pre.Children.Add(acts);
            TextBlock pl = Ui.Txt("Preamp", 13, "#E5E5EA");
            pl.Width = 64;
            pl.ToolTip = Help.Preamp;
            pre.Children.Add(pl);
            preampSlider = new Slider { Minimum = -24, Maximum = 6, Width = 200, SmallChange = 0.1, LargeChange = 1, VerticalAlignment = VerticalAlignment.Center, ToolTip = Help.Preamp };
            preampSlider.MouseDoubleClick += delegate { if (preampSlider.IsEnabled) preampSlider.Value = 0; };
            preampSlider.ValueChanged += delegate
            {
                if (syncing) return;
                Current.Preamp = Math.Round(preampSlider.Value, 1);
                syncing = true; preampBox.Text = ApoFormat.N(Current.Preamp); syncing = false;
                Edited();
            };
            AutomationProperties.SetName(preampSlider, "Preamp");
            pre.Children.Add(preampSlider);
            preampBox = Ui.NumBox(delegate (double v) { Current.Preamp = ApoFormat.Clamp(v, -24, 6); SyncPreamp(); Edited(); });
            preampBox.Width = 58;
            preampBox.Margin = new Thickness(10, 0, 4, 0);
            preampBox.ToolTip = Help.Preamp;
            pre.Children.Add(preampBox);
            pre.Children.Add(Ui.Txt("dB", 12, "#8E8E93"));
            autoBtn = Ui.Btn("Auto", delegate { AutoPreamp(); });
            autoBtn.Margin = new Thickness(12, 0, 0, 0);
            autoBtn.ToolTip = "Set the preamp to exactly the headroom your boosts need, so nothing distorts.";
            pre.Children.Add(autoBtn);
            clipText = Ui.Txt("", 12, "#8E8E93");
            clipText.Margin = new Thickness(14, 0, 0, 0);
            clipText.TextTrimming = TextTrimming.CharacterEllipsis;
            pre.Children.Add(clipText);
            Grid.SetRow(pre, 2);
            main.Children.Add(pre);

            // lower card: bands / effects
            Grid lower = new Grid();
            lower.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            lower.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            string group = "lower" + (int)Kind;
            if (!showFx && BuiltIns.IsSimple(Current)) showSimple = true;
            tabSimple = Ui.Radio("Simple", "Tab", group);
            tabSimple.Padding = new Thickness(18, 4, 18, 4);
            tabSimple.ToolTip = "Three easy controls: Low, Mid and High";
            tabBands = Ui.Radio("Bands", "Tab", group);
            tabBands.Padding = new Thickness(18, 4, 18, 4);
            tabBands.ToolTip = "The EQ filters you see on the graph";
            tabFx = Ui.Radio("Effects & plugins", "Tab", group);
            tabFx.Padding = new Thickness(18, 4, 18, 4);
            tabFx.ToolTip = "Extra processing before or after the EQ: gain, balance, mono, impulse responses, VST plugins and custom Equalizer APO commands";
            tabSimple.IsChecked = showSimple && !showFx;
            tabBands.IsChecked = !showFx && !showSimple;
            tabFx.IsChecked = showFx;
            tabSimple.Checked += delegate { showSimple = true; showFx = false; UpdateLowerTab(); };
            tabBands.Checked += delegate { showSimple = false; showFx = false; UpdateLowerTab(); };
            tabFx.Checked += delegate { showSimple = false; showFx = true; UpdateLowerTab(); };
            Border tabs = Ui.SegBox("#2C2C2E", tabSimple, tabBands, tabFx);
            tabs.Margin = new Thickness(0, 0, 0, 12);
            lower.Children.Add(tabs);

            Grid bandsWrap = new Grid();
            bandsWrap.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bandsWrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid head = Ui.RowGrid();
            head.Margin = new Thickness(8, 0, 18, 6);
            string[] labels = { "#", "On", "Type", "Frequency (Hz)", "", "Gain (dB)", "", "Q (width)", "", "" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].Length == 0) continue;
                TextBlock t = Ui.Txt(labels[i], 11.5, "#8E8E93");
                t.Background = Brushes.Transparent;
                if (i == 1) t.ToolTip = "Turn a single band on or off without deleting it.";
                if (i == 2) t.ToolTip = "Filter type. Click a band's type button to switch between Peak, Low shelf, High shelf, Low pass and High pass.";
                if (i == 3) t.ToolTip = Help.Freq;
                if (i == 5) t.ToolTip = Help.Gain;
                if (i == 7) t.ToolTip = Help.Q;
                Grid.SetColumn(t, i);
                if (i == 3 || i == 5 || i == 7) Grid.SetColumnSpan(t, 2);
                head.Children.Add(t);
            }
            bandsWrap.Children.Add(head);
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            bandPanel = new StackPanel();
            sv.Content = bandPanel;
            Grid.SetRow(sv, 1);
            bandsWrap.Children.Add(sv);
            bandsView = bandsWrap;
            Grid.SetRow(bandsWrap, 1);
            lower.Children.Add(bandsWrap);

            ScrollViewer fsv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            fxPanel = new StackPanel();
            fsv.Content = fxPanel;
            fxView = fsv;
            Grid.SetRow(fsv, 1);
            lower.Children.Add(fsv);

            simplePanel = new StackPanel { Margin = new Thickness(6, 4, 6, 0) };
            ScrollViewer ssv = new ScrollViewer { Content = simplePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            simpleView = ssv;
            Grid.SetRow(ssv, 1);
            lower.Children.Add(ssv);

            Border lcard = Ui.Card(lower);
            Grid.SetRow(lcard, 3);
            main.Children.Add(lcard);

            View = body;
            ApplyOptions();
            RefreshPresetList(Current.Name);
            SyncPreamp();
            RebuildRows();
            RebuildFx();
            UpdateLowerTab();
            UpdateClip();
            UpdateTitle();
            UpdateDeviceText();
            UpdateUndoButtons();
            UpdateLink();
            ShowApoWarning(lastWarn);
            return body;
        }

        UIElement BuildSidebar()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock t = Ui.Txt("PRESETS", 11, "#8E8E93");
            t.Margin = new Thickness(6, 0, 0, 8);
            g.Children.Add(t);

            presetList = new ListBox();
            presetList.SelectionChanged += delegate
            {
                if (listUpdating) return;
                ListBoxItem it = presetList.SelectedItem as ListBoxItem;
                if (it == null) return;
                Preset p = it.Tag as Preset;
                if (p != null) LoadPreset(p);
            };
            Grid.SetRow(presetList, 1);
            g.Children.Add(presetList);

            StackPanel bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (Kind == PageKind.Output)
            {
                Button find = Ui.Btn("Find my headphones…", delegate { host.OpenAutoEq(this); });
                find.Foreground = Theme.Br("#D4B470");
                find.Margin = new Thickness(0, 0, 0, 10);
                find.ToolTip = "Search thousands of measured headphones and IEMs and load a correction in one click (needs internet).";
                bottom.Children.Add(find);
            }
            DockPanel sr = new DockPanel();
            Button more = Ui.Icon("\uE712", "More: import a file, copy or paste an EQ as text, delete a saved preset", null);
            more.Click += delegate { ShowMoreMenu(more); };
            moreBtn = more;
            more.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(more, Dock.Right);
            sr.Children.Add(more);
            Button save = Ui.Btn("Save", delegate { SavePreset(); });
            save.Margin = new Thickness(6, 0, 0, 0);
            save.ToolTip = "Save the current EQ under this name";
            DockPanel.SetDock(save, Dock.Right);
            sr.Children.Add(save);
            saveName = new TextBox();
            saveName.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) SavePreset(); };
            sr.Children.Add(Ui.WithHint(saveName, "Name this EQ"));
            bottom.Children.Add(sr);
            Grid.SetRow(bottom, 2);
            g.Children.Add(bottom);
            return Ui.Card(g);
        }

        // Less-used preset actions, tucked into one menu.
        Button moreBtn;
        Button micTestBtn;
        int micTestState;   // 0 idle, 1 recording, 2 playing
        System.Media.SoundPlayer micPlayer;

        void SetMicTestLabel(string glyph, string text)
        {
            if (micTestBtn == null) return;
            StackPanel c = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock g = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            if (micTestState == 1) g.Foreground = Theme.Solid(Theme.Parse("#FF453A"));
            c.Children.Add(g);
            c.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            micTestBtn.Content = c;
        }

        // "Test my mic": record 5 s from the EQ'd mic, then play it back on the speakers or headphones.
        int micTestRun;   // counts the tests, so one that finishes late can't change the button of a newer one

        async void TestMic()
        {
            if (micTestState == 2) { if (micPlayer != null) micPlayer.Stop(); micTestState = 0; SetMicTestLabel("\uE720", "Test my mic"); return; }
            if (micTestState != 0) return;
            int run = ++micTestRun;
            bool started = false;
            try
            {
                if (!capture.IsRunning) { capture.DeviceId = DeviceId ?? ""; capture.Start(); started = true; await System.Threading.Tasks.Task.Delay(400); }
                micTestState = 1;
                capture.BeginRecording(5);
                for (int sec = 5; sec > 0 && capture.Recording; sec--)
                {
                    SetMicTestLabel("\uE7C8", "Recording… " + sec);
                    await System.Threading.Tasks.Task.Delay(1000);
                }
                float[] samples = capture.TakeRecording();
                if (started && !vizRunning) capture.Stop();
                started = false;
                if (samples.Length < capture.SampleRate / 2) { micTestState = 0; SetMicTestLabel("\uE720", "Test my mic"); host.Say("No sound came from the microphone"); return; }
                // The recording stays in memory and is gone after playback: nothing you say is written to the disk.
                MemoryStream wav = new MemoryStream();
                WriteWav(wav, samples, capture.SampleRate);
                wav.Position = 0;
                micTestState = 2;
                SetMicTestLabel("\uE71A", "Stop playback");
                if (micPlayer != null) micPlayer.Dispose();
                micPlayer = new System.Media.SoundPlayer(wav);
                micPlayer.Play();
                await System.Threading.Tasks.Task.Delay((int)(samples.Length * 1000.0 / capture.SampleRate) + 300);
                if (micTestState == 2 && run == micTestRun) { micTestState = 0; SetMicTestLabel("\uE720", "Test my mic"); }
            }
            catch (Exception ex)
            {
                Store.LogError(ex);
                if (started && !vizRunning) capture.Stop();
                micTestState = 0;
                SetMicTestLabel("\uE720", "Test my mic");
                host.Say("Couldn't test the mic: " + ex.Message);
            }
        }

        // 16-bit mono WAV, gently normalised so quiet mics are still audible (never boosted more than 4x).
        public static void WriteWav(string path, float[] s, int rate)
        {
            using (FileStream fs = File.Create(path)) WriteWav(fs, s, rate);
        }

        public static void WriteWav(Stream output, float[] s, int rate)
        {
            float peak = 0;
            foreach (float v in s) { float a = Math.Abs(v); if (a > peak) peak = a; }
            float gain = peak > 0 ? Math.Min(4f, 0.89f / peak) : 1f;
            BinaryWriter w = new BinaryWriter(output);   // not closed here: the stream belongs to the caller
            int bytes = s.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float v in s) w.Write((short)Math.Max(-32768, Math.Min(32767, Math.Round(v * gain * 32767))));
            w.Flush();
        }

        // Self-test: record one second without playing anything.
        public async System.Threading.Tasks.Task<int> DebugRecord()
        {
            bool started = false;
            if (!capture.IsRunning) { capture.DeviceId = DeviceId ?? ""; capture.Start(); started = true; await System.Threading.Tasks.Task.Delay(400); }
            capture.BeginRecording(1);
            await System.Threading.Tasks.Task.Delay(1300);
            float[] s = capture.TakeRecording();
            if (started && !vizRunning) capture.Stop();
            if (s.Length > 0) WriteWav(Path.Combine(Path.GetTempPath(), "TonelineEQ-selftest.wav"), s, capture.SampleRate);
            return s.Length;
        }
        Border sideHost;
        Grid mainArea;
        const double SideWidth = 276, SideGap = 14;

        public void SetSidebar(bool open)
        {
            if (sideHost == null) return;
            Duration d = new Duration(TimeSpan.FromMilliseconds(260));
            IEasingFunction ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation w = new DoubleAnimation(open ? SideWidth : 0, d);
            w.EasingFunction = ease;
            sideHost.BeginAnimation(FrameworkElement.WidthProperty, w);
            DoubleAnimation o = new DoubleAnimation(open ? 1 : 0, new Duration(TimeSpan.FromMilliseconds(open ? 260 : 140)));
            sideHost.BeginAnimation(UIElement.OpacityProperty, o);
            if (open) Motion.Slide(sideHost.Child, -18, 0, 340, 40);
            ThicknessAnimation m = new ThicknessAnimation(new Thickness(open ? SideGap : 0, 0, 0, 0), d);
            m.EasingFunction = ease;
            mainArea.BeginAnimation(FrameworkElement.MarginProperty, m);
        }
        public ContextMenu DebugMoreMenu() { return moreBtn != null ? ShowMoreMenu(moreBtn) : null; }

        ContextMenu ShowMoreMenu(Button anchor)
        {
            ContextMenu m = new ContextMenu();
            Action<string, Action, string> add = delegate (string text, Action act, string tip)
            {
                MenuItem mi = new MenuItem { Header = text, ToolTip = tip };
                mi.Click += delegate { act(); };
                m.Items.Add(mi);
            };
            add("Import a file…", delegate { ImportFile(); }, "Open a ParametricEQ.txt from AutoEQ or any Equalizer APO config file");
            add("Copy as text", delegate { CopyPreset(); }, "Copy this EQ as Equalizer APO text, to share it or keep a backup");
            add("Paste from clipboard", delegate { PastePreset(); }, "Load an EQ from a share code, or from copied Equalizer APO or AutoEQ text");
            add("Copy share code", delegate { CopyShareCode(); }, "Copy this EQ as one short line. Send it to a friend; they paste it into Toneline EQ with Paste from clipboard.");
            m.Items.Add(new Separator());
            add("Delete saved preset", delegate { DeletePreset(); }, "Delete the selected saved preset");
            if (Kind == PageKind.Mic)
            {
                m.Items.Add(new Separator());
                add("Mic setup help", delegate { host.OpenGuide(PageKind.Mic); }, "How to enable Equalizer APO on your microphone and how to hear the result");
            }
            m.PlacementTarget = anchor;
            m.Placement = PlacementMode.Top;
            m.IsOpen = true;
            return m;
        }

        int lastTab = -1;

        void UpdateLowerTab()
        {
            if (bandsView == null) return;
            bool simple = showSimple && !showFx;
            bandsView.Visibility = !showFx && !simple ? Visibility.Visible : Visibility.Collapsed;
            fxView.Visibility = showFx ? Visibility.Visible : Visibility.Collapsed;
            if (simpleView != null) simpleView.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
            int tab = showFx ? 2 : (simple ? 0 : 1);
            double dx = lastTab < 0 || tab == lastTab ? 0 : (tab > lastTab ? 22 : -22);
            Motion.Slide(showFx ? fxView : (simple ? simpleView : bandsView), dx, dx == 0 ? 4 : 0, 280, 0);
            lastTab = tab;
        }

        // ---------------------------------------------------------------- Simple: Low / Mid / High
        void RebuildSimple()
        {
            if (simplePanel == null) return;
            simplePanel.Children.Clear();
            simpleSliders = null;
            simpleValues = null;
            if (graph != null) graph.SimpleLabels = BuiltIns.IsSimple(Current);
            if (!BuiltIns.IsSimple(Current))
            {
                StackPanel msg = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 26, 0, 0) };
                TextBlock h = Ui.Txt("Three sliders: Low, Mid and High", 16, "#FFFFFF");
                h.FontWeight = FontWeights.SemiBold;
                h.HorizontalAlignment = HorizontalAlignment.Center;
                msg.Children.Add(h);
                TextBlock d = Ui.Txt("The easiest way to tune. Switching replaces the current bands (Ctrl+Z brings them back).", 12.5, "#8E8E93");
                d.Margin = new Thickness(0, 6, 0, 14);
                d.HorizontalAlignment = HorizontalAlignment.Center;
                msg.Children.Add(d);
                Button go = Ui.Primary(Ui.Btn("Start a simple EQ", delegate
                {
                    Preset sp = builtIns.FirstOrDefault(x => x.Name == BuiltIns.SimpleName);
                    if (sp != null) { LoadPreset(sp); SelectInList(sp.Name); }
                }));
                go.HorizontalAlignment = HorizontalAlignment.Center;
                go.Padding = new Thickness(20, 8, 20, 8);
                msg.Children.Add(go);
                simplePanel.Children.Add(msg);
                return;
            }
            string[] names = { "Low", "Mid", "High" };
            string[] descs = Kind == PageKind.Mic
                ? new[] { "Warmth and boom", "Body and presence", "Crispness and air" }
                : new[] { "Bass, kick drums and rumble", "Vocals and instruments", "Detail, cymbals and air" };
            simpleSliders = new Slider[3];
            simpleValues = new TextBlock[3];
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Grid row = new Grid { Margin = new Thickness(4, 8, 4, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                StackPanel lab = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                TextBlock n = Ui.Txt(names[i], 15, "#FFFFFF");
                n.FontWeight = FontWeights.SemiBold;
                lab.Children.Add(n);
                lab.Children.Add(Ui.Txt(descs[i], 12, "#8E8E93"));
                row.Children.Add(lab);
                Slider sl = new Slider { Minimum = -12, Maximum = 12, Value = Current.Bands[i].Gain, SmallChange = 0.5, LargeChange = 1, VerticalAlignment = VerticalAlignment.Center };
                sl.ToolTip = "Drag to boost or cut. Double-click to reset to 0.";
                AutomationProperties.SetName(sl, names[i]);
                Grid.SetColumn(sl, 1);
                row.Children.Add(sl);
                TextBlock v = Ui.Txt("", 15, "#FFFFFF");
                v.FontWeight = FontWeights.SemiBold;
                v.HorizontalAlignment = HorizontalAlignment.Right;
                v.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(v, 2);
                row.Children.Add(v);
                sl.ValueChanged += delegate
                {
                    if (syncingSimple || !BuiltIns.IsSimple(Current)) return;
                    Band b = Current.Bands[idx];
                    b.Gain = Math.Round(sl.Value * 2) / 2;
                    v.Text = FormatDb(b.Gain);
                    PaintSimpleValue(v, b.Gain);
                    // Boosts never clip: the preamp follows automatically.
                    double pk = Dsp.PeakGain(Current.Bands);
                    Current.Preamp = pk > 0 ? -Math.Ceiling(pk * 10) / 10.0 : 0;
                    SyncPreamp();
                    if (idx < rows.Count) RefreshRow(rows[idx]);
                    Edited();
                };
                sl.MouseDoubleClick += delegate { sl.Value = 0; };
                simpleSliders[i] = sl;
                simpleValues[i] = v;
                v.Text = FormatDb(Current.Bands[i].Gain);
                PaintSimpleValue(v, Current.Bands[i].Gain);
                simplePanel.Children.Add(row);
                if (i < 2) simplePanel.Children.Add(new Border { Height = 1, Background = Theme.Br("#38383A"), Opacity = 0.6, Margin = new Thickness(4, 0, 4, 0) });
            }

            // Quick picks: one click sets all three sliders, and the curve glides there.
            TextBlock qh = Ui.Txt("QUICK PICKS", 11, "#8E8E93");
            qh.FontWeight = FontWeights.SemiBold;
            qh.Margin = new Thickness(4, 12, 0, 7);
            simplePanel.Children.Add(qh);
            WrapPanel picks = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            string[] pickNames = Kind == PageKind.Mic
                ? new[] { "Flat", "Warmer", "Clearer", "Less boom", "Brighter", "Radio voice" }
                : new[] { "Flat", "More bass", "Less bass", "Brighter", "Warmer", "Vocals", "V-shape", "Loud and clear" };
            double[][] pickGains = Kind == PageKind.Mic
                ? new[] { new double[] { 0, 0, 0 }, new double[] { 3, 0, -2 }, new double[] { -2, 2, 2 }, new double[] { -5, 0, 1 }, new double[] { 0, 1, 4 }, new double[] { 3, -1, 3 } }
                : new[] { new double[] { 0, 0, 0 }, new double[] { 5, 0, 0 }, new double[] { -4, 0, 0 }, new double[] { 0, 0, 4 }, new double[] { 3, 0, -3 }, new double[] { -1, 3, 1 }, new double[] { 4, -2, 4 }, new double[] { 3, 0, 3 } };
            for (int q = 0; q < pickNames.Length; q++)
            {
                double[] gains = pickGains[q];
                Button pick = Ui.Btn(pickNames[q], delegate { ApplySimplePick(gains); });
                pick.Padding = new Thickness(14, 6, 14, 7);
                pick.Margin = new Thickness(0, 0, 8, 8);
                pick.ToolTip = "Low " + FormatDb(gains[0]) + "   Mid " + FormatDb(gains[1]) + "   High " + FormatDb(gains[2]);
                picks.Children.Add(pick);
            }
            simplePanel.Children.Add(picks);
        }

        void ApplySimplePick(double[] gains)
        {
            if (simpleSliders == null || !BuiltIns.IsSimple(Current)) return;
            if (graph != null) graph.Morph();
            for (int i = 0; i < 3; i++) simpleSliders[i].Value = gains[i];
            for (int i = 0; i < 3; i++) Motion.Pulse(simpleValues[i]);
        }

        // Boosts read warm, cuts read cool, 0 dB stays plain: the same colours as the graph.
        void PaintSimpleValue(TextBlock v, double g)
        {
            v.Foreground = Math.Abs(g) < 0.05 ? Theme.Br("#FFFFFF") : Theme.Solid(LevelColors.At(g / 12.0, host.VizColor));
        }

        static string FormatDb(double g)
        {
            return (g > 0 ? "+" : "") + g.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB";
        }

        // Keeps the three sliders in step when the dots are dragged on the graph.
        void SyncSimple()
        {
            if (simpleSliders == null || !BuiltIns.IsSimple(Current)) return;
            syncingSimple = true;
            for (int i = 0; i < 3; i++)
            {
                simpleSliders[i].Value = Current.Bands[i].Gain;
                simpleValues[i].Text = FormatDb(Current.Bands[i].Gain);
                PaintSimpleValue(simpleValues[i], Current.Bands[i].Gain);
            }
            syncingSimple = false;
        }

        // ---------------------------------------------------------------- Now Playing
        UIElement BuildNowPlaying()
        {
            Grid g = new Grid { Margin = new Thickness(8, 4, 0, 12), Visibility = Visibility.Collapsed };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            npArt = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(10), Background = Theme.Br("#2C2C2E"), Margin = new Thickness(0, 0, 14, 0) };
            g.Children.Add(npArt);
            StackPanel t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            npTitle = Ui.Txt("", 15, "#FFFFFF");
            npTitle.FontWeight = FontWeights.SemiBold;
            npTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            npArtist = Ui.Txt("", 12.5, "#8E8E93");
            npArtist.TextTrimming = TextTrimming.CharacterEllipsis;
            t.Children.Add(npTitle);
            t.Children.Add(npArtist);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Style round = Application.Current.TryFindResource("Round") as Style;
            Func<string, double, int, string, Button> mk = delegate (string glyph, double size, int cmd, string tip)
            {
                Button b = new Button { Width = size, Height = size, ToolTip = tip, Margin = new Thickness(6, 0, 0, 0) };
                if (round != null) b.Style = round;
                b.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size * 0.38 };
                b.Click += delegate { MediaCommand(cmd); };
                AutomationProperties.SetName(b, tip);
                return b;
            };
            Button prev = mk("\uE892", 34, 0, "Previous track");
            prev.Background = Brushes.Transparent;
            npPlay = mk("\uE768", 44, 1, "Play / pause");
            npPlay.Background = Theme.Solid(Theme.Lerp(Theme.Col("#1C1C1E"), Theme.Accent, 0.2));
            npPlay.Foreground = Theme.Br("#D4B470");
            Button next = mk("\uE893", 34, 2, "Next track");
            next.Background = Brushes.Transparent;
            btns.Children.Add(prev);
            btns.Children.Add(npPlay);
            btns.Children.Add(next);
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);
            Border line = new Border { Height = 1, Background = Theme.Br("#38383A"), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -12) };
            Grid.SetColumnSpan(line, 3);
            g.Children.Add(line);
            npCard = new Border { Child = g, Visibility = Visibility.Collapsed };
            npKey = "";   // a rebuilt card (theme change) must load the artwork again
            g.Visibility = Visibility.Visible;
            return npCard;
        }

        async void MediaCommand(int cmd)
        {
            try { await NowPlaying.Command(cmd); } catch (Exception ex) { Store.LogError(ex); }
            await System.Threading.Tasks.Task.Delay(350);
            PollNowPlaying();
        }

        // Polls only while the headphones page is on screen (every 2 s; a few cheap Windows calls).
        public void SetShown(bool shown)
        {
            if (npCard == null || !NowPlaying.Available || npFailed) return;
            if (npTimer == null)
            {
                npTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                npTimer.Tick += delegate { PollNowPlaying(); };
            }
            if (shown == npShown) return;
            npShown = shown;
            if (shown) { npTimer.Start(); PollNowPlaying(); } else npTimer.Stop();
        }

        // Self-test: switch to the Simple tab with the simple preset and move the Low slider.
        public double DebugSimple(double low)
        {
            Preset sp = builtIns.FirstOrDefault(x => x.Name == BuiltIns.SimpleName);
            if (sp == null) return double.NaN;
            LoadPreset(sp);
            SelectInList(sp.Name);
            tabSimple.IsChecked = true;
            if (simpleSliders == null) return double.NaN;
            simpleSliders[0].Value = low;
            return Current.Bands[0].Gain;
        }

        // Self-test: load a preset by name, as if picked in the list.
        public bool DebugLoad(string name)
        {
            Preset p = AllPresets().FirstOrDefault(x => x.Name == name);
            if (p == null) return false;
            LoadPreset(p);
            SelectInList(p.Name);
            return true;
        }

        public bool DebugSimpleLabels { get { return graph != null && graph.SimpleLabels; } }

        public string DebugNowPlaying() { return npCard == null ? "not built" : (npCard.Visibility == Visibility.Visible ? npTitle.Text + " - " + npArtist.Text : "nothing playing"); }

        async void PollNowPlaying()
        {
            if (npBusy || npCard == null) return;
            npBusy = true;
            try
            {
                NowPlayingInfo info = await NowPlaying.Read(npKey);
                if (npCard == null) return;   // the window was closed to the tray while Windows was answering
                if (info == null)
                {
                    npKey = "";
                    Motion.Reveal(npCard, false);
                    return;
                }
                if (info.Key != npKey)
                {
                    npArt.Background = info.Art != null ? ArtBrush(info.Art) : Theme.Br("#2C2C2E");
                    if (npKey.Length > 0 && npCard.IsVisible)
                    {
                        // New song: the artwork pops and the text glides in, like Apple Music.
                        Motion.Pulse(npArt);
                        Motion.Slide(npTitle, 10, 0, 300, 0);
                        Motion.Slide(npArtist, 10, 0, 300, 40);
                    }
                    npKey = info.Key;
                }
                string playGlyph = info.Playing ? "\uE769" : "\uE768";
                if (((TextBlock)npPlay.Content).Text != playGlyph && npCard.IsVisible) Motion.Pulse((UIElement)npPlay.Content);
                npTitle.Text = info.Title;
                npArtist.Text = info.Artist;
                ((TextBlock)npPlay.Content).Text = info.Playing ? "\uE769" : "\uE768";
                Motion.Reveal(npCard, true);
            }
            catch (Exception ex)
            {
                if (npCard == null) return;
                // Media controls unavailable on this PC: log once, hide the card and stop asking.
                npFailed = true;
                if (npTimer != null) npTimer.Stop();
                npCard.Visibility = Visibility.Collapsed;
                Store.LogError(ex);
            }
            finally { npBusy = false; }
        }

        static Brush ArtBrush(byte[] data)
        {
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 112;
                bi.StreamSource = new MemoryStream(data);
                bi.EndInit();
                bi.Freeze();
                ImageBrush ib = new ImageBrush(bi) { Stretch = Stretch.UniformToFill };
                ib.Freeze();
                return ib;
            }
            catch { return Theme.Br("#2C2C2E"); }
        }

        public void ApplyOptions()
        {
            if (graph == null) return;
            graph.RangeDb = host.GraphRange;
            graph.InvalidateVisual();
            spectrum.RangeDb = host.VizRange;
            spectrum.Bars = host.VizFilled;
            spectrum.ColorMode = host.VizColor;
            spectrum.ResetColors();
            graph.ColorMode = host.VizColor;
            spectrum.InvalidateVisual();
            bool manual = !host.AutoPreamp;
            preampSlider.IsEnabled = manual;
            preampBox.IsEnabled = manual;
            autoBtn.IsEnabled = manual;
            if (host.AutoPreamp) { ApplyAutoPreampIfOn(); UpdateClip(); }
        }

        public void UpdateDeviceText()
        {
            if (deviceText == null) return;
            string target = DeviceGuid.Length == 0 ? (Kind == PageKind.Mic ? "All microphones" : "All outputs") : DeviceName;
            deviceText.Text = Title + "  ·  " + target;
        }

        // ---------------------------------------------------------------- presets
        public void RefreshPresetList(string selectName)
        {
            if (presetList == null) return;
            listUpdating = true;
            presetList.Items.Clear();
            string lastGroup = null;
            foreach (Preset p in builtIns)
            {
                if (p.Group != lastGroup) { AddHeader(BuiltIns.GroupTitle(p.Group)); lastGroup = p.Group; }
                AddPresetItem(p);
            }
            AddHeader("Saved");
            List<Preset> user = Store.LoadUserPresets(Kind);
            if (user.Count == 0)
                presetList.Items.Add(new ListBoxItem { Content = "Nothing saved yet", IsEnabled = false, Foreground = Theme.Br("#636366"), FontSize = 12 });
            foreach (Preset p in user) AddPresetItem(p);
            listUpdating = false;
            SelectInList(selectName);
        }

        public void SelectInList(string name)
        {
            if (presetList == null) return;
            listUpdating = true;
            presetList.SelectedItem = null;
            if (name != null)
                foreach (object o in presetList.Items)
                {
                    ListBoxItem it = o as ListBoxItem;
                    Preset p = it != null ? it.Tag as Preset : null;
                    if (p != null && p.Name == name) { presetList.SelectedItem = it; break; }
                }
            listUpdating = false;
        }

        void AddHeader(string s)
        {
            ListBoxItem h = new ListBoxItem { Content = s.ToUpperInvariant(), IsEnabled = false, Foreground = Theme.Br("#8E8E93"), FontSize = 11 };
            h.Padding = new Thickness(10, presetList.Items.Count == 0 ? 2 : 14, 10, 4);
            presetList.Items.Add(h);
        }

        void AddPresetItem(Preset p)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = p.Name, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(p.Note))
                words.Children.Add(new TextBlock { Text = p.Note, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Foreground = Theme.Br("#8E8E93"), Margin = new Thickness(0, 1, 0, 0) });
            g.Children.Add(words);
            PresetSpark spark = new PresetSpark(p) { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(spark, 1);
            g.Children.Add(spark);
            presetList.Items.Add(new ListBoxItem
            {
                Content = g, Tag = p, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = string.IsNullOrEmpty(p.Note) ? p.Name : p.Name + "\n" + p.Note
            });
        }

        public void LoadPreset(Preset p)
        {
            if (stable != null) PushUndo(stable);
            Current = p.Clone();
            Dirty = false;
            selected = -1;
            ApplyAutoPreampIfOn();
            SyncPreamp();
            RebuildRows();
            UpdateClip();
            UpdateTitle();
            if (saveName != null) saveName.Text = p.IsUser ? p.Name : "";
            stable = Snapshot();
            lastEditAt = DateTime.MinValue;
            UpdateLink();
            host.ScheduleApply();
        }

        public bool LoadPresetByName(string name)
        {
            foreach (Preset p in AllPresets())
                if (p.Name == name) { LoadPreset(p); SelectInList(p.Name); return true; }
            return false;
        }

        // Steps through all presets; returns the new preset's name.
        public string NextPreset(int dir)
        {
            List<Preset> all = AllPresets();
            if (all.Count == 0) return "";
            int idx = all.FindIndex(x => x.Name == Current.Name);
            int next = idx < 0 ? 0 : ((idx + dir) % all.Count + all.Count) % all.Count;
            LoadPreset(all[next]);
            SelectInList(all[next].Name);
            return all[next].Name;
        }

        public void ImportPreset(Preset p, string name)
        {
            if (p.Bands.Count > MaxBands) p.Bands = p.Bands.Take(MaxBands).ToList();
            name = ApoFormat.CleanName(name);
            p.Name = name;
            p.Group = "user";
            SelectInList(null);
            LoadPreset(p);
            Dirty = true;
            UpdateTitle();
            if (saveName != null) saveName.Text = name;
            host.Say("Loaded " + p.Bands.Count + " bands. Press Save to keep it.");
        }

        void CopyPreset()
        {
            try { Clipboard.SetText(ApoFormat.ToText(Current, "# " + Current.Name + " (Toneline EQ)\r\n")); host.Say("Copied as Equalizer APO text"); }
            catch (Exception ex) { host.Say("Couldn't copy: " + ex.Message); }
        }

        void CopyShareCode()
        {
            try
            {
                string code = ShareCode.Encode(Current);
                Clipboard.SetText(code);
                host.Say("Share code copied. Paste it into Toneline EQ on any PC");
            }
            catch (Exception ex) { host.Say("Couldn't copy: " + ex.Message); }
        }

        public string DebugShareCode() { return ShareCode.Encode(Current); }

        void PastePreset()
        {
            try
            {
                string text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                Preset shared = ShareCode.Decode(text);
                if (shared != null) { ImportPreset(shared, shared.Name); return; }
                Preset p = ApoFormat.Parse(text, "Pasted");
                if (p == null) { host.Say("The clipboard doesn't contain an EQ or a share code"); return; }
                ImportPreset(p, "Pasted");
            }
            catch (Exception ex) { host.Say("Couldn't paste: " + ex.Message); }
        }

        void SavePreset()
        {
            string name = saveName.Text.Trim();
            if (name.Length == 0) { host.Say("Type a name for the preset first"); saveName.Focus(); return; }
            name = ApoFormat.CleanName(name);
            foreach (Preset b in builtIns) if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) { name += " (mine)"; break; }
            try
            {
                string file = Path.Combine(Store.PresetDir(Kind), Store.SafeName(name) + ".txt");
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (File.Exists(file) && !string.Equals(Current.Name, fileName, StringComparison.OrdinalIgnoreCase) &&
                    !host.Confirm("Replace “" + fileName + "”?", "You already saved a preset with this name. Saving replaces it with the EQ you have now.", "Replace")) return;
                Current.Name = fileName;
                Current.Group = "user";
                File.WriteAllText(file, ApoFormat.ToText(Current, null));
                Dirty = false;
                UpdateTitle();
                RefreshPresetList(Current.Name);
                saveName.Text = Current.Name;
                host.Say("Saved “" + Current.Name + "”");
                UpdateLink();
                host.SaveAll();
            }
            catch (Exception ex) { Store.LogError(ex); host.Say("Couldn't save the preset: " + ex.Message); }
        }

        void DeletePreset()
        {
            ListBoxItem it = presetList.SelectedItem as ListBoxItem;
            Preset p = it != null ? it.Tag as Preset : null;
            if (p == null || !p.IsUser) { host.Say("Select one of your saved presets to delete it"); return; }
            if (!host.Confirm("Delete “" + p.Name + "”?", "This saved preset is removed for good. Built-in presets and your other saved presets stay.", "Delete")) return;
            try
            {
                File.Delete(Path.Combine(Store.PresetDir(Kind), Store.SafeName(p.Name) + ".txt"));
                host.Say("Deleted “" + p.Name + "”");
                if (Current.Name == p.Name) { Dirty = true; UpdateTitle(); }
                RefreshPresetList(null);
            }
            catch (Exception ex) { Store.LogError(ex); host.Say("Couldn't delete: " + ex.Message); }
        }

        void ImportFile()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Import an AutoEQ or Equalizer APO file";
            dlg.Filter = "EQ text files (*.txt)|*.txt|All files (*.*)|*.*";
            if (dlg.ShowDialog(host) != true) return;
            try
            {
                if (new FileInfo(dlg.FileName).Length > 2 * 1024 * 1024) { host.Say("That file is too big to be an EQ preset"); return; }
                string name = Path.GetFileNameWithoutExtension(dlg.FileName).Replace("ParametricEQ", "").Trim();
                if (name.Length == 0) name = "Imported";
                Preset p = ApoFormat.Parse(File.ReadAllText(dlg.FileName), name);
                if (p == null) { host.Say("No EQ filters found in that file"); return; }
                ImportPreset(p, name);
            }
            catch (Exception ex) { Store.LogError(ex); host.Say("Couldn't read that file: " + ex.Message); }
        }

        // ---------------------------------------------------------------- bands
        void RebuildRows()
        {
            if (bandPanel == null || graph == null) return;
            graph.Morph();
            RebuildSimple();
            bandPanel.Children.Clear();
            rows.Clear();
            foreach (Band b in Current.Bands)
            {
                BandRow r = CreateRow(b);
                rows.Add(r);
                bandPanel.Children.Add(r.Root);
            }
            RenumberRows();
            graph.Bands = Current.Bands;
            graph.Selected = selected;
            graph.InvalidateVisual();
            if (rowAnim == -2) { Motion.Cascade(bandPanel); Motion.Cascade(simplePanel); }
            else if (rowAnim >= 0 && rowAnim < rows.Count) { Motion.Slide(rows[rowAnim].Root, 0, -8, 300, 0); Motion.Pulse(rows[rowAnim].Num); }
            rowAnim = -2;
        }

        int rowAnim = -2;   // -2 = cascade all rows, -1 = none, n = only row n is new

        void RenumberRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Num.Text = (i + 1).ToString(IC);
                rows[i].Root.Background = i == selected ? Theme.Br("#252527") : Brushes.Transparent;
            }
        }

        BandRow CreateRow(Band b)
        {
            BandRow r = new BandRow();
            r.Band = b;
            bool wasSyncing = syncing;
            syncing = true; // slider ranges below fire ValueChanged while the row is being built
            Grid g = Ui.RowGrid();
            r.Root = new Border { Child = g, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 1, 6, 1) };
            r.Root.PreviewMouseLeftButtonDown += delegate { SelectBand(rows.IndexOf(r), true); };
            r.Root.MouseEnter += delegate { if (rows.IndexOf(r) != selected) r.Root.Background = Theme.Br("#262628"); };
            r.Root.MouseLeave += delegate { if (rows.IndexOf(r) != selected) r.Root.Background = Brushes.Transparent; };

            r.Num = Ui.Txt("", 11, "#AEAEB2");
            r.Num.HorizontalAlignment = HorizontalAlignment.Center;
            Border badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Theme.Br("#2C2C2E"), Child = r.Num, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(badge, 0);
            g.Children.Add(badge);

            r.On = new CheckBox { VerticalAlignment = VerticalAlignment.Center, ToolTip = "Turn this band on or off without deleting it." };
            Style check = Application.Current.TryFindResource("Check") as Style;
            if (check != null) r.On.Style = check;
            r.On.Click += delegate { r.Band.Enabled = r.On.IsChecked == true; Edited(); };
            Grid.SetColumn(r.On, 1);
            g.Children.Add(r.On);

            r.TypeBtn = Ui.Btn("", delegate
            {
                r.Band.Type = (FilterType)(((int)r.Band.Type + 1) % 5);
                if (!r.Band.HasGain && r.Band.Q > 2) r.Band.Q = 0.71;
                RefreshRow(r);
                Edited();
            });
            r.TypeBtn.Padding = new Thickness(10, 3, 10, 4);
            r.TypeBtn.FontSize = 12.5;
            r.TypeBtn.Background = Theme.Br("#2C2C2E");
            r.TypeBtn.Foreground = Theme.Br("#E5E5EA");
            r.TypeBtn.HorizontalAlignment = HorizontalAlignment.Left;
            r.TypeBtn.Margin = new Thickness(0, 0, 10, 0);
            r.TypeBtn.ToolTip = "Filter type. Click to switch: Peak, Low shelf, High shelf, Low pass, High pass.";
            Grid.SetColumn(r.TypeBtn, 2);
            g.Children.Add(r.TypeBtn);

            r.FS = new Slider { Minimum = Math.Log10(20), Maximum = Math.Log10(20000), VerticalAlignment = VerticalAlignment.Center };
            r.FS.ValueChanged += delegate
            {
                if (syncing) return;
                r.Band.Freq = Math.Round(Math.Pow(10, r.FS.Value));
                syncing = true; r.FT.Text = ApoFormat.N(r.Band.Freq); syncing = false;
                Edited();
            };
            r.FT = Ui.NumBox(delegate (double v) { r.Band.Freq = Math.Round(ApoFormat.Clamp(v, 20, 20000), 1); RefreshRow(r); Edited(); });
            r.FT.Margin = new Thickness(8, 0, 12, 0);
            AutomationProperties.SetName(r.FS, "Frequency");
            AutomationProperties.SetName(r.FT, "Frequency in hertz");
            Grid.SetColumn(r.FS, 3); Grid.SetColumn(r.FT, 4);
            g.Children.Add(r.FS); g.Children.Add(r.FT);

            r.GS = new Slider { Minimum = -15, Maximum = 15, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Gain. Double-click: back to 0 dB." };
            r.GS.MouseDoubleClick += delegate { if (r.Band.HasGain) r.GS.Value = 0; };
            r.GS.ValueChanged += delegate
            {
                if (syncing) return;
                r.Band.Gain = Math.Round(r.GS.Value, 1);
                syncing = true; r.GT.Text = ApoFormat.N(r.Band.Gain); syncing = false;
                PaintBadge(r);
                Edited();
            };
            r.GT = Ui.NumBox(delegate (double v) { r.Band.Gain = ApoFormat.Clamp(v, -24, 24); RefreshRow(r); Edited(); });
            r.GT.Margin = new Thickness(8, 0, 12, 0);
            AutomationProperties.SetName(r.GS, "Gain");
            AutomationProperties.SetName(r.GT, "Gain in decibels");
            Grid.SetColumn(r.GS, 5); Grid.SetColumn(r.GT, 6);
            g.Children.Add(r.GS); g.Children.Add(r.GT);

            r.QS = new Slider { Minimum = -1, Maximum = 1, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Width (Q). Double-click: back to the standard width." };
            r.QS.MouseDoubleClick += delegate { r.QS.Value = Math.Log10(r.Band.HasGain ? 1.41 : 0.71); };
            r.QS.ValueChanged += delegate
            {
                if (syncing) return;
                r.Band.Q = Math.Round(Math.Pow(10, r.QS.Value), 2);
                syncing = true; r.QT.Text = ApoFormat.N(r.Band.Q); syncing = false;
                Edited();
            };
            r.QT = Ui.NumBox(delegate (double v) { r.Band.Q = ApoFormat.Clamp(v, 0.1, 20); RefreshRow(r); Edited(); });
            r.QT.Margin = new Thickness(8, 0, 6, 0);
            AutomationProperties.SetName(r.QS, "Width");
            AutomationProperties.SetName(r.QT, "Q");
            Grid.SetColumn(r.QS, 7); Grid.SetColumn(r.QT, 8);
            g.Children.Add(r.QS); g.Children.Add(r.QT);

            Button del = Ui.Btn("✕", delegate { RemoveBand(rows.IndexOf(r)); });
            del.Padding = new Thickness(0, 3, 0, 3);
            del.Background = Brushes.Transparent;
            del.BorderBrush = Brushes.Transparent;
            del.Foreground = Theme.Br("#8E8E93");
            del.ToolTip = "Remove this band";
            AutomationProperties.SetName(del, "Remove this band");
            AutomationProperties.SetName(r.On, "Band on");
            Grid.SetColumn(del, 9);
            g.Children.Add(del);

            syncing = wasSyncing;
            RefreshRow(r);
            return r;
        }

        // The band number wears the same colour as its dot on the graph.
        void PaintBadge(BandRow r)
        {
            Border badge = r.Num.Parent as Border;
            if (badge == null) return;
            Band b = r.Band;
            Color c = LevelColors.At((b.HasGain ? b.Gain : -3) / 15.0, host.VizColor);
            if (!b.Enabled) c = Plot.WithAlpha(c, 0.35);
            badge.Background = Theme.Solid(c);
            double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            r.Num.Foreground = Theme.Solid(b.Enabled && lum > 150 ? Color.FromRgb(17, 17, 19) : Colors.White);
            r.Num.FontWeight = FontWeights.SemiBold;
        }

        void RefreshRow(BandRow r)
        {
            syncing = true;
            Band b = r.Band;
            r.On.IsChecked = b.Enabled;
            r.TypeBtn.Content = ApoFormat.TypeName(b.Type);
            r.FS.Value = Math.Log10(ApoFormat.Clamp(b.Freq, 20, 20000));
            r.FT.Text = ApoFormat.N(b.Freq);
            r.GS.Value = ApoFormat.Clamp(b.Gain, -15, 15);
            r.GT.Text = ApoFormat.N(b.Gain);
            r.GS.IsEnabled = b.HasGain;
            r.GT.IsEnabled = b.HasGain;
            r.QS.Value = Math.Log10(ApoFormat.Clamp(b.Q, 0.1, 10));
            r.QT.Text = ApoFormat.N(b.Q);
            PaintBadge(r);
            syncing = false;
            r.TypeBtn.ToolTip = Help.TypeText(b.Type) + "\n\nClick to switch to the next type.";
            string ft = Help.Freq + "\n\nThis band: " + ApoFormat.N(b.Freq) + " Hz. " + Help.RegionText(Kind, b.Freq);
            r.FS.ToolTip = ft;
            r.FT.ToolTip = ft;
            string gt = b.HasGain ? Help.Gain : "Low pass and high pass filters only cut, so they have no gain.";
            r.GS.ToolTip = gt;
            r.GT.ToolTip = gt;
            string qt = Help.Q + (b.Type == FilterType.Peak
                ? "\n\nThis band is about " + Help.Octaves(b.Q).ToString("0.#", IC) + " octaves wide."
                : "\n\nOn shelves and pass filters, Q sets how steep the slope is (0.7 is smooth).");
            r.QS.ToolTip = qt;
            r.QT.ToolTip = qt;
        }

        void SelectBand(int i, bool fromRow)
        {
            selected = i;
            if (graph == null) return;
            graph.Selected = i;
            if (fromRow) graph.InvalidateVisual();
            RenumberRows();
        }

        void AddBand(double freq, double gain)
        {
            if (Current.Bands.Count >= MaxBands) { host.Say("Maximum " + MaxBands + " bands"); return; }
            Band b = new Band();
            b.Freq = Math.Round(ApoFormat.Clamp(freq, 20, 20000));
            b.Gain = ApoFormat.Clamp(gain, -24, 24);
            b.Q = 1.0;
            Current.Bands.Add(b);
            selected = Current.Bands.Count - 1;
            rowAnim = selected;
            RebuildRows();
            Edited();
        }

        public void RemoveSelected() { RemoveBand(selected); }

        void RemoveBand(int i)
        {
            if (i < 0 || i >= Current.Bands.Count) return;
            Current.Bands.RemoveAt(i);
            selected = -1;
            rowAnim = -1;
            RebuildRows();
            Edited();
        }

        // ---------------------------------------------------------------- state changes
        void ApplyAutoPreampIfOn()
        {
            if (!host.AutoPreamp || Current == null) return;
            double pk = Dsp.PeakGain(Current.Bands);
            Current.Preamp = pk > 0 ? -Math.Ceiling(pk * 10) / 10.0 : 0;
            SyncPreamp();
        }

        void Edited()
        {
            DateTime now = DateTime.Now;
            if ((now - lastEditAt).TotalMilliseconds > 700 && stable != null) PushUndo(stable);
            lastEditAt = now;
            ApplyAutoPreampIfOn();
            if (graph != null) graph.InvalidateVisual();
            UpdateClip();
            SyncSimple();
            if (!Dirty) { Dirty = true; UpdateTitle(); }
            stable = Snapshot();
            host.ScheduleApply();
        }

        // ---------------------------------------------------------------- undo / redo
        string Snapshot() { return Current.Name + "\n" + Current.Group + "\n" + ApoFormat.ToText(Current, null); }

        void PushUndo(string s)
        {
            if (undo.Count == 0 || undo[undo.Count - 1] != s)
            {
                undo.Add(s);
                if (undo.Count > 60) undo.RemoveAt(0);
            }
            redo.Clear();
            UpdateUndoButtons();
        }

        void Restore(string s)
        {
            string[] parts = s.Split(new char[] { '\n' }, 3);
            Preset p = parts.Length == 3 ? ApoFormat.Parse(parts[2], parts[0]) : null;
            if (p == null) { p = new Preset(); p.Name = parts[0]; }
            p.Group = parts.Length > 1 ? parts[1] : "user";
            Current = p;
            selected = -1;
            SyncPreamp();
            RebuildRows();
            UpdateClip();
            Dirty = true;
            UpdateTitle();
            stable = s;
            lastEditAt = DateTime.MinValue;
            UpdateUndoButtons();
            host.ScheduleApply();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(Snapshot());
            string s = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            Restore(s);
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(Snapshot());
            string s = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            Restore(s);
        }

        void UpdateUndoButtons()
        {
            if (undoBtn == null) return;
            undoBtn.IsEnabled = undo.Count > 0;
            redoBtn.IsEnabled = redo.Count > 0;
        }

        // ---------------------------------------------------------------- per-device presets and health
        public void UpdateLink()
        {
            if (linkBtn == null) return;
            string dev = host.DefaultDeviceName(Kind);
            string linked = host.LinkedPreset(Kind);
            if (dev == null) { Motion.Reveal(linkBtn, false); return; }
            Motion.Reveal(linkBtn, true);
            bool isLinked = linked != null && linked == Current.Name;
            string shortDev = dev.Length > 26 ? dev.Substring(0, 24) + "…" : dev;
            linkBtn.Content = isLinked ? "Linked to this device" : "Link to this device";
            linkBtn.Foreground = isLinked ? Theme.Br("#E8CF94") : Theme.Br("#F2F2F7");
            linkBtn.ToolTip = isLinked
                ? "This preset loads automatically whenever " + dev + " becomes the default device. Click to unlink."
                : "Load this preset automatically whenever " + dev + " becomes the default device" + (linked != null ? " (currently linked: " + linked + ")" : "") + ". Save your edits as a preset first.";
        }

        string lastWarn;

        public void ShowApoWarning(string deviceName)
        {
            lastWarn = deviceName;
            if (apoWarn == null) return;
            Motion.Reveal(apoWarn, deviceName != null);
            if (deviceName != null) apoWarnText.Text = "Equalizer APO isn't installed on " + deviceName + ", so this EQ can't reach it.";
        }

        // ---------------------------------------------------------------- self-test hooks
        public void DebugSelectBand(int i) { SelectBand(i, true); }

        // Self-test: press an arrow key on the graph with the first band selected; returns that band.
        public Band DebugNudge(Key key)
        {
            if (graph == null || Current.Bands.Count == 0) return null;
            SelectBand(0, true);
            graph.Nudge(key, false);
            return Current.Bands[0];
        }

        public string DebugViz()
        {
            double peak = -999;
            foreach (double v in vizLevels) if (v > peak) peak = v;
            return "running=" + vizRunning + " error=" + (capture.LastError ?? "none") + " rate=" + capture.SampleRate + " peakLevel=" + peak.ToString("0.0", IC);
        }

        public void DebugEditFirstBand(double gain)
        {
            if (Current.Bands.Count == 0) return;
            Current.Bands[0].Gain = gain;
            if (rows.Count > 0) RefreshRow(rows[0]);
            Edited();
        }

        public void DebugShowEffects(bool fx)
        {
            showFx = fx;
            if (tabFx != null) { tabFx.IsChecked = fx; tabBands.IsChecked = !fx; }
            RebuildFx();
            UpdateLowerTab();
        }

        public void ReleaseView()
        {
            SetVisualizerActive(false);
            rows.Clear();
            graph = null; spectrum = null; bandPanel = null; fxPanel = null; presetList = null; saveName = null;
            preampSlider = null; preampBox = null; autoBtn = null; clipText = null; presetTitle = null; deviceText = null;
            power = null; vizToggle = null; bandsView = null; fxView = null; tabBands = null; tabFx = null;
            linkBtn = null; undoBtn = null; redoBtn = null; apoWarn = null; apoWarnText = null; micTestBtn = null;
            simpleView = null; simplePanel = null; simpleSliders = null; simpleValues = null; tabSimple = null;
            npCard = null; npArt = null; npTitle = null; npArtist = null; npPlay = null;
            sideHost = null; mainArea = null; moreBtn = null;
            View = null;
        }

        void UpdateTitle()
        {
            if (presetTitle == null) return;
            presetTitle.Inlines.Clear();
            presetTitle.Inlines.Add(new Run(Current.Name));
            if (!Dirty) return;
            // A small "Edited" tag after the name, instead of shouting it in the title.
            Border tag = new Border
            {
                Background = Theme.Br("#2C2C2E"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(10, 0, 0, 3),
                Child = new TextBlock { Text = "Edited", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br("#AEAEB2") }
            };
            presetTitle.Inlines.Add(new InlineUIContainer(tag) { BaselineAlignment = BaselineAlignment.Center });
        }

        void UpdateClip()
        {
            if (clipText == null) return;
            double peak = Dsp.PeakGain(Current.Bands) + Current.Preamp;
            if (peak > 0.05)
            {
                clipText.Text = "May clip (+" + peak.ToString("0.0", IC) + " dB): press Auto";
                clipText.Foreground = Theme.Br("#FF6961");
                clipText.ToolTip = "Your loudest boost is " + peak.ToString("0.0", IC) + " dB above the preamp, so loud parts can distort. Auto lowers the preamp just enough.";
            }
            else
            {
                clipText.Text = "";
                clipText.Foreground = Theme.Br("#8E8E93");
                clipText.ToolTip = "The preamp leaves enough room for your boosts (peak " + peak.ToString("0.0", IC) + " dB).";
            }
        }

        void SyncPreamp()
        {
            if (preampSlider == null) return;
            syncing = true;
            preampSlider.Value = ApoFormat.Clamp(Current.Preamp, -24, 6);
            preampBox.Text = ApoFormat.N(Current.Preamp);
            syncing = false;
        }

        void AutoPreamp()
        {
            double pk = Dsp.PeakGain(Current.Bands);
            Current.Preamp = pk > 0 ? -Math.Ceiling(pk * 10) / 10.0 : 0;
            SyncPreamp();
            Edited();
        }

        public void SetBypass(bool off)
        {
            if (power != null) power.IsChecked = !off;
            else SetBypassInternal(off);
        }

        void SetBypassInternal(bool off)
        {
            Bypass = off;
            if (power != null) power.Content = Ui.PowerContent(!off, ShortTitle);
            if (graph != null) Ui.FadeTo(graph, off ? 0.4 : 1, 220);
            host.ApplyNow();
        }

        // ---------------------------------------------------------------- effects / plugins
        void RebuildFx()
        {
            if (fxPanel == null) return;
            fxPanel.Children.Clear();
            if (Effects.Count == 0)
            {
                TextBlock empty = Ui.Txt("No effects yet. Effects run before or after the EQ: add one below.", 12.5, "#8E8E93");
                empty.Margin = new Thickness(4, 2, 0, 12);
                fxPanel.Children.Add(empty);
            }
            for (int i = 0; i < Effects.Count; i++) fxPanel.Children.Add(FxRow(Effects[i], i));

            TextBlock addLabel = Ui.Txt("ADD", 11, "#8E8E93");
            addLabel.Margin = new Thickness(4, 10, 0, 6);
            fxPanel.Children.Add(addLabel);
            WrapPanel adds = new WrapPanel();
            AddFxButton(adds, "gain", "+ Gain");
            if (Kind == PageKind.Output)
            {
                AddFxButton(adds, "balance", "+ Balance");
                AddFxButton(adds, "mono", "+ Mono");
                AddFxButton(adds, "swap", "+ Swap L/R");
            }
            AddFxButton(adds, "conv", "+ Impulse response");
            AddFxButton(adds, "vst", "+ VST plugin");
            AddFxButton(adds, "custom", "+ Custom lines");
            fxPanel.Children.Add(adds);
        }

        void AddFxButton(WrapPanel panel, string kind, string label)
        {
            FxItem probe = new FxItem { Kind = kind };
            Button b = Ui.Btn(label, delegate { AddFx(kind); });
            b.Margin = new Thickness(0, 0, 8, 8);
            b.ToolTip = probe.Explain;
            panel.Children.Add(b);
        }

        void AddFx(string kind)
        {
            FxItem f = new FxItem { Kind = kind };
            if (kind == "conv" || kind == "vst")
            {
                string path = PickFile(kind);
                if (path == null) return;
                f.Path = path;
            }
            if (kind == "custom") f.Params = "# Equalizer APO commands, one per line\r\n";
            if (Kind == PageKind.Mic && kind == "vst") f.BeforeEq = true; // gates and compressors usually go first on a mic
            Effects.Add(f);
            RebuildFx();
            FxEdited();
        }

        string PickFile(string kind)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (kind == "vst")
            {
                dlg.Title = "Choose a VST2 plugin (.dll)";
                dlg.Filter = "VST plugins (*.dll)|*.dll|All files (*.*)|*.*";
                string vst = Environment.GetEnvironmentVariable("ProgramW6432");
                if (!string.IsNullOrEmpty(vst) && Directory.Exists(Path.Combine(vst, "VSTPlugins"))) dlg.InitialDirectory = Path.Combine(vst, "VSTPlugins");
            }
            else
            {
                dlg.Title = "Choose an impulse response (.wav)";
                dlg.Filter = "Impulse responses (*.wav)|*.wav|All files (*.*)|*.*";
            }
            return dlg.ShowDialog(host) == true ? dlg.FileName : null;
        }

        void FxEdited()
        {
            host.ScheduleApply();
        }

        UIElement FxRow(FxItem f, int index)
        {
            StackPanel content = new StackPanel();
            DockPanel head = new DockPanel();

            Button remove = Ui.Btn("✕", delegate { Effects.Remove(f); RebuildFx(); FxEdited(); });
            remove.Background = Brushes.Transparent;
            remove.BorderBrush = Brushes.Transparent;
            remove.Foreground = Theme.Br("#8E8E93");
            remove.Padding = new Thickness(8, 3, 8, 3);
            remove.ToolTip = "Remove this effect";
            AutomationProperties.SetName(remove, "Remove this effect");
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);

            Button down = Ui.Btn("↓", delegate { if (index < Effects.Count - 1) { Effects.RemoveAt(index); Effects.Insert(index + 1, f); RebuildFx(); FxEdited(); } });
            down.Padding = new Thickness(8, 3, 8, 3);
            down.Margin = new Thickness(4, 0, 0, 0);
            down.ToolTip = "Move later in the chain";
            AutomationProperties.SetName(down, "Move later in the chain");
            down.IsEnabled = index < Effects.Count - 1;
            Button up = Ui.Btn("↑", delegate { if (index > 0) { Effects.RemoveAt(index); Effects.Insert(index - 1, f); RebuildFx(); FxEdited(); } });
            up.Padding = new Thickness(8, 3, 8, 3);
            up.Margin = new Thickness(6, 0, 0, 0);
            up.ToolTip = "Move earlier in the chain";
            AutomationProperties.SetName(up, "Move earlier in the chain");
            up.IsEnabled = index > 0;
            DockPanel.SetDock(down, Dock.Right);
            DockPanel.SetDock(up, Dock.Right);
            head.Children.Add(down);
            head.Children.Add(up);

            Button pos = Ui.Btn(f.BeforeEq ? "Before EQ" : "After EQ", null);
            pos.Padding = new Thickness(10, 3, 10, 3);
            pos.ToolTip = "Where this effect runs: before the EQ (e.g. a noise gate on a mic) or after it (e.g. a limiter). Click to switch.";
            pos.Click += delegate { f.BeforeEq = !f.BeforeEq; pos.Content = f.BeforeEq ? "Before EQ" : "After EQ"; FxEdited(); };
            DockPanel.SetDock(pos, Dock.Right);
            head.Children.Add(pos);

            CheckBox on = new CheckBox { IsChecked = f.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Turn this effect on or off" };
            on.Click += delegate { f.Enabled = on.IsChecked == true; FxEdited(); };
            head.Children.Add(on);
            TextBlock title = Ui.Txt(f.Title, 13.5, "#FFFFFF");
            title.FontWeight = FontWeights.SemiBold;
            title.ToolTip = f.Explain;
            head.Children.Add(title);
            content.Children.Add(head);

            if (f.Kind == "gain" || f.Kind == "balance")
            {
                bool bal = f.Kind == "balance";
                DockPanel line = new DockPanel { Margin = new Thickness(28, 8, 0, 0) };
                TextBlock val = Ui.Txt("", 12.5, "#E5E5EA");
                val.Width = 90;
                val.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(val, Dock.Right);
                line.Children.Add(val);
                Slider sl = new Slider { Minimum = bal ? -100 : -24, Maximum = bal ? 100 : 24, Value = f.Value, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Double-click: back to 0" };
                sl.MouseDoubleClick += delegate { sl.Value = 0; };
                Action showVal = delegate
                {
                    if (bal) val.Text = Math.Abs(f.Value) < 0.5 ? "Center" : (f.Value < 0 ? "Left " : "Right ") + Math.Abs(f.Value).ToString("0", IC) + "%";
                    else val.Text = (f.Value > 0 ? "+" : "") + f.Value.ToString("0.0", IC) + " dB";
                };
                showVal();
                sl.ValueChanged += delegate { f.Value = Math.Round(sl.Value, bal ? 0 : 1); showVal(); FxEdited(); };
                sl.ToolTip = f.Explain;
                line.Children.Add(sl);
                content.Children.Add(line);
            }
            else if (f.Kind == "conv" || f.Kind == "vst")
            {
                DockPanel line = new DockPanel { Margin = new Thickness(28, 8, 0, 0) };
                Button change = Ui.Btn("Change…", delegate { string pth = PickFile(f.Kind); if (pth != null) { f.Path = pth; RebuildFx(); FxEdited(); } });
                change.Padding = new Thickness(10, 3, 10, 3);
                DockPanel.SetDock(change, Dock.Right);
                line.Children.Add(change);
                TextBlock path = Ui.Txt(f.Path.Length > 0 ? f.Path : "No file chosen", 12, f.Path.Length > 0 && File.Exists(f.Path) ? "#AEAEB2" : "#FF6961");
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                path.ToolTip = f.Path.Length > 0 && !File.Exists(f.Path) ? "This file can't be found: " + f.Path : f.Path;
                line.Children.Add(path);
                content.Children.Add(line);
                if (f.Kind == "vst")
                {
                    TextBlock hint = Ui.Txt("Plugin settings (optional): paste the part after the library path from a VSTPlugin line made in Equalizer APO's Editor, e.g. ChunkData \"...\". Leave empty for the plugin's defaults.", 11.5, "#8E8E93");
                    hint.TextWrapping = TextWrapping.Wrap;
                    hint.Margin = new Thickness(28, 8, 0, 4);
                    content.Children.Add(hint);
                    TextBox ps = new TextBox { Text = f.Params, Margin = new Thickness(28, 0, 0, 0), FontFamily = new FontFamily("Cascadia Code, Consolas") };
                    ps.LostKeyboardFocus += delegate { if (ps.Text != f.Params) { f.Params = ps.Text; FxEdited(); } };
                    content.Children.Add(ps);
                }
            }
            else if (f.Kind == "custom")
            {
                TextBox tb = new TextBox
                {
                    Text = f.Params,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    MinHeight = 70,
                    Margin = new Thickness(28, 8, 0, 0),
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    VerticalContentAlignment = VerticalAlignment.Top
                };
                tb.LostKeyboardFocus += delegate { if (tb.Text != f.Params) { f.Params = tb.Text; FxEdited(); } };
                tb.ToolTip = "Applied when this box loses focus. Example: Delay: 10 ms";
                content.Children.Add(tb);
            }
            else
            {
                TextBlock ex = Ui.Txt(f.Explain, 12, "#8E8E93");
                ex.TextWrapping = TextWrapping.Wrap;
                ex.Margin = new Thickness(28, 6, 0, 0);
                content.Children.Add(ex);
            }

            return new Border { Child = content, Background = Theme.Br("#262628"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 8, 12), Margin = new Thickness(0, 0, 6, 8) };
        }

        // ---------------------------------------------------------------- visualizer
        public void SetVisualizerActive(bool want)
        {
            if (want && !vizRunning)
            {
                vizRunning = true;
                lastFrame = TimeSpan.Zero;
                capture.DeviceId = DeviceId ?? "";
                capture.Start();
                CompositionTarget.Rendering += OnFrame;
            }
            else if (!want && vizRunning)
            {
                vizRunning = false;
                CompositionTarget.Rendering -= OnFrame;
                capture.Stop();
                for (int c = 0; c < VizCols; c++) vizLevels[c] = -120;
                if (spectrum != null) { spectrum.Levels = null; spectrum.InvalidateVisual(); }
            }
        }

        public void RestartCapture()
        {
            if (!vizRunning) return;
            SetVisualizerActive(false);
            SetVisualizerActive(true);
        }

        void OnFrame(object sender, EventArgs e)
        {
            RenderingEventArgs ra = e as RenderingEventArgs;
            TimeSpan now = ra != null ? ra.RenderingTime : TimeSpan.FromMilliseconds(Environment.TickCount);
            if (now == lastFrame) return;
            double dtMs = lastFrame == TimeSpan.Zero ? 16.7 : (now - lastFrame).TotalMilliseconds;
            // While you're using another app (a game, a browser), 30 frames a second is plenty: the PC's power goes to that app.
            int cap = host.VizFps;
            if (!host.AppActive && !Program.SelfTest && (cap <= 0 || cap > 30)) cap = 30;
            double minMs = cap <= 0 ? 0 : 1000.0 / cap * 0.85;
            if (dtMs < minMs) return;
            lastFrame = now;
            VizStep(Math.Min(dtMs, 100) / 1000.0);
        }

        // Which FFT bins feed each column and the +3 dB/octave tilt, worked out once per sample rate.
        int[] binLo, binHi;
        double[] vizTilt;
        int binsRate = -1;
        bool vizIdle;

        void PrepareBins(int rate)
        {
            if (rate == binsRate && binLo != null) return;
            binsRate = rate;
            binLo = new int[VizCols];
            binHi = new int[VizCols];
            vizTilt = new double[VizCols];
            double binHz = (double)rate / FftSize;
            int half = FftSize / 2;
            for (int c = 0; c < VizCols; c++)
            {
                double f0 = Math.Pow(10, Math.Log10(20) + (double)c / VizCols * 3);
                double f1 = Math.Pow(10, Math.Log10(20) + (double)(c + 1) / VizCols * 3);
                binLo[c] = Math.Max(1, (int)Math.Floor(f0 / binHz));
                binHi[c] = Math.Min(half, Math.Max(binLo[c] + 1, (int)Math.Ceiling(f1 / binHz)));
                vizTilt[c] = 3 * Math.Log(vizFreqs[c] / 1000, 2);
            }
        }

        // Self-test only: the visualizer shows a made-up, music-like signal instead of the PC's real sound,
        // so the pictures for the GitHub page look the same on every run.
        public static bool DemoSound;
        double demoTime;
        readonly Random demoNoise = new Random(7);
        static readonly double[] DemoTones = { 45, 62, 98, 131, 196, 262, 392, 523, 784, 1175, 1760, 2637, 3951, 5920, 8870, 12500 };

        void FillDemoSound(float[] buf, double dt, int rate)
        {
            demoTime += dt;
            double low = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = demoTime + i / (double)rate;
                double s = 0;
                for (int k = 0; k < DemoTones.Length; k++)
                {
                    double amp = 0.035 / (1 + k * 0.9) * (0.15 + 0.85 * Math.Abs(Math.Sin(t * (1.1 + k * 0.29) + k * 1.7)));
                    s += amp * Math.Sin(2 * Math.PI * DemoTones[k] * t);
                }
                low += (demoNoise.NextDouble() * 2 - 1 - low) * 0.05;
                s += low * 0.05 + (demoNoise.NextDouble() * 2 - 1) * 0.0004;
                buf[i] = (float)s;
            }
        }

        void VizStep(double dt)
        {
            if (spectrum == null) return;
            int rate = capture.SampleRate > 0 ? capture.SampleRate : 48000;
            if (DemoSound) FillDemoSound(vizSamples, dt, rate);
            else capture.CopyLatest(vizSamples);
            double floor = -host.VizRange - 10;
            // Silence: skip the FFT, let the bars fall, then stop redrawing until sound returns.
            float peak = 0;
            for (int i = 0; i < FftSize; i += 4) { float a = Math.Abs(vizSamples[i]); if (a > peak) peak = a; }
            if (peak < 1e-5f)
            {
                double releaseQ = host.VizSmooth == 0 ? 70 : (host.VizSmooth == 1 ? 42 : 24);
                bool settled = true;
                for (int c = 0; c < VizCols; c++)
                {
                    double v = Math.Max(floor, vizLevels[c] - releaseQ * dt);
                    if (v > floor) settled = false;
                    vizLevels[c] = v;
                }
                if (settled && vizIdle) return;
                vizIdle = settled;
                spectrum.Levels = vizLevels;
                spectrum.Freqs = vizFreqs;
                spectrum.InvalidateVisual();
                return;
            }
            vizIdle = false;
            PrepareBins(rate);
            for (int i = 0; i < FftSize; i++) { vizRe[i] = vizSamples[i] * vizWin[i]; vizIm[i] = 0; }
            Fft.Transform(vizRe, vizIm);
            double norm = 20 * Math.Log10(4.0 / FftSize);
            double attack30 = host.VizSmooth == 0 ? 0.85 : (host.VizSmooth == 1 ? 0.6 : 0.4);
            double releasePerSec = host.VizSmooth == 0 ? 70 : (host.VizSmooth == 1 ? 42 : 24);
            double attack = 1 - Math.Pow(1 - attack30, dt / 0.0333);
            for (int c = 0; c < VizCols; c++)
            {
                int b0 = binLo[c], b1 = binHi[c];
                double m = 0;
                for (int b = b0; b < b1; b++)
                {
                    double mm = vizRe[b] * vizRe[b] + vizIm[b] * vizIm[b];
                    if (mm > m) m = mm;
                }
                double db = 10 * Math.Log10(m + 1e-20) + norm + vizTilt[c];
                if (db < floor) db = floor;
                double cur = Math.Max(vizLevels[c], floor);
                vizLevels[c] = db > cur ? cur + (db - cur) * attack : Math.Max(db, cur - releasePerSec * dt);
            }
            spectrum.Levels = vizLevels;
            spectrum.Freqs = vizFreqs;
            spectrum.InvalidateVisual();
            string err = capture.LastError ?? "";
            if (err != lastVizErr && vizToggle != null)
            {
                lastVizErr = err;
                vizToggle.ToolTip = err.Length == 0
                    ? (Kind == PageKind.Mic ? "Live spectrum of your microphone" : "Live spectrum of what Windows is playing")
                    : "The visualizer can't hear this device right now: " + err;
            }
        }
    }
}
