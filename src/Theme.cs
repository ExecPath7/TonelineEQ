// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - theming. The interface is designed in Apple's dark palette (black background, #1C1C1E cards,
// white text, system blue). Every colour is then mapped through the user's chosen accent, background and
// text colours, so one change restyles the whole app.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace TonelineEQ
{
    public class ThemePreset
    {
        public string Name, Bg, Text, Accent;
        public ThemePreset(string name, string bg, string text, string accent) { Name = name; Bg = bg; Text = text; Accent = accent; }
    }

    public static class Theme
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public static readonly Color DefBg = Parse("#000000"), DefText = Parse("#FFFFFF"), DefAccent = Parse("#0A84FF");
        // Apple's SF Pro when it's installed, otherwise Inter (bundled in the fonts folder; drawn to match SF Pro).
        public const string DefaultFont = "SF Pro Text, Inter, Segoe UI Variable Text, Segoe UI";
        // Saved looks from before the Apple redesign are replaced once by the new default.
        const string StyleVersion = "apple6";

        public static Color Bg = DefBg, Text = DefText, Accent = DefAccent;
        public static string FontName = DefaultFont;
        public static double PanelOpacity = 1.0, Glow = 0, Scale = 1.0, ImageDim = 0.55;
        public static string ImagePath = "";
        public static Color? GlowColor;    // null: the glow follows the accent
        public static Color[] VizStops;    // the five live-audio colours, floor to peak (used by the "Custom" colour mode)

        // The live-audio colours Toneline EQ ships with (blue at the floor, orange at the peak).
        public static Color[] DefaultVizStops()
        {
            return new Color[] { Parse("#2F7BFF"), Parse("#3CC8F5"), Parse("#7CEBB4"), Parse("#F5E25C"), Parse("#FF9F43") };
        }
        public static bool Translucent;   // Mica window: cards let a little of the backdrop through

        static readonly Dictionary<string, Brush> cache = new Dictionary<string, Brush>();
        static Typeface face;

        public static readonly ThemePreset[] Presets = {
            new ThemePreset("Blue", "#000000", "#FFFFFF", "#0A84FF"),
            new ThemePreset("Graphite", "#000000", "#FFFFFF", "#98989D"),
            new ThemePreset("Purple", "#000000", "#FFFFFF", "#BF5AF2"),
            new ThemePreset("Pink", "#000000", "#FFFFFF", "#FF375F"),
            new ThemePreset("Orange", "#000000", "#FFFFFF", "#FF9F0A"),
            new ThemePreset("Green", "#000000", "#FFFFFF", "#30D158"),
            new ThemePreset("Gold", "#0E0E10", "#E4E4E7", "#D4B470"),
            new ThemePreset("Light", "#F2F2F7", "#000000", "#007AFF"),
        };

        public static readonly string[] AccentSwatches = { "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#63E6E2", "#64D2FF", "#98989D", "#D4B470" };
        public static readonly string[] BgSwatches = { "#000000", "#1C1C1E", "#0E0E10", "#101317", "#0C100F", "#15130F", "#F2F2F7", "#FFFFFF" };
        public static readonly string[] TextSwatches = { "#FFFFFF", "#F2F2F7", "#E4E4E7", "#EDE6DA", "#000000" };
        public static readonly string[] Fonts = { DefaultFont, "Segoe UI Variable Display, Segoe UI", "Segoe UI", "Bahnschrift", "Calibri", "Trebuchet MS", "Verdana", "Georgia", "Cascadia Code, Consolas" };

        public static string FontLabel(string f) { return f == DefaultFont ? "Inter (default)" : f.Split(',')[0].Trim(); }

        // Turns a font list into a FontFamily, pointing "Inter" at the copy shipped next to TonelineEQ.exe.
        public static FontFamily FamilyFor(string names)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            bool bundled = System.IO.File.Exists(System.IO.Path.Combine(baseDir, "fonts", "Inter-Regular.ttf"));
            List<string> parts = new List<string>();
            foreach (string raw in (names ?? "").Split(','))
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                parts.Add(bundled && string.Equals(n, "Inter", StringComparison.OrdinalIgnoreCase) ? "./fonts/#Inter" : n);
            }
            if (parts.Count == 0) parts.Add("Segoe UI");
            try { return new FontFamily(new Uri(baseDir.EndsWith("\\") ? baseDir : baseDir + "\\"), string.Join(", ", parts)); }
            catch { return new FontFamily(names); }
        }

        static FontFamily family;
        static string familyOf;
        public static FontFamily Family
        {
            get
            {
                if (family == null || familyOf != FontName) { family = FamilyFor(FontName); familyOf = FontName; }
                return family;
            }
        }

        public static Color Parse(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        public static bool TryParse(string s, out Color c)
        {
            c = Colors.Black;
            if (s == null) return false;
            s = s.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            if (!Regex.IsMatch(s, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")) return false;
            try { c = Parse(s); return true; } catch { return false; }
        }

        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        static string Hex8(Color c) { return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        static byte Ch(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(v))); }

        public static Color Lerp(Color a, Color b, double t)
        {
            return Color.FromArgb(255, Ch(a.R + (b.R - a.R) * t), Ch(a.G + (b.G - a.G) * t), Ch(a.B + (b.B - a.B) * t));
        }

        static double Lum(Color c) { return (c.R + c.G + c.B) / 3.0; }
        static double Perceived(Color c) { return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B; }

        public static bool IsLight { get { return Lum(Bg) > 128; } }

        // Text on an accent-filled control: white on dark accents (blue, pink), near-black on bright ones (yellow, gold).
        // Picked by real contrast (WCAG), leaning to white like Apple's buttons; grey, gold, green and yellow accents get dark text.
        public static Color OnAccent { get { return OnColor(Accent); } }

        public static Color OnColor(Color fill)
        {
            Color dark = Parse("#1C1C1E");
            return Contrast(dark, fill) > Contrast(Colors.White, fill) * 1.6 ? dark : Colors.White;
        }

        public static double Contrast(Color a, Color b)
        {
            double la = RelLum(a), lb = RelLum(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        static double RelLum(Color c)
        {
            Func<byte, double> lin = delegate (byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
            return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
        }

        // Apple system colours used by the EQ graph, switches and status dot (dark and light variants).
        public static Color Boost { get { return Parse(IsLight ? "#E8A000" : "#FFD60A"); } }
        public static Color Cut { get { return Parse(IsLight ? "#0A9FD8" : "#64D2FF"); } }
        public static Color BarStart { get { return Parse("#5AC8FA"); } }
        public static Color BarEnd { get { return Parse(IsLight ? "#007AFF" : "#0A84FF"); } }
        public static Color Green { get { return Parse(IsLight ? "#34C759" : "#30D158"); } }

        // Maps a colour from the design palette to the user's theme.
        public static Color Map(Color c)
        {
            string key = c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            Color m;
            switch (key)
            {
                case "D4B470": m = Accent; break;
                case "E8CF94": m = Lerp(Accent, IsLight ? Text : Colors.White, 0.35); break;
                case "B39B66": m = Lerp(Accent, Bg, 0.2); break;
                case "A88C55": m = Lerp(Accent, Bg, 0.25); break;
                case "8F7A4A": m = Lerp(Accent, Bg, 0.4); break;
                case "5C4D2E": m = Lerp(Accent, Bg, 0.65); break;
                case "3A3426": m = Lerp(Accent, Bg, 0.8); break;
                case "111113": m = OnAccent; break;
                // Light mode follows Apple: filled slider tracks and checkmarks use the accent instead of a dark grey.
                case "AEAEB3": m = IsLight ? Accent : Surface(Parse("#AEAEB2")); break;
                case "E5E5EB": m = IsLight ? Accent : Surface(Parse("#E5E5EA")); break;
                case "1C1C1F": m = IsLight ? OnAccent : Surface(Parse("#1C1C1E")); break;
                default:
                    int mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B));
                    m = mx - mn <= 16 ? Surface(c) : c;
                    break;
            }
            m.A = c.A;
            return m;
        }

        // Greys are written for black (background) to white (text) and placed between the theme's two colours.
        // Light themes turn the card grey into white, like Apple's grouped light mode.
        static Color Surface(Color c)
        {
            double l = Lum(c), t = l / 255.0;
            Color m;
            if (IsLight) m = t <= 0.11 ? Lerp(Bg, Colors.White, t / 0.11) : Lerp(Colors.White, Text, (t - 0.11) / 0.89);
            else m = Lerp(Bg, Text, t);
            return Color.FromArgb(255, Ch(m.R + c.R - l), Ch(m.G + c.G - l), Ch(m.B + c.B - l));
        }

        public static Color Col(string hex) { return Map(Parse(hex)); }

        public static Brush Br(string hex)
        {
            Brush b;
            if (cache.TryGetValue(hex, out b)) return b;
            SolidColorBrush sb = new SolidColorBrush(Col(hex));
            sb.Freeze();
            cache[hex] = sb;
            return sb;
        }

        public static Brush Solid(Color c)
        {
            SolidColorBrush sb = new SolidColorBrush(c);
            sb.Freeze();
            return sb;
        }

        // Vertical gradient between two theme colours (cached).
        public static Brush Grad(string top, string bottom)
        {
            string key = top + ">" + bottom;
            Brush b;
            if (cache.TryGetValue(key, out b)) return b;
            LinearGradientBrush g = new LinearGradientBrush(Col(top), Col(bottom), 90);
            g.Freeze();
            cache[key] = g;
            return g;
        }

        // Card background: Apple's secondary grouped background, as see-through as the user chose.
        public static Brush Panel()
        {
            string key = "panel";
            Brush b;
            if (cache.TryGetValue(key, out b)) return b;
            Color c = Col("#1C1C1E");
            c.A = (byte)Math.Round(255 * PanelOpacity * (Translucent ? 0.78 : 1));
            b = Solid(c);
            cache[key] = b;
            return b;
        }

        public static Typeface Face
        {
            get
            {
                if (face == null) face = new Typeface(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                return face;
            }
        }

        public static void Invalidate() { cache.Clear(); face = null; }

        public static SD.Color Win(string hex)
        {
            Color c = Col(hex);
            return SD.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        // ------------------------------------------------ settings
        public static void Load(Dictionary<string, string> s)
        {
            string v;
            Color c;
            double d;
            if (s.TryGetValue("theme.style", out v) && v == StyleVersion)
            {
                if (s.TryGetValue("theme.bg", out v) && TryParse(v, out c)) Bg = c;
                if (s.TryGetValue("theme.text", out v) && TryParse(v, out c)) Text = c;
                if (s.TryGetValue("theme.accent", out v) && TryParse(v, out c)) Accent = c;
                if (s.TryGetValue("theme.font", out v) && v.Length > 0) FontName = v;
                if (s.TryGetValue("theme.panel", out v) && double.TryParse(v, NumberStyles.Float, IC, out d)) PanelOpacity = ApoFormat.Clamp(d, 0.3, 1);
                if (s.TryGetValue("theme.glow", out v) && double.TryParse(v, NumberStyles.Float, IC, out d)) Glow = ApoFormat.Clamp(d, 0, 3);
            }
            GlowColor = s.TryGetValue("theme.glowColor", out v) && TryParse(v, out c) ? (Color?)c : null;
            VizStops = null;
            if (s.TryGetValue("theme.vizStops", out v))
            {
                string[] parts = v.Split(',');
                Color[] stops = new Color[5];
                bool ok = parts.Length == 5;
                for (int i = 0; ok && i < 5; i++) ok = TryParse(parts[i].Trim(), out stops[i]);
                if (ok) VizStops = stops;
            }
            if (s.TryGetValue("theme.scale", out v) && double.TryParse(v, NumberStyles.Float, IC, out d)) Scale = ApoFormat.Clamp(d, 0.75, 1.5);
            if (s.TryGetValue("theme.imageDim", out v) && double.TryParse(v, NumberStyles.Float, IC, out d)) ImageDim = ApoFormat.Clamp(d, 0, 0.95);
            if (s.TryGetValue("theme.image", out v)) ImagePath = v;
            Invalidate();
        }

        public static void Save(Dictionary<string, string> s)
        {
            s["theme.style"] = StyleVersion;
            s["theme.bg"] = Hex(Bg);
            s["theme.text"] = Hex(Text);
            s["theme.accent"] = Hex(Accent);
            s["theme.font"] = FontName;
            s["theme.panel"] = PanelOpacity.ToString(IC);
            s["theme.glow"] = Glow.ToString(IC);
            s["theme.scale"] = Scale.ToString(IC);
            s["theme.imageDim"] = ImageDim.ToString(IC);
            s["theme.image"] = ImagePath ?? "";
            s["theme.glowColor"] = GlowColor.HasValue ? Hex(GlowColor.Value) : "";
            s["theme.vizStops"] = VizStops != null && VizStops.Length == 5 ? string.Join(",", Array.ConvertAll<Color, string>(VizStops, Hex)) : "";
        }

        // True when every look setting is still how Toneline EQ comes.
        public static bool IsDefaultLook
        {
            get
            {
                return Hex(Bg) == Hex(DefBg) && Hex(Text) == Hex(DefText) && Hex(Accent) == Hex(DefAccent) && FontName == DefaultFont &&
                    PanelOpacity == 1.0 && Glow == 0 && Scale == 1.0 && string.IsNullOrEmpty(ImagePath) && !GlowColor.HasValue && VizStops == null;
            }
        }

        // After the background changes: if the text would be hard to read on it, switch the text to black or white.
        public static void FixTextContrast()
        {
            if (Contrast(Text, Bg) >= 4.5) return;
            Text = DefaultTextFor(Bg);
        }

        // The text colour Toneline EQ would use on this background: white, or black on light backgrounds.
        public static Color DefaultTextFor(Color bg)
        {
            if (Contrast(DefText, bg) >= 4.5) return DefText;
            return Contrast(Colors.White, bg) >= Contrast(Colors.Black, bg) ? Colors.White : Colors.Black;
        }

        public static void ResetAll()
        {
            Bg = DefBg; Text = DefText; Accent = DefAccent; FontName = DefaultFont;
            PanelOpacity = 1.0; Glow = 0; Scale = 1.0; ImageDim = 0.55; ImagePath = "";
            GlowColor = null; VizStops = null;
            Invalidate();
        }

        // Implicit control styles, with every colour passed through Map().
        public static ResourceDictionary Resources()
        {
            HookSwitches();
            string x = Expand(XamlTemplate);
            x = Regex.Replace(x, "#[0-9A-Fa-f]{6}\\b", delegate (Match m) { return Hex8(Col(m.Value)); });
            return (ResourceDictionary)XamlReader.Parse(x);
        }

        // Short animation macros used in the template: [[fade NAME TO MS]], [[hover NAME TO]], [[press NAME]].
        static string Fade(string name, string to, string ms)
        {
            return "<BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='" + name + "' Storyboard.TargetProperty='Opacity' To='" + to + "' Duration='0:0:0." + ms + "'/></Storyboard></BeginStoryboard>";
        }

        static string PressScale(string name, string to, string ms)
        {
            string a = "<DoubleAnimation Storyboard.TargetName='" + name + "' Storyboard.TargetProperty='(UIElement.RenderTransform).(ScaleTransform.Scale{0})' To='" + to + "' Duration='0:0:0." + ms + "'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>";
            return "<BeginStoryboard><Storyboard>" + a.Replace("{0}", "X") + a.Replace("{0}", "Y") + "</Storyboard></BeginStoryboard>";
        }

        static string Expand(string x)
        {
            x = Regex.Replace(x, @"\[\[fade (\w+) ([\d.]+) (\d+)\]\]", delegate (Match m) { return Fade(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value); });
            x = Regex.Replace(x, @"\[\[hover (\w+) ([\d.]+)\]\]", delegate (Match m)
            {
                return "<Trigger Property='IsMouseOver' Value='True'><Trigger.EnterActions>" + Fade(m.Groups[1].Value, m.Groups[2].Value, "12") +
                    "</Trigger.EnterActions><Trigger.ExitActions>" + Fade(m.Groups[1].Value, "0", "22") + "</Trigger.ExitActions></Trigger>";
            });
            x = Regex.Replace(x, @"\[\[scale (\w+) ([\d.]+) (\d+)\]\]", delegate (Match m)
            {
                return PressScale(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            });
            x = Regex.Replace(x, @"\[\[press (\w+)\]\]", delegate (Match m)
            {
                return "<Trigger Property='IsPressed' Value='True'><Trigger.EnterActions>" + PressScale(m.Groups[1].Value, "0.96", "08") +
                    "</Trigger.EnterActions><Trigger.ExitActions>" + PressScale(m.Groups[1].Value, "1", "22") + "</Trigger.ExitActions></Trigger>";
            });
            return x;
        }

        // Switches slide their knob and fade in the green track when flipped (not when a page is first drawn).
        static bool switchesHooked;
        static void HookSwitches()
        {
            if (switchesHooked) return;
            switchesHooked = true;
            Motion.Hook();
            EventManager.RegisterClassHandler(typeof(CheckBox), ToggleButton.CheckedEvent, new RoutedEventHandler(SwitchFlipped));
            EventManager.RegisterClassHandler(typeof(CheckBox), ToggleButton.UncheckedEvent, new RoutedEventHandler(SwitchFlipped));
        }

        static void SwitchFlipped(object sender, RoutedEventArgs e)
        {
            CheckBox c = sender as CheckBox;
            if (c == null || e.OriginalSource != c || !c.IsLoaded || c.Template == null) return;
            FrameworkElement knob = c.Template.FindName("Knob", c) as FrameworkElement;
            UIElement on = c.Template.FindName("OnTrack", c) as UIElement;
            if (knob == null || on == null) return;
            bool isOn = c.IsChecked == true;
            TranslateTransform tt = knob.RenderTransform as TranslateTransform;
            if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); knob.RenderTransform = tt; }
            Duration d = new Duration(TimeSpan.FromMilliseconds(190));
            DoubleAnimation slide = new DoubleAnimation(isOn ? -16 : 16, 0, new Duration(TimeSpan.FromMilliseconds(380)));
            slide.EasingFunction = new SpringEase();
            tt.BeginAnimation(TranslateTransform.XProperty, slide);
            DoubleAnimation fade = new DoubleAnimation(isOn ? 0 : 1, isOn ? 1 : 0, d);
            fade.FillBehavior = FillBehavior.Stop;
            on.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // Apple-style controls: grey rounded buttons, segmented controls, iOS switches, slim sliders with a white knob.
        const string XamlTemplate = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Ring'>
    <Setter Property='Control.Template'>
      <Setter.Value>
        <ControlTemplate><Border Margin='-4' CornerRadius='14' BorderBrush='#D4B470' BorderThickness='2'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='RingRound'>
    <Setter Property='Control.Template'>
      <Setter.Value>
        <ControlTemplate><Border Margin='-3' CornerRadius='24' BorderBrush='#D4B470' BorderThickness='2'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='RingIn'>
    <Setter Property='Control.Template'>
      <Setter.Value>
        <ControlTemplate><Border Margin='0,1' CornerRadius='8' BorderBrush='#D4B470' BorderThickness='2'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='14,6'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid x:Name='R' RenderTransformOrigin='0.5,0.5'>
            <Grid.RenderTransform><ScaleTransform ScaleX='1' ScaleY='1'/></Grid.RenderTransform>
            <Border x:Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'/>
            <Border x:Name='O' Background='#FFFFFF' CornerRadius='9' Opacity='0'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            [[hover O 0.08]]
            [[press R]]
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#D4B470'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Round' TargetType='Button'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingRound}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid x:Name='R' RenderTransformOrigin='0.5,0.5'>
            <Grid.RenderTransform><ScaleTransform ScaleX='1' ScaleY='1'/></Grid.RenderTransform>
            <Ellipse Fill='{TemplateBinding Background}'/>
            <Ellipse x:Name='O' Fill='#FFFFFF' Opacity='0'/>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            [[hover O 0.08]]
            [[press R]]
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToggleButton'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='Padding' Value='16,7'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingRound}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Grid x:Name='R' RenderTransformOrigin='0.5,0.5'>
            <Grid.RenderTransform><ScaleTransform ScaleX='1' ScaleY='1'/></Grid.RenderTransform>
            <Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='17'/>
            <Border x:Name='C' Background='#D4B470' CornerRadius='17' Opacity='0'/>
            <Border x:Name='O' Background='#FFFFFF' CornerRadius='17' Opacity='0'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            [[hover O 0.08]]
            [[press R]]
            <Trigger Property='IsChecked' Value='True'>
              <Trigger.EnterActions>[[fade C 1 18]]</Trigger.EnterActions>
              <Trigger.ExitActions>[[fade C 0 18]]</Trigger.ExitActions>
              <Setter TargetName='C' Property='Opacity' Value='1'/>
              <Setter Property='Foreground' Value='#111113'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Tab' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Padding' Value='16,5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingIn}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid>
            <Border x:Name='H' Background='#3A3A3C' CornerRadius='7' Opacity='0'/>
            <Border Background='Transparent' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <MultiTrigger>
              <MultiTrigger.Conditions>
                <Condition Property='IsMouseOver' Value='True'/>
                <Condition Property='IsChecked' Value='False'/>
              </MultiTrigger.Conditions>
              <MultiTrigger.EnterActions>[[fade H 0.7 12]]</MultiTrigger.EnterActions>
              <MultiTrigger.ExitActions>[[fade H 0 18]]</MultiTrigger.ExitActions>
            </MultiTrigger>
            <Trigger Property='IsChecked' Value='True'><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Pill' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='Padding' Value='12,5'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingRound}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid x:Name='R' RenderTransformOrigin='0.5,0.5'>
            <Grid.RenderTransform><ScaleTransform ScaleX='1' ScaleY='1'/></Grid.RenderTransform>
            <Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='15'/>
            <Border x:Name='C' Background='#D4B470' CornerRadius='15' Opacity='0'/>
            <Border x:Name='O' Background='#FFFFFF' CornerRadius='15' Opacity='0'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            [[hover O 0.08]]
            [[press R]]
            <Trigger Property='IsChecked' Value='True'>
              <Trigger.EnterActions>[[fade C 1 18]]</Trigger.EnterActions>
              <Trigger.ExitActions>[[fade C 0 18]]</Trigger.ExitActions>
              <Setter TargetName='C' Property='Opacity' Value='1'/>
              <Setter Property='Foreground' Value='#111113'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource Ring}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <ContentPresenter x:Name='CP' VerticalAlignment='Center' Margin='0,0,10,0'/>
            <Grid Width='40' Height='24' VerticalAlignment='Center'>
              <Border x:Name='Track' CornerRadius='12' Background='#39393D'/>
              <Border x:Name='OnTrack' CornerRadius='12' Background='#30D158' Opacity='0'/>
              <Grid x:Name='Knob' Width='20' Height='20' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='2,0,2,0'>
                <Ellipse Fill='#38000000' Margin='0,1.5,0,-1.5'/>
                <Ellipse Fill='#FFFFFFFF'/>
              </Grid>
            </Grid>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='Content' Value='{x:Null}'><Setter TargetName='CP' Property='Visibility' Value='Collapsed'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='OnTrack' Property='Opacity' Value='1'/>
              <Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Check' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingRound}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Width='22' Height='22' Background='Transparent'>
            <Ellipse x:Name='Ring' Stroke='#636366' StrokeThickness='1.5'/>
            <Ellipse x:Name='Fill' Fill='#E5E5EB' Opacity='0'/>
            <Path x:Name='Mark' Data='M 6.5 11.5 L 9.6 14.5 L 15.5 8' Stroke='#1C1C1F' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Opacity='0'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Trigger.EnterActions>[[fade Fill 1 15]][[fade Mark 1 15]]</Trigger.EnterActions>
              <Trigger.ExitActions>[[fade Fill 0 15]][[fade Mark 0 15]]</Trigger.ExitActions>
              <Setter TargetName='Ring' Property='Opacity' Value='0'/>
              <Setter TargetName='Fill' Property='Opacity' Value='1'/>
              <Setter TargetName='Mark' Property='Opacity' Value='1'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Ring' Property='Stroke' Value='#AEAEB2'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='CaretBrush' Value='#FFFFFF'/>
    <Setter Property='SelectionBrush' Value='#D4B470'/>
    <Setter Property='Padding' Value='7,4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Grid>
            <Border x:Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1.5' CornerRadius='8'>
              <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/>
            </Border>
            <Border x:Name='F' BorderBrush='#D4B470' BorderThickness='1.5' CornerRadius='8' Opacity='0' IsHitTestVisible='False'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Trigger.EnterActions>[[fade F 1 16]]</Trigger.EnterActions>
              <Trigger.ExitActions>[[fade F 0 20]]</Trigger.ExitActions>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Value' TargetType='TextBox' BasedOn='{StaticResource {x:Type TextBox}}'>
    <Setter Property='Background' Value='Transparent'/>
    <Style.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#2C2C2E'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Background' Value='#2C2C2E'/></Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ContextMenu'>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{TemplateBinding Background}' BorderBrush='#3A3A3C' BorderThickness='1' CornerRadius='10' Padding='5' MinWidth='190'>
            <StackPanel IsItemsHost='True'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Grid>
            <Border x:Name='B' Background='#D4B470' CornerRadius='6' Opacity='0'/>
            <Border Background='Transparent' Padding='12,7,24,7'>
              <ContentPresenter ContentSource='Header' VerticalAlignment='Center'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Trigger.EnterActions>[[fade B 1 08]]</Trigger.EnterActions>
              <Trigger.ExitActions>[[fade B 0 16]]</Trigger.ExitActions>
              <Setter Property='Foreground' Value='#111113'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'><Border Height='1' Background='#3A3A3C' Margin='8,4'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PlainRepeat' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='FillRepeat' TargetType='RepeatButton' BasedOn='{StaticResource PlainRepeat}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent'><Border Height='4' CornerRadius='2' Background='#AEAEB3' VerticalAlignment='Center'/></Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Slider'>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingRound}'/>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Slider'>
          <Grid Height='24' Background='Transparent'>
            <Border Height='4' CornerRadius='2' Background='#3A3A3C' VerticalAlignment='Center' Margin='2,0'/>
            <Track x:Name='PART_Track'>
              <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource FillRepeat}' Command='Slider.DecreaseLarge'/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource PlainRepeat}' Command='Slider.IncreaseLarge'/></Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Grid x:Name='K' Width='18' Height='18' Background='Transparent' RenderTransformOrigin='0.5,0.5'>
                        <Grid.RenderTransform><ScaleTransform ScaleX='1' ScaleY='1'/></Grid.RenderTransform>
                        <Ellipse Fill='#40000000' Margin='0,1.5,0,-1.5'/>
                        <Ellipse x:Name='E' Fill='#FFFFFFFF' Stroke='#22000000' StrokeThickness='0.75'/>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Trigger.EnterActions>[[scale K 1.15 18]]</Trigger.EnterActions>
                          <Trigger.ExitActions>[[scale K 1 26]]</Trigger.ExitActions>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.3'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key='VScroll' TargetType='ScrollBar'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'>
      <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource PlainRepeat}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource PlainRepeat}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
      <Track.Thumb>
        <Thumb>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='#48484A' Margin='2,0'/></ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </ControlTemplate>
  <ControlTemplate x:Key='HScroll' TargetType='ScrollBar'>
    <Track x:Name='PART_Track' IsDirectionReversed='False'>
      <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource PlainRepeat}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource PlainRepeat}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
      <Track.Thumb>
        <Thumb>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='#48484A' Margin='0,2'/></ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
    </Track>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='9'/>
    <Setter Property='MinWidth' Value='9'/>
    <Setter Property='Template' Value='{StaticResource VScroll}'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='9'/>
        <Setter Property='MinHeight' Value='9'/>
        <Setter Property='Template' Value='{StaticResource HScroll}'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
    <Setter Property='VirtualizingPanel.ScrollUnit' Value='Pixel'/>
  </Style>
  <Style TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='#E5E5EA'/>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource RingIn}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Grid Margin='0,1' Background='Transparent'>
            <Border x:Name='H' Background='#2C2C2E' CornerRadius='8' Opacity='0'/>
            <Border x:Name='S' Background='#3A3A3C' CornerRadius='8' Opacity='0'/>
            <Border Padding='{TemplateBinding Padding}'>
              <ContentPresenter/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            [[hover H 1]]
            <Trigger Property='IsSelected' Value='True'>
              <Trigger.EnterActions>[[fade S 1 18]]</Trigger.EnterActions>
              <Trigger.ExitActions>[[fade S 0 22]]</Trigger.ExitActions>
              <Setter TargetName='S' Property='Opacity' Value='1'/>
              <Setter Property='Foreground' Value='#FFFFFF'/>
              <Setter Property='FontWeight' Value='SemiBold'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#FFFFFF'/>
    <Setter Property='Background' Value='#2C2C2E'/>
    <Setter Property='BorderBrush' Value='#38383A'/>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property='ContentTemplate'>
      <Setter.Value>
        <DataTemplate><TextBlock Text='{Binding}' TextWrapping='Wrap' MaxWidth='340'/></DataTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }

    // Tray menu in the app's colours
    public class ThemedMenuColors : WF.ProfessionalColorTable
    {
        public override SD.Color ToolStripDropDownBackground { get { return Theme.Win("#1C1C1E"); } }
        public override SD.Color MenuBorder { get { return Theme.Win("#38383A"); } }
        public override SD.Color MenuItemBorder { get { return Theme.Win("#3A3A3C"); } }
        public override SD.Color MenuItemSelected { get { return Theme.Win("#3A3A3C"); } }
        public override SD.Color MenuItemSelectedGradientBegin { get { return Theme.Win("#3A3A3C"); } }
        public override SD.Color MenuItemSelectedGradientEnd { get { return Theme.Win("#3A3A3C"); } }
        public override SD.Color MenuItemPressedGradientBegin { get { return Theme.Win("#3A3A3C"); } }
        public override SD.Color MenuItemPressedGradientEnd { get { return Theme.Win("#3A3A3C"); } }
        public override SD.Color ImageMarginGradientBegin { get { return Theme.Win("#1C1C1E"); } }
        public override SD.Color ImageMarginGradientMiddle { get { return Theme.Win("#1C1C1E"); } }
        public override SD.Color ImageMarginGradientEnd { get { return Theme.Win("#1C1C1E"); } }
        public override SD.Color SeparatorDark { get { return Theme.Win("#38383A"); } }
        public override SD.Color SeparatorLight { get { return Theme.Win("#1C1C1E"); } }
        public override SD.Color CheckBackground { get { return Theme.Win("#D4B470"); } }
        public override SD.Color CheckSelectedBackground { get { return Theme.Win("#E8CF94"); } }
        public override SD.Color CheckPressedBackground { get { return Theme.Win("#E8CF94"); } }
    }
}
