// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - Settings (including appearance) and Guide windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TonelineEQ
{
    public partial class MainWindow
    {
        Window settingsWin, guideWin;
        ScrollViewer settingsScroll;
        PageKind guideKind = PageKind.Output;

        Window MakeChildWindow(string title, double w, double h)
        {
            Window win = new Window();
            win.Title = title;
            win.Width = w;
            win.Height = h;
            win.MinWidth = 480;
            win.MinHeight = 380;
            win.Owner = this;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            win.ShowInTaskbar = false;
            win.FontSize = 13;
            win.UseLayoutRounding = true;
            StyleChild(win);
            DarkTitle(win);
            // Mica for child windows too; if Windows refuses, rebuild them solid.
            if (glassOn) win.Tag = "glass";
            win.SourceInitialized += delegate
            {
                if (!"glass".Equals(win.Tag) || Glass.Enable(win, true)) return;
                win.Tag = null;
                StyleChild(win);
                if (win == settingsWin) RebuildSettings();
                else if (win == guideWin) RebuildGuide();
            };
            return win;
        }

        bool ChildGlass(Window win) { return glassOn && "glass".Equals(win.Tag); }

        void StyleChild(Window win)
        {
            win.Background = ChildGlass(win) ? (Brush)Brushes.Transparent : Theme.Br("#000000");
            win.Foreground = Theme.Br("#E5E5EA");
            win.FontFamily = Theme.Family;
        }

        static TextBlock SectionTitle(string text)
        {
            TextBlock t = Ui.Txt(text.ToUpperInvariant(), 11.5, "#8E8E93");
            t.Margin = new Thickness(16, 24, 0, 7);
            t.Tag = "section:" + text;
            return t;
        }

        // Settings categories, each with a symbol in a coloured rounded square, like macOS System Settings.
        static readonly Dictionary<string, string[]> SettingsIcons = new Dictionary<string, string[]>
        {
            { "General", new[] { "\uE713", "#8E8E93" } },
            { "Equalizer", new[] { "\uE9E9", "#0A84FF" } },
            { "Devices", new[] { "\uE7F6", "#FF9F0A" } },
            { "Visualizer", new[] { "\uE9D2", "#BF5AF2" } },
            { "Appearance", new[] { "\uE790", "#FF375F" } },
            { "Files and Windows", new[] { "\uE8B7", "#30B0C7" } },
        };

        static Border CategoryIcon(string name, double size)
        {
            string[] ic;
            if (!SettingsIcons.TryGetValue(name, out ic)) ic = new[] { "\uE713", "#8E8E93" };
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = Theme.Solid(Theme.Parse(ic[1])),
                Child = new TextBlock { Text = ic[0], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size * 0.56, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
        }

        string settingsPage = "General";
        readonly List<KeyValuePair<string, StackPanel>> settingsPages = new List<KeyValuePair<string, StackPanel>>();
        ListBox settingsNav;
        Border settingsBody;

        public List<string> SettingsPageNames() { return settingsPages.Select(p => p.Key).ToList(); }

        public void ShowSettingsPage(string name)
        {
            if (settingsNav == null) return;
            for (int i = 0; i < settingsPages.Count; i++)
                if (settingsPages[i].Key == name) { settingsNav.SelectedIndex = i; return; }
        }

        // Splits the long settings list at its section titles into one page per category.
        void LayoutSettingsPages(StackPanel all)
        {
            settingsPages.Clear();
            StackPanel cur = null;
            List<UIElement> items = new List<UIElement>();
            foreach (UIElement e in all.Children) items.Add(e);
            all.Children.Clear();
            foreach (UIElement e in items)
            {
                FrameworkElement fe = e as FrameworkElement;
                string tag = fe != null ? fe.Tag as string : null;
                if (tag != null && tag.StartsWith("section:"))
                {
                    string name = tag.Substring(8);
                    cur = new StackPanel { Margin = new Thickness(28, 0, 28, 28) };
                    StackPanel title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 6) };
                    title.Children.Add(CategoryIcon(name, 30));
                    TextBlock big = Ui.Txt(name, 22, "#FFFFFF");
                    big.FontWeight = FontWeights.Bold;
                    big.Margin = new Thickness(12, 0, 0, 0);
                    big.VerticalAlignment = VerticalAlignment.Center;
                    title.Children.Add(big);
                    cur.Children.Add(title);
                    settingsPages.Add(new KeyValuePair<string, StackPanel>(name, cur));
                    continue;
                }
                if (cur == null) continue;
                if (fe != null && fe.Margin.Top == 0 && cur.Children.Count == 1) fe.Margin = new Thickness(fe.Margin.Left, 14, fe.Margin.Right, fe.Margin.Bottom);
                cur.Children.Add(e);
            }
        }

        // Apple grouped list: consecutive rows share one rounded card, split by inset hairlines.
        static void GroupRows(StackPanel sp)
        {
            List<UIElement> items = new List<UIElement>();
            foreach (UIElement e in sp.Children) items.Add(e);
            sp.Children.Clear();
            StackPanel group = null;
            foreach (UIElement e in items)
            {
                FrameworkElement fe = e as FrameworkElement;
                if (fe == null || !"row".Equals(fe.Tag)) { group = null; sp.Children.Add(e); continue; }
                if (group == null)
                {
                    group = new StackPanel();
                    sp.Children.Add(new Border { Child = group, Background = Theme.Panel(), CornerRadius = new CornerRadius(12), ClipToBounds = true });
                }
                else group.Children.Add(new Border { Height = 1, Background = Theme.Br("#38383A"), Margin = new Thickness(16, 0, 0, 0), Opacity = 0.7 });
                group.Children.Add(e);
            }
        }

        static Border OptionRow(string title, string desc, UIElement control)
        {
            DockPanel row = new DockPanel();
            if (control != null)
            {
                FrameworkElement fe = control as FrameworkElement;
                if (fe != null) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(16, 0, 0, 0); }
                DockPanel.SetDock(control, Dock.Right);
                row.Children.Add(control);
            }
            StackPanel text = new StackPanel();
            text.Children.Add(Ui.Txt(title, 13.5, "#FFFFFF"));
            if (!string.IsNullOrEmpty(desc))
            {
                TextBlock d = Ui.Txt(desc, 12, "#8E8E93");
                d.TextWrapping = TextWrapping.Wrap;
                d.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(d);
            }
            row.Children.Add(text);
            return new Border { Child = row, Padding = new Thickness(16, 11, 16, 11), Tag = "row" };
        }

        static CheckBox Toggle(bool value, Action<bool> changed)
        {
            CheckBox c = new CheckBox();
            c.IsChecked = value;
            c.Click += delegate { changed(c.IsChecked == true); };
            return c;
        }

        static UIElement Segmented(string[] labels, int selectedIndex, Action<int> picked)
        {
            string group = "g" + Guid.NewGuid().ToString("N");
            RadioButton[] items = new RadioButton[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                RadioButton rb = Ui.Radio(labels[i], "Tab", group);
                rb.Padding = new Thickness(12, 3, 12, 3);
                rb.FontSize = 12;
                rb.MinWidth = 44;
                rb.IsChecked = i == selectedIndex;
                rb.Checked += delegate { picked(idx); };
                items[i] = rb;
            }
            Border box = Ui.SegBox("#2C2C2E", items);
            box.HorizontalAlignment = HorizontalAlignment.Right;
            return box;
        }

        static int Pick(double value, double[] options)
        {
            int best = 0;
            for (int i = 1; i < options.Length; i++) if (Math.Abs(options[i] - value) < Math.Abs(options[best] - value)) best = i;
            return best;
        }

        // A quiet "reset" line under a settings group.
        static UIElement PageReset(string label, string what, RoutedEventHandler click)
        {
            DockPanel row = new DockPanel { Margin = new Thickness(4, 8, 0, 0) };
            Button b = Ui.Btn(label, click);
            b.Padding = new Thickness(12, 4, 12, 5);
            b.FontSize = 12;
            b.ToolTip = what;
            b.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(b);
            return row;
        }

        string openWheel;         // which colour row has its wheel open; survives settings rebuilds
        int vizStopSel = 4;       // which live-audio height point the wheel edits
        readonly List<Action> wheelClosers = new List<Action>();

        static Border Dot(Color c, bool selected, double size)
        {
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                Background = new SolidColorBrush(c), BorderThickness = new Thickness(selected ? 3 : 1),
                BorderBrush = selected ? Theme.Br("#FFFFFF") : Theme.Br("#4E4E54")
            };
        }

        // A rainbow dot that opens the colour wheel.
        static Border WheelDot(double size)
        {
            Border b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1), BorderBrush = Theme.Br("#4E4E54") };
            RadialGradientBrush white = new RadialGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255));
            LinearGradientBrush rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            string[] hues = { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#5AC8FA", "#007AFF", "#AF52DE", "#FF2D55" };
            for (int i = 0; i < hues.Length; i++) rainbow.GradientStops.Add(new GradientStop(Theme.Parse(hues[i]), i / (double)(hues.Length - 1)));
            Grid g = new Grid();
            g.Children.Add(new Border { CornerRadius = new CornerRadius(size / 2), Background = rainbow });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(size / 2), Background = white, Opacity = 0.55 });
            b.Child = g;
            return b;
        }

        // Swatches, a colour wheel, a hex box and (when it isn't the default) a Reset button.
        UIElement ColorEditor(string key, string[] swatches, Color current, bool canReset, Action<Color> picked, Action reset)
        {
            StackPanel sp = new StackPanel();
            WrapPanel dots = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Border host = new Border { Visibility = openWheel == key ? Visibility.Visible : Visibility.Collapsed };
            foreach (string hex in swatches)
            {
                Color c = Theme.Parse(hex);
                Border dot = Dot(c, Theme.Hex(c) == Theme.Hex(current), 28);
                dot.Margin = new Thickness(0, 0, 8, 6);
                dot.Cursor = Cursors.Hand;
                dot.ToolTip = hex;
                Color cap = c;
                dot.MouseLeftButtonUp += delegate { picked(cap); };
                dots.Children.Add(dot);
            }
            bool custom = !swatches.Any(h => Theme.Hex(Theme.Parse(h)) == Theme.Hex(current));
            Border wheelDot = WheelDot(28);
            wheelDot.Margin = new Thickness(0, 0, 8, 6);
            wheelDot.Cursor = Cursors.Hand;
            wheelDot.ToolTip = "Color wheel: pick any color";
            if (custom) { wheelDot.BorderThickness = new Thickness(3); wheelDot.BorderBrush = Theme.Br("#FFFFFF"); }
            dots.Children.Add(wheelDot);
            sp.Children.Add(dots);

            DockPanel tools = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
            TextBlock lbl = Ui.Txt("Hex", 12, "#8E8E93");
            lbl.Margin = new Thickness(0, 0, 10, 0);
            tools.Children.Add(lbl);
            TextBox box = new TextBox { Text = Theme.Hex(current), Width = 96, FontFamily = new FontFamily("Cascadia Code, Consolas") };
            Action commit = delegate
            {
                Color c;
                if (Theme.TryParse(box.Text.Trim(), out c)) { if (Theme.Hex(c) != Theme.Hex(current)) picked(c); }
                else box.Text = Theme.Hex(current);
            };
            box.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
            box.LostKeyboardFocus += delegate { commit(); };
            tools.Children.Add(box);
            // The wheel has its own hex box: while it is open this one steps aside, so the same number isn't shown twice.
            Action<bool> showHex = delegate (bool show) { lbl.Visibility = show ? Visibility.Visible : Visibility.Collapsed; box.Visibility = lbl.Visibility; };
            showHex(openWheel != key);
            if (canReset && reset != null)
            {
                Button rs = Ui.Btn("Reset", delegate { if (openWheel == key) openWheel = null; reset(); });
                rs.Padding = new Thickness(12, 4, 12, 5);
                rs.Margin = new Thickness(10, 0, 0, 0);
                rs.FontSize = 12;
                rs.ToolTip = "Back to how Toneline EQ comes";
                tools.Children.Add(rs);
            }
            sp.Children.Add(tools);

            if (openWheel == key)
            {
                ColorWheel w = new ColorWheel(current);
                w.Changed += delegate (Color c) { box.Text = Theme.Hex(c); };
                w.Committed += delegate (Color c) { picked(c); };
                host.Child = w;
            }
            Action close = delegate { if (host.Visibility == Visibility.Visible) { Motion.Reveal(host, false); showHex(true); } };
            wheelClosers.Add(close);
            wheelDot.MouseLeftButtonUp += delegate
            {
                bool open = openWheel != key;
                foreach (Action a in wheelClosers) a();
                openWheel = open ? key : null;
                if (!open) return;
                showHex(false);
                ColorWheel w = new ColorWheel(current);
                w.Changed += delegate (Color c) { box.Text = Theme.Hex(c); };
                w.Committed += delegate (Color c) { picked(c); };
                host.Child = w;
                Motion.Reveal(host, true);
            };
            sp.Children.Add(host);
            return sp;
        }

        // Live-audio colours: five height points from the floor (quiet, deepest cut) to the peak (loud, biggest boost).
        UIElement VizColorEditor()
        {
            Color[] stops = LevelColors.Stops(VizColor);
            string[] names = { "Floor", "Low", "Middle", "High", "Peak" };
            string[] hints = { "quietest bars, deepest cuts", "", "flat (0 dB)", "", "loudest bars, biggest boosts" };
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            LinearGradientBrush bar = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int k = 0; k < 5; k++) bar.GradientStops.Add(new GradientStop(stops[4 - k], k * 0.25));
            g.Children.Add(new Border { Width = 26, CornerRadius = new CornerRadius(8), Background = bar, BorderBrush = Theme.Br("#4E4E54"), BorderThickness = new Thickness(1), ToolTip = "How the bars and the curve change color with height" });
            StackPanel rows = new StackPanel();
            Grid.SetColumn(rows, 2);
            for (int k = 4; k >= 0; k--)
            {
                int idx = k;
                bool sel = openWheel == "viz" && vizStopSel == k;
                DockPanel row = new DockPanel();
                Border dot = Dot(stops[k], sel, 22);
                dot.Margin = new Thickness(0, 0, 10, 0);
                row.Children.Add(dot);
                TextBlock hx = Ui.Txt(Theme.Hex(stops[k]), 11.5, "#8E8E93");
                hx.FontFamily = new FontFamily("Cascadia Code, Consolas");
                DockPanel.SetDock(hx, Dock.Right);
                row.Children.Insert(0, hx);
                StackPanel words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(Ui.Txt(names[k], 13, "#FFFFFF"));
                if (hints[k].Length > 0) words.Children.Add(Ui.Txt(hints[k], 11, "#8E8E93"));
                row.Children.Add(words);
                Border hit = new Border
                {
                    Child = row, Padding = new Thickness(8, 5, 10, 5), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
                    Background = sel ? Theme.Br("#2C2C2E") : Brushes.Transparent, ToolTip = "Pick the " + names[k].ToLowerInvariant() + " color"
                };
                hit.MouseLeftButtonUp += delegate
                {
                    vizStopSel = idx;
                    openWheel = openWheel == "viz" && vizStopSel == idx && sel ? null : "viz";
                    RebuildSettings();
                };
                rows.Children.Add(hit);
            }
            g.Children.Add(rows);
            sp.Children.Add(g);

            if (openWheel == "viz")
            {
                ColorWheel w = new ColorWheel(stops[vizStopSel]);
                w.Committed += delegate (Color c)
                {
                    Color[] next = LevelColors.Stops(VizColor);
                    next[vizStopSel] = c;
                    Theme.VizStops = next;
                    VizColor = 3;
                    OptionsChanged();
                    ApplyTheme();
                };
                Border host = new Border { Child = w };
                sp.Children.Add(host);
                Motion.Slide(host, 0, -6, 260, 0);
            }
            if (VizColor == 3 || Theme.VizStops != null)
            {
                Button rs = Ui.Btn("Reset", delegate { Theme.VizStops = null; VizColor = 0; if (openWheel == "viz") openWheel = null; OptionsChanged(); ApplyTheme(); });
                rs.Padding = new Thickness(12, 4, 12, 5);
                rs.FontSize = 12;
                rs.Margin = new Thickness(0, 10, 0, 0);
                rs.HorizontalAlignment = HorizontalAlignment.Left;
                rs.ToolTip = "Back to the Spectrum colors";
                sp.Children.Add(rs);
            }
            return sp;
        }

        static Border Block(string title, string desc, UIElement content)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(Ui.Txt(title, 13.5, "#FFFFFF"));
            if (!string.IsNullOrEmpty(desc))
            {
                TextBlock d = Ui.Txt(desc, 12, "#8E8E93");
                d.TextWrapping = TextWrapping.Wrap;
                d.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(d);
            }
            if (content != null) sp.Children.Add(content);
            return new Border { Child = sp, Padding = new Thickness(16, 11, 16, 12), Tag = "row" };
        }

        // ---------------------------------------------------------------- Settings
        public void OpenSettings()
        {
            if (settingsWin != null) { settingsWin.Activate(); return; }
            settingsWin = MakeChildWindow("Settings", 860, 760);
            settingsWin.MinWidth = 720;
            settingsWin.Closed += delegate { settingsWin = null; settingsScroll = null; settingsNav = null; };
            RebuildSettings();
            settingsWin.Show();
            Ui.FadeIn(settingsWin.Content as UIElement, 10, 260);
        }

        void RebuildSettings()
        {
            if (settingsWin == null) return;
            double offset = settingsScroll != null ? settingsScroll.VerticalOffset : 0;
            StyleChild(settingsWin);
            StackPanel sp = new StackPanel { Margin = new Thickness(26, 4, 26, 26) };

            sp.Children.Add(SectionTitle("General"));
            sp.Children.Add(OptionRow("Start with Windows", "Toneline EQ opens quietly in the tray when you sign in.", Toggle(IsStartup(), delegate (bool on) { SetStartup(on); })));
            sp.Children.Add(OptionRow("Keep running in the tray when I close the window", "Off means the close button quits Toneline EQ. Your EQ stays on either way, because Equalizer APO applies it.", Toggle(CloseToTray, delegate (bool on) { CloseToTray = on; SaveAll(); })));
            sp.Children.Add(OptionRow("Show tray notifications", "The reminder that Toneline EQ is still running after you close the window.", Toggle(Notify, delegate (bool on) { Notify = on; SaveAll(); })));
            sp.Children.Add(OptionRow("Check for updates automatically", "Once a day Toneline EQ asks its GitHub page whether a newer version is out. Nothing is installed until you click Update. You have version " + Program.Version + ".",
                Toggle(AutoUpdateCheck, delegate (bool on) { SetAutoUpdateCheck(on); })));
            WrapPanel upd = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            AddLinkButton(upd, "Check for updates now", delegate { CheckForUpdate(true); });
            sp.Children.Add(upd);
            sp.Children.Add(OptionRow("Use less memory in the tray", "A few seconds after you close the window, Toneline EQ frees its interface, so it uses very little memory in the background. Reopening takes a moment longer.", Toggle(LowMemory, delegate (bool on) { LowMemory = on; SaveAll(); })));
            string keys = "Ctrl+Alt+E: headphone EQ on/off.  Ctrl+Alt+M: mic EQ on/off.  Ctrl+Alt+Page Down / Page Up: next / previous headphone preset. They work in games too and show a small on-screen message.";
            if (Hotkeys && HotkeyProblems.Count > 0) keys += "  Already used by another app: " + string.Join(", ", HotkeyProblems) + ".";
            sp.Children.Add(OptionRow("Keyboard shortcuts", keys, Toggle(Hotkeys, delegate (bool on) { Hotkeys = on; ApplyHotkeySetting(); SaveAll(); RebuildSettings(); })));
            sp.Children.Add(PageReset("Reset these settings", "Tray, notifications, memory and shortcuts go back to how Toneline EQ comes. Start with Windows is left as it is.", delegate
            {
                CloseToTray = true; Notify = true; LowMemory = true;
                if (!Hotkeys) { Hotkeys = true; ApplyHotkeySetting(); }
                SaveAll(); RebuildSettings();
            }));

            sp.Children.Add(SectionTitle("Equalizer"));
            sp.Children.Add(OptionRow("Automatic preamp", "Keeps each preamp exactly as low as your boosts need, so nothing distorts. Turns the manual preamp controls off.", Toggle(AutoPreamp, delegate (bool on) { AutoPreamp = on; OptionsChanged(); })));
            sp.Children.Add(OptionRow("Level-matched EQ off", "When you switch an EQ off, keep its preamp. Otherwise 'off' is louder, and louder always sounds better, which makes fair comparisons impossible.", Toggle(LevelMatch, delegate (bool on) { LevelMatch = on; SaveAll(); ScheduleApply(); })));
            sp.Children.Add(OptionRow("Graph range", "How many dB the EQ graph shows above and below zero.",
                Segmented(new string[] { "±12 dB", "±15 dB", "±24 dB" }, Pick(GraphRange, new double[] { 12, 15, 24 }), delegate (int i) { GraphRange = (new double[] { 12, 15, 24 })[i]; OptionsChanged(); })));

            sp.Children.Add(PageReset("Reset these settings", "Manual preamp, level-matched off, ±15 dB graph.", delegate
            {
                AutoPreamp = false; LevelMatch = true; GraphRange = 15;
                OptionsChanged(); RebuildSettings();
            }));

            sp.Children.Add(SectionTitle("Devices"));
            sp.Children.Add(DeviceBlock(OutPage, 0, "Headphone EQ applies to", "Pick one output to EQ only that one, for example your IEM dongle, while other outputs stay untouched."));
            sp.Children.Add(DeviceBlock(MicPage, 1, "Mic EQ applies to", "Pick your microphone, or leave it on all microphones. Equalizer APO must be installed on it: Configurator, Capture devices tab."));
            StackPanel links = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            List<KeyValuePair<string, string>> map = DeviceLinks();
            if (map.Count == 0) links.Children.Add(Ui.Txt("No links yet. On a page, pick a preset and press 'Link to <device>'.", 12, "#8E8E93"));
            foreach (KeyValuePair<string, string> kv in map)
            {
                string key = kv.Key;
                DockPanel row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                Button rm = Ui.Btn("Remove", delegate { RemoveLink(key); RebuildSettings(); });
                rm.Padding = new Thickness(10, 3, 10, 3);
                DockPanel.SetDock(rm, Dock.Right);
                row.Children.Add(rm);
                TextBlock lt = Ui.Txt(DeviceNameForKey(key) + "  →  " + kv.Value, 12.5, "#E5E5EA");
                lt.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(lt);
                links.Children.Add(row);
            }
            sp.Children.Add(Block("Per-device presets", "Linked presets load automatically when that device becomes the Windows default, for example a correction for your earbuds' dongle and another for your headphones.", links));

            sp.Children.Add(SectionTitle("Visualizer"));
            sp.Children.Add(OptionRow("Frame rate", "Max follows your monitor's refresh rate (60, 144, 240 Hz...), the smoothest. 30 or 60 save a little power. It only runs while the window is open, and slows to 30 by itself while you're using another app, such as a game.",
                Segmented(new string[] { "30", "60", "Max" }, VizFps == 30 ? 0 : (VizFps <= 0 ? 2 : 1), delegate (int i) { VizFps = (new int[] { 30, 60, 0 })[i]; OptionsChanged(); })));
            sp.Children.Add(OptionRow("Sensitivity", "How quiet a sound still shows up. 60 dB is calmer, 100 dB shows the faintest detail.",
                Segmented(new string[] { "60 dB", "80 dB", "100 dB" }, Pick(VizRange, new double[] { 60, 80, 100 }), delegate (int i) { VizRange = (new double[] { 60, 80, 100 })[i]; OptionsChanged(); })));
            sp.Children.Add(OptionRow("Motion", "How quickly the spectrum falls back after a sound.",
                Segmented(new string[] { "Fast", "Normal", "Smooth" }, VizSmooth, delegate (int i) { VizSmooth = i; OptionsChanged(); })));
            sp.Children.Add(OptionRow("Colors", "Spectrum: bars warm up from blue to orange as they get louder; the curve is mint when flat, yellow and orange where it boosts, cyan and blue where it cuts. Custom: your own colors (Appearance, Live audio colors).", Segmented(new string[] { "Spectrum", "Accent", "Mono", "Custom" }, VizColor, delegate (int i)
                {
                    if (i == 3 && Theme.VizStops == null) Theme.VizStops = Theme.DefaultVizStops();
                    VizColor = i; OptionsChanged(); ApplyTheme();
                })));
            sp.Children.Add(OptionRow("Style", null, Segmented(new string[] { "Bars", "Line" }, VizFilled ? 0 : 1, delegate (int i) { VizFilled = i == 0; OptionsChanged(); })));
            sp.Children.Add(PageReset("Reset these settings", "Max frame rate, 80 dB, Normal motion, Spectrum colors, Bars.", delegate
            {
                VizFps = 0; VizRange = 80; VizSmooth = 1; VizColor = 0; VizFilled = true; Theme.VizStops = null;
                OptionsChanged(); ApplyTheme();
            }));

            BuildAppearance(sp);

            sp.Children.Add(SectionTitle("Files and Windows"));
            WrapPanel files = new WrapPanel();
            AddLinkButton(files, "My presets folder", delegate { try { Process.Start(Sys.Tool("explorer.exe"), "\"" + Store.PresetDir(PageKind.Output) + "\""); } catch { } });
            AddLinkButton(files, "Equalizer APO folder", delegate { OpenApoFolder(); });
            AddLinkButton(files, "Equalizer APO Configurator", delegate { OpenConfigurator(); });
            AddLinkButton(files, "Windows recording settings", delegate { OpenRecordingSettings(); });
            AddLinkButton(files, "Restore default settings", delegate { RestoreDefaults(); });
            AddLinkButton(files, "Uninstall Toneline EQ", delegate { try { Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--uninstall") { UseShellExecute = true }); } catch (Exception ex) { SetStatus(ex.Message); } });
            sp.Children.Add(files);
            TextBlock ver = Ui.Txt("Toneline EQ " + Program.Version + "  ·  powered by Equalizer APO", 11.5, "#8E8E93");
            ver.Margin = new Thickness(2, 12, 0, 0);
            sp.Children.Add(ver);
            TextBlock legal = Ui.Txt(LegalText, 11, "#8E8E93");
            legal.TextWrapping = TextWrapping.Wrap;
            legal.Margin = new Thickness(2, 6, 0, 0);
            sp.Children.Add(legal);
            WrapPanel legalLinks = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            AddLinkButton(legalLinks, "License (GPL-3.0)", delegate { OpenUrl("https://www.gnu.org/licenses/gpl-3.0.html"); });
            AddLinkButton(legalLinks, "Third-party notices", delegate { OpenInstalledDoc("THIRD-PARTY-NOTICES.txt"); });
            AddLinkButton(legalLinks, "Privacy", delegate { OpenInstalledDoc("PRIVACY.txt"); });
            sp.Children.Add(legalLinks);

            GroupRows(sp);
            LayoutSettingsPages(sp);

            // Left: categories on the window material. Right: the chosen page, which fades in.
            bool glass = ChildGlass(settingsWin);
            Grid split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsNav = new ListBox { Margin = new Thickness(10, 14, 10, 10) };
            foreach (KeyValuePair<string, StackPanel> navKv in settingsPages)
            {
                StackPanel item = new StackPanel { Orientation = Orientation.Horizontal };
                item.Children.Add(CategoryIcon(navKv.Key, 22));
                TextBlock label = Ui.Txt(navKv.Key, 13.5, "#FFFFFF");
                label.Margin = new Thickness(10, 0, 0, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                item.Children.Add(label);
                settingsNav.Items.Add(new ListBoxItem { Content = item, Padding = new Thickness(8, 6, 8, 6) });
            }
            Border navPane = new Border { Child = settingsNav, Background = glass ? (Brush)Brushes.Transparent : Theme.Br("#121214") };
            split.Children.Add(navPane);
            settingsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Tag = "content" };
            settingsBody = new Border { Child = settingsScroll, Background = Theme.Br(glass ? "#C0000000" : "#000000") };
            Grid.SetColumn(settingsBody, 1);
            split.Children.Add(settingsBody);
            settingsNav.SelectionChanged += delegate
            {
                int i = settingsNav.SelectedIndex;
                if (i < 0 || i >= settingsPages.Count) return;
                settingsPage = settingsPages[i].Key;
                settingsScroll.Content = settingsPages[i].Value;
                settingsScroll.ScrollToTop();
                Ui.FadeIn(settingsPages[i].Value, 8, 220);
            };
            UIElement content = Ui.WrapBackground(split, glass);
            ((FrameworkElement)content).LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale);
            settingsWin.Content = content;
            int start = Math.Max(0, settingsPages.FindIndex(kv => kv.Key == settingsPage));
            settingsNav.SelectedIndex = start;
            ScrollViewer target = settingsScroll;
            settingsWin.Dispatcher.BeginInvoke(new Action(delegate { target.ScrollToVerticalOffset(offset); }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public const string LegalText =
            "Copyright \u00A9 2026 ExecPath7. Toneline EQ is free software under the GNU General Public License v3: you may share and change it under its terms. " +
            "It comes with ABSOLUTELY NO WARRANTY. Use at your own risk: big EQ boosts can be loud, so start with the volume low. " +
            "Toneline EQ collects no data. It is not affiliated with Equalizer APO, AutoEQ, Apple, Microsoft or any headphone brand; their names are trademarks of their owners.";

        // Opens a notice file installed next to TonelineEQ.exe (or in the source folder when run from a build).
        static void OpenInstalledDoc(string name)
        {
            try
            {
                string dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string f = Path.Combine(dir, name);
                if (!File.Exists(f)) f = Path.Combine(Path.GetDirectoryName(dir) ?? dir, name);
                if (File.Exists(f)) Process.Start(new ProcessStartInfo(f) { UseShellExecute = true });
                else OpenUrl("https://www.gnu.org/licenses/gpl-3.0.html");
            }
            catch (Exception ex) { Store.LogError(ex); }
        }

        static void AddLinkButton(WrapPanel panel, string text, RoutedEventHandler click)
        {
            Button b = Ui.Btn(text, click);
            b.Margin = new Thickness(0, 0, 8, 8);
            panel.Children.Add(b);
        }

        Border DeviceBlock(EqPage page, int flow, string title, string desc)
        {
            ListBox list = new ListBox { MaxHeight = 200, Margin = new Thickness(0, 8, 0, 0) };
            ListBoxItem all = new ListBoxItem { Content = flow == 0 ? "All outputs" : "All microphones", Tag = null };
            list.Items.Add(all);
            ListBoxItem pick = all;
            foreach (AudioDevice d in AudioDevices.List(flow, false))
            {
                bool? apoOn = AudioDevices.HasApo(d.Guid, flow);
                string label = d.Name + (apoOn == true ? "   ·  Equalizer APO ✓" : (apoOn == false ? "   ·  no Equalizer APO" : ""));
                ListBoxItem it = new ListBoxItem { Content = label, Tag = d, ToolTip = d.Name + "\n" + d.Guid + (apoOn == false ? "\nEqualizer APO isn't installed on this device. Tick it in the Configurator to EQ it." : "") };
                list.Items.Add(it);
                if (string.Equals(d.Guid, page.DeviceGuid, StringComparison.OrdinalIgnoreCase)) pick = it;
            }
            if (pick == all && page.DeviceGuid.Length > 0)
            {
                AudioDevice missing = new AudioDevice { Guid = page.DeviceGuid, Id = page.DeviceId, Name = page.DeviceName };
                ListBoxItem m = new ListBoxItem { Content = page.DeviceName + " (not connected)", Tag = missing };
                list.Items.Add(m);
                pick = m;
            }
            list.SelectedItem = pick;
            list.SelectionChanged += delegate
            {
                ListBoxItem it = list.SelectedItem as ListBoxItem;
                if (it == null) return;
                AudioDevice d = it.Tag as AudioDevice;
                page.DeviceGuid = d != null ? d.Guid : "";
                page.DeviceId = d != null ? (d.Id ?? "") : "";
                page.DeviceName = d != null ? d.Name : "";
                page.UpdateDeviceText();
                page.RestartCapture();
                devicesRead = DateTime.MinValue;
                ApplyNow();
            };
            return Block(title, desc, list);
        }

        void BuildAppearance(StackPanel sp)
        {
            sp.Children.Add(SectionTitle("Appearance"));
            if (Glass.Supported)
                sp.Children.Add(OptionRow("Translucent window", "The Windows 11 Mica material: your desktop's colors softly show through. Off while a background picture is set.",
                    Toggle(Translucent, delegate (bool on) { Translucent = on; ApplyGlass(); SaveAll(); })));

            wheelClosers.Clear();
            WrapPanel presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            bool isDefault = Theme.IsDefaultLook && VizColor == 0;
            StackPanel dc = new StackPanel { Orientation = Orientation.Horizontal };
            Border mark = Ui.Logo(18);
            mark.Margin = new Thickness(0, 0, 8, 0);
            dc.Children.Add(mark);
            dc.Children.Add(new TextBlock { Text = "Default", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Button defBtn = new Button { Content = dc, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 4, 12, 4), ToolTip = "How Toneline EQ looks when you first install it: black, blue accent, solid panels, Inter, Spectrum colors." };
            if (isDefault) { defBtn.BorderBrush = Theme.Br("#D4B470"); defBtn.BorderThickness = new Thickness(2); }
            defBtn.Click += delegate { Theme.ResetAll(); VizColor = 0; openWheel = null; OptionsChanged(); ApplyTheme(); };
            presets.Children.Add(defBtn);
            foreach (ThemePreset tp in Theme.Presets)
            {
                ThemePreset cap = tp;
                StackPanel c = new StackPanel { Orientation = Orientation.Horizontal };
                Color bg = Theme.Parse(tp.Bg), ac = Theme.Parse(tp.Accent);
                c.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(ac), BorderBrush = new SolidColorBrush(bg), BorderThickness = new Thickness(3), Margin = new Thickness(0, 0, 8, 0) });
                c.Children.Add(new TextBlock { Text = tp.Name, VerticalAlignment = VerticalAlignment.Center });
                Button b = new Button { Content = c, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 5, 12, 5) };
                b.Click += delegate
                {
                    Theme.Bg = Theme.Parse(cap.Bg);
                    Theme.Text = Theme.Parse(cap.Text);
                    Theme.Accent = Theme.Parse(cap.Accent);
                    ApplyTheme();
                };
                presets.Children.Add(b);
            }
            sp.Children.Add(Block("Theme", "Default is how Toneline EQ comes. The others set accent, background and text together; fine-tune each below.", presets));

            sp.Children.Add(Block("Accent color", "Used for highlights: switches, selected items and buttons.",
                ColorEditor("accent", Theme.AccentSwatches, Theme.Accent, Theme.Hex(Theme.Accent) != Theme.Hex(Theme.DefAccent),
                    delegate (Color c) { Theme.Accent = c; ApplyTheme(); }, delegate { Theme.Accent = Theme.DefAccent; ApplyTheme(); })));
            sp.Children.Add(Block("Background color", "Every panel, border and muted label is derived from this and the text color. A light background switches Toneline EQ to light mode.",
                ColorEditor("bg", Theme.BgSwatches, Theme.Bg, Theme.Hex(Theme.Bg) != Theme.Hex(Theme.DefBg),
                    delegate (Color c) { Theme.Bg = c; Theme.FixTextContrast(); ApplyTheme(); }, delegate { Theme.Bg = Theme.DefBg; Theme.FixTextContrast(); ApplyTheme(); })));
            sp.Children.Add(Block("Text color", null,
                ColorEditor("text", Theme.TextSwatches, Theme.Text, Theme.Hex(Theme.Text) != Theme.Hex(Theme.DefaultTextFor(Theme.Bg)),
                    delegate (Color c) { Theme.Text = c; ApplyTheme(); }, delegate { Theme.Text = Theme.DefaultTextFor(Theme.Bg); ApplyTheme(); })));
            sp.Children.Add(Block("Live audio colors", "The visualizer and the EQ curve change color with height. Click a height point, then pick its color on the wheel.", VizColorEditor()));

            sp.Children.Add(OptionRow("Panels", "How see-through the cards are. Clear shows more of the background picture or glow.",
                Segmented(new string[] { "Solid", "Glass", "Clear" }, Pick(Theme.PanelOpacity, new double[] { 1.0, 0.85, 0.6 }), delegate (int i) { Theme.PanelOpacity = (new double[] { 1.0, 0.85, 0.6 })[i]; ApplyTheme(); })));
            sp.Children.Add(OptionRow("Background glow", "The soft light in the corner of the window.",
                Segmented(new string[] { "Off", "Soft", "Strong" }, Pick(Theme.Glow, new double[] { 0, 1, 1.8 }), delegate (int i) { Theme.Glow = (new double[] { 0, 1, 1.8 })[i]; ApplyTheme(); })));
            string[] glowSwatches = Theme.AccentSwatches.Concat(new[] { "#FFFFFF" }).ToArray();
            sp.Children.Add(Block("Glow color", Theme.GlowColor.HasValue ? "Your own glow color." : "Follows the accent until you pick one. Picking a color turns the glow on.",
                ColorEditor("glow", glowSwatches, Theme.GlowColor ?? Theme.Accent, Theme.GlowColor.HasValue,
                    delegate (Color c) { Theme.GlowColor = c; if (Theme.Glow <= 0) Theme.Glow = 1; ApplyTheme(); },
                    delegate { Theme.GlowColor = null; ApplyTheme(); })));

            StackPanel img = new StackPanel();
            WrapPanel imgBtns = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button choose = Ui.Btn("Choose picture…", delegate { ChooseBackgroundImage(); });
            choose.Margin = new Thickness(0, 0, 8, 6);
            imgBtns.Children.Add(choose);
            if (!string.IsNullOrEmpty(Theme.ImagePath))
            {
                Button remove = Ui.Btn("Remove picture", delegate { Theme.ImagePath = ""; ApplyTheme(); });
                remove.Margin = new Thickness(0, 0, 8, 6);
                imgBtns.Children.Add(remove);
            }
            img.Children.Add(imgBtns);
            DockPanel dimRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            dimRow.Children.Add(Ui.Txt("Darken picture", 12, "#8E8E93"));
            UIElement dimSeg = Segmented(new string[] { "Light", "Medium", "Strong" }, Pick(Theme.ImageDim, new double[] { 0.35, 0.55, 0.75 }), delegate (int i) { Theme.ImageDim = (new double[] { 0.35, 0.55, 0.75 })[i]; ApplyTheme(); });
            DockPanel.SetDock(dimSeg, Dock.Right);
            dimRow.Children.Insert(0, dimSeg);
            img.Children.Add(dimRow);
            sp.Children.Add(Block("Background picture", string.IsNullOrEmpty(Theme.ImagePath) ? "Any JPG, PNG or BMP. It fills the window behind the panels." : "Using " + Path.GetFileName(Theme.ImagePath) + ".", img));

            StackPanel fontBox = new StackPanel();
            WrapPanel fonts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            string group = "font" + Guid.NewGuid().ToString("N");
            foreach (string f in Theme.Fonts)
            {
                string cap = f;
                RadioButton rb = Ui.Radio(Theme.FontLabel(f), "Pill", group);
                rb.FontFamily = Theme.FamilyFor(f);
                rb.FontSize = 13;
                rb.Margin = new Thickness(0, 0, 6, 6);
                rb.IsChecked = Theme.FontName == f;
                rb.Checked += delegate { Theme.FontName = cap; ApplyTheme(); };
                fonts.Children.Add(rb);
            }
            fontBox.Children.Add(fonts);
            DockPanel other = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            TextBlock ol = Ui.Txt("Any installed font", 12, "#8E8E93");
            ol.Margin = new Thickness(0, 0, 10, 0);
            other.Children.Add(ol);
            TextBox fontName = new TextBox { Text = Theme.FontName, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            Action commitFont = delegate
            {
                string v = fontName.Text.Trim();
                if (v.Length > 0 && v != Theme.FontName) { Theme.FontName = v; ApplyTheme(); }
            };
            fontName.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { commitFont(); e.Handled = true; } };
            fontName.LostKeyboardFocus += delegate { commitFont(); };
            other.Children.Add(fontName);
            fontBox.Children.Add(other);
            if (Theme.FontName != Theme.DefaultFont)
            {
                Button rf = Ui.Btn("Reset", delegate { Theme.FontName = Theme.DefaultFont; ApplyTheme(); });
                rf.Padding = new Thickness(12, 4, 12, 5);
                rf.FontSize = 12;
                rf.Margin = new Thickness(10, 0, 0, 0);
                other.Children.Add(rf);
            }
            sp.Children.Add(Block("Font", null, fontBox));

            sp.Children.Add(OptionRow("Interface size", "Scales the whole window: text, controls and graph.",
                Segmented(new string[] { "90%", "100%", "110%", "125%" }, Pick(Theme.Scale, new double[] { 0.9, 1.0, 1.1, 1.25 }), delegate (int i) { Theme.Scale = (new double[] { 0.9, 1.0, 1.1, 1.25 })[i]; ApplyTheme(); })));

            sp.Children.Add(PageReset("Reset all appearance settings", "Same as the Default theme: colors, glow, panels, picture, font, size and live audio colors.", delegate
            {
                Theme.ResetAll(); VizColor = 0; openWheel = null; OptionsChanged(); ApplyTheme();
            }));
        }

        void ChooseBackgroundImage()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Choose a background picture";
            dlg.Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*";
            if (dlg.ShowDialog(settingsWin ?? (Window)this) != true) return;
            try
            {
                string dest = Path.Combine(Store.Dir, "background" + Path.GetExtension(dlg.FileName).ToLowerInvariant());
                foreach (string old in Directory.GetFiles(Store.Dir, "background.*")) { try { File.Delete(old); } catch { } }
                File.Copy(dlg.FileName, dest, true);
                Theme.ImagePath = dest;
                ApplyTheme();
            }
            catch (Exception ex) { Store.LogError(ex); SetStatus("Couldn't use that picture: " + ex.Message); }
        }

        void RestoreDefaults()
        {
            if (!Confirm("Restore default settings?", "Tray, equalizer, visualizer and appearance settings go back to how Toneline EQ comes. Your presets, device links and the EQ you have now are kept.", "Restore")) return;
            CloseToTray = true; Notify = true; AutoPreamp = false; VizFilled = true; VizColor = 0;
            GraphRange = 15; VizRange = 80; VizFps = 0; VizSmooth = 1;
            LevelMatch = true; LowMemory = true;
            if (!Hotkeys) { Hotkeys = true; ApplyHotkeySetting(); }
            Theme.ResetAll();
            OptionsChanged();
            ApplyTheme();
        }

        // A short question before something that can't be undone, in the app's own look. Cancel is the default
        // (Enter and Esc both cancel), and the button that goes ahead is red, as on a phone.
        public bool Confirm(string title, string message, string actionLabel)
        {
            if (Program.SelfTest) return true;
            bool ok = false;
            MakeConfirm(title, message, actionLabel, delegate (bool yes) { ok = yes; }).ShowDialog();
            return ok;
        }

        // Same question, with the app's accent color on the button that goes ahead (for choices that aren't destructive).
        public bool Ask(string title, string message, string actionLabel, string cancelLabel)
        {
            if (Program.SelfTest) return true;
            bool ok = false;
            MakeConfirm(title, message, actionLabel, delegate (bool yes) { ok = yes; }, false, cancelLabel).ShowDialog();
            return ok;
        }

        public Window MakeConfirm(string title, string message, string actionLabel, Action<bool> answered)
        {
            return MakeConfirm(title, message, actionLabel, answered, true, "Cancel");
        }

        public Window MakeConfirm(string title, string message, string actionLabel, Action<bool> answered, bool destructive, string cancelLabel)
        {
            Window win = new Window
            {
                Title = "Toneline EQ", Width = 430, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, FontSize = 13, UseLayoutRounding = true,
                Background = Theme.Br("#000000"), Foreground = Theme.Br("#E5E5EA"), FontFamily = Theme.Family
            };
            Window owner = settingsWin != null && settingsWin.IsActive ? settingsWin : (Window)this;
            if (owner.IsVisible) { win.Owner = owner; win.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DarkTitle(win);

            StackPanel sp = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            TextBlock head = Ui.Txt(title, 16, "#FFFFFF");
            head.FontWeight = FontWeights.SemiBold;
            head.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(head);
            TextBlock body = Ui.Txt(message, 13, "#AEAEB2");
            body.TextWrapping = TextWrapping.Wrap;
            body.LineHeight = 19;
            body.Margin = new Thickness(0, 8, 0, 22);
            sp.Children.Add(body);

            DockPanel buttons = new DockPanel { LastChildFill = false };
            Button go = Ui.Btn(actionLabel, delegate { if (answered != null) answered(true); win.Close(); });
            if (destructive)
            {
                go.Background = Theme.Solid(Theme.Parse(Theme.IsLight ? "#FF3B30" : "#FF453A"));
                go.Foreground = Brushes.White;
            }
            else Ui.Primary(go);
            go.FontWeight = FontWeights.SemiBold;
            go.Padding = new Thickness(18, 7, 18, 7);
            DockPanel.SetDock(go, Dock.Right);
            buttons.Children.Add(go);
            Button cancel = Ui.Btn(cancelLabel, delegate { win.Close(); });
            cancel.IsCancel = true;
            cancel.IsDefault = true;
            cancel.Padding = new Thickness(18, 7, 18, 7);
            cancel.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);

            UIElement content = Ui.WrapBackground(sp);
            ((FrameworkElement)content).LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale);
            win.Content = content;
            win.Loaded += delegate { cancel.Focus(); };
            return win;
        }

        // ---------------------------------------------------------------- Guide
        public void OpenGuide(PageKind kind)
        {
            guideKind = kind;
            if (guideWin != null) { RebuildGuide(); guideWin.Activate(); return; }
            guideWin = MakeChildWindow("Toneline EQ guide", 760, 840);
            guideWin.Closed += delegate { guideWin = null; };
            RebuildGuide();
            guideWin.Show();
            Ui.FadeIn(guideWin.Content as UIElement, 10, 260);
        }

        void RebuildGuide()
        {
            if (guideWin == null) return;
            StyleChild(guideWin);
            StackPanel sp = new StackPanel { Margin = new Thickness(28, 14, 28, 28), MaxWidth = 700 };

            RadioButton tOut = Ui.Radio("Headphones guide", "Tab", "guide");
            RadioButton tMic = Ui.Radio("Microphone guide", "Tab", "guide");
            tOut.IsChecked = guideKind == PageKind.Output;
            tMic.IsChecked = guideKind == PageKind.Mic;
            tOut.Checked += delegate { guideKind = PageKind.Output; RebuildGuide(); };
            tMic.Checked += delegate { guideKind = PageKind.Mic; RebuildGuide(); };
            Border tabsBox = Ui.SegBox("#1C1C1E", tOut, tMic);
            sp.Children.Add(tabsBox);

            Action<string> h = delegate (string t)
            {
                TextBlock ht = Ui.Txt(t, 18, "#FFFFFF");
                ht.FontWeight = FontWeights.Bold;
                ht.Margin = new Thickness(2, 26, 0, 10);
                sp.Children.Add(ht);
            };
            Action<string> para = delegate (string t)
            {
                TextBlock tb = Ui.Txt(t, 13, "#E5E5EA");
                tb.TextWrapping = TextWrapping.Wrap;
                tb.LineHeight = 20;
                tb.Margin = new Thickness(2, 0, 0, 10);
                sp.Children.Add(tb);
            };
            Action<string, string> item = delegate (string term, string text)
            {
                Grid g = new Grid { Margin = new Thickness(2, 0, 0, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock a = Ui.Txt(term, 13, "#FFFFFF");
                a.FontWeight = FontWeights.SemiBold;
                a.VerticalAlignment = VerticalAlignment.Top;
                a.TextWrapping = TextWrapping.Wrap;
                a.Margin = new Thickness(0, 0, 10, 0);
                TextBlock b = Ui.Txt(text, 13, "#E5E5EA");
                b.TextWrapping = TextWrapping.Wrap;
                b.LineHeight = 20;
                b.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(b, 1);
                g.Children.Add(a);
                g.Children.Add(b);
                sp.Children.Add(g);
            };
            Action<string, RoutedEventHandler> button = delegate (string text, RoutedEventHandler click)
            {
                Button bt = Ui.Btn(text, click);
                bt.HorizontalAlignment = HorizontalAlignment.Left;
                bt.Margin = new Thickness(2, 0, 0, 12);
                sp.Children.Add(bt);
            };

            if (guideKind == PageKind.Output)
            {
                h("Hz and kHz");
                para("Sound is vibration, and frequency is how fast it vibrates, measured in hertz (Hz): vibrations per second. Low numbers are deep sounds and high numbers are bright ones. 1 kHz (kilohertz) is 1000 Hz, so 2.5 kHz = 2500 Hz. People hear roughly 20 Hz to 20 kHz, which is the width of the graph. Each doubling (100 to 200 Hz, 1 to 2 kHz) is one octave, which is why the graph spaces them evenly.");
                h("What lives where");
                item("20-60 Hz  Sub-bass", "Rumble you feel: the weight of a kick drum, movie explosions. Boost for impact, cut if it sounds bloated or your headphones distort.");
                item("60-250 Hz  Bass", "Punch and warmth from kick drums and bass guitar. Too much sounds boomy and hides everything else.");
                item("250-500 Hz  Low mids", "The body of voices and instruments. Too much is the classic muddy or boxy sound; a small cut here is the quickest way to clean things up.");
                item("500 Hz-2 kHz  Mids", "The core of voices, guitars and piano. Too much sounds honky or nasal, too little sounds hollow.");
                item("2-5 kHz  Presence", "Clarity, speech and in-game footsteps. Ears are most sensitive here, so too much is harsh and tiring on long sessions.");
                item("5-10 kHz  Treble", "Crispness, cymbals and 's' sounds. Too much is sharp and sibilant.");
                item("10-20 kHz  Air", "Sparkle and a sense of space. Small changes go a long way.");
            }
            else
            {
                h("Set up your mic (once)");
                para("Equalizer APO only processes microphones you enable. Open its Configurator, go to the Capture devices tab, tick your mic (it shows up by name), click OK and restart Windows. Then pick presets on the Microphone page like you do for headphones. Discord, games, OBS and every other app get the EQ'd voice.");
                button("Open Equalizer APO Configurator", delegate { OpenConfigurator(); });
                h("Hear what you sound like");
                para("You won't hear mic EQ unless you listen back. Easiest: press Test my mic at the top of the Microphone page. Toneline EQ records 5 seconds with the EQ applied and plays it back. For live listening: Windows recording settings, double-click your mic, Listen tab, tick 'Listen to this device'. Untick it when done, because it adds a small echo. Or record yourself in Voice Recorder, or use Discord's Let's Check mic test. The Visualizer on the Microphone page shows your voice live.");
                button("Open Windows recording settings", delegate { OpenRecordingSettings(); });
                h("What lives where in a voice");
                item("Below 80 Hz  Rumble", "Desk bumps, fans, footsteps and electrical hum. Your voice has almost nothing here: a high pass at 70-100 Hz removes it safely. Do this first.");
                item("80-250 Hz  Boom", "Chest and warmth. It grows the closer you are to the mic (proximity effect). Cut a little if you sound boomy or muffled.");
                item("250-500 Hz  Mud", "Room reflections and boxiness. A 2-3 dB cut around 300 Hz makes most voices clearer.");
                item("500 Hz-2 kHz  Body", "The core of your voice. Too much around 1 kHz sounds nasal or like a phone call.");
                item("2-5 kHz  Presence", "How understandable you are. A gentle 2-3 dB boost helps you cut through game audio. Too much sounds harsh.");
                item("5-9 kHz  Sibilance", "'S' and 'sh' sounds and mouth clicks. If they sting, cut a narrow band (Q 3-5) where it's sharpest.");
                item("9-20 kHz  Air", "Breath and sparkle. A small high shelf sounds open; on budget mics it mostly adds hiss.");
                h("A simple order that works");
                para("1. High pass around 80 Hz. 2. Cut mud around 250-400 Hz. 3. Add a little presence around 3-4 kHz. 4. Tame harsh 's' sounds if needed. 5. Press Auto for the preamp. Set your input level on the interface first: loud talking should peak well below the top, then EQ.");
                h("Apps that fight your EQ");
                para("Discord, Zoom and some games have their own noise suppression and automatic gain control. If your voice pumps or sounds processed, turn off Automatic Gain Control in that app and let Toneline EQ do the shaping.");
            }

            h("Gain (dB)");
            para("Gain is how much a band boosts (+) or cuts (-) its frequencies, in decibels. +1 dB is barely noticeable, +3 dB is a clear change, +6 dB is a big one and +10 dB sounds about twice as loud. Cutting what you don't want usually sounds cleaner than boosting what you do.");

            h("Q (width)");
            para("Q sets how wide a band is. A low Q is wide and gentle; a high Q is narrow and surgical. The dot's label on the graph shows the width in octaves.");
            item("Q 0.5", "About 2.5 octaves. Very broad, gentle tone shaping.");
            item("Q 0.7", "About 2 octaves. Broad. Also the smooth default for shelves and pass filters.");
            item("Q 1.4", "About 1 octave. The standard for a graphic-style EQ.");
            item("Q 2-4", "Half to a third of an octave. Targeted fixes.");
            item("Q 5-10", "Very narrow. Removing one harsh ring or boomy note.");

            h("Filter types");
            item("Peak (bell)", "Boosts or cuts around its frequency in a hill or dip shape. Q sets the width. The most common tool.");
            item("Low shelf", "Raises or lowers everything below its frequency by the same amount. The cleanest way to add or remove bass, because it doesn't stack up like several bells do.");
            item("High shelf", "Raises or lowers everything above its frequency. The cleanest way to make the sound brighter or darker.");
            item("Low pass", "Keeps the lows and removes everything above its frequency. Useful against hiss or harshness. Gain does nothing.");
            item("High pass", "Removes everything below its frequency. On a mic it removes rumble; on speakers it protects them from deep bass. Gain does nothing.");

            h("Preamp and clipping");
            para("Digital audio has a ceiling. If a boost pushes loud parts past it, they clip, which sounds like crackling or harshness. The preamp lowers everything first to leave room. Rule of thumb: set the preamp to minus your biggest boost. Auto does it for you, and Automatic preamp in Settings keeps it right all the time.");

            h("Effects and plugins");
            para("Each page has an Effects & plugins tab. Effects run in order, before or after the EQ, and can be switched on and off individually.");
            item("Gain", "Volume trim for the whole chain.");
            if (guideKind == PageKind.Output)
            {
                item("Balance", "Shifts the sound left or right by turning the other side down.");
                item("Mono / Swap L/R", "Mix both channels together, or swap left and right.");
                item("Impulse response", "A .wav that Equalizer APO convolves with your audio: room simulation, headphone correction or surround virtualizers made for Equalizer APO.");
                item("VST plugin", "Any 64-bit VST2 plugin (.dll), such as a limiter or exciter. Put a limiter after the EQ to catch peaks.");
            }
            else
            {
                item("VST plugin", "Any 64-bit VST2 plugin (.dll). For a mic, the classics are a noise gate (silences the background when you're not talking) and noise reduction before the EQ, and a compressor (evens out loud and quiet speech) after it. The free ReaPlugs bundle from Cockos includes ReaGate, ReaFIR and ReaComp.");
                item("Impulse response", "A .wav convolved with your voice, for example a room or radio effect.");
            }
            item("Custom lines", "Any Equalizer APO commands, one per line, for anything Toneline EQ doesn't have a control for.");
            para("Plugin settings: plugins start with their default settings. To fine-tune one, set it up in Equalizer APO's own Editor, copy the ChunkData part of its VSTPlugin line, and paste it into the plugin's settings box in Toneline EQ.");

            if (guideKind == PageKind.Output)
            {
                h("Presets and AutoEQ");
                para("AutoEQ corrections are measured fixes that move a headphone towards a neutral target. They're a starting point: add your own taste on top, like a little bass shelf. Use the preset that matches what you're wearing; an IEM correction on other headphones will sound wrong. Find my headphones... (in the presets list) downloads one for you; Import... opens a ParametricEQ.txt you saved yourself.");
            }

            h("The visualizer");
            para(guideKind == PageKind.Output
                ? "The bars behind the curve are a live spectrum of what Windows is playing: tall bars on the left mean lots of bass, on the right lots of treble, and louder bars turn from blue to yellow and orange. The curve is your EQ: mint where it leaves the sound alone, yellow and orange where it boosts, cyan and blue where it cuts. Point at a dot to see its exact values. It only runs while the window is open."
                : "On the Microphone page the visualizer shows your mic live, so you can see rumble below 80 Hz, how much boom your voice has and where harsh 's' sounds peak. Windows shows its mic-in-use icon while it runs; switch the visualizer off to stop that.");

            h("If the EQ does nothing");
            item("Device not ticked", "Equalizer APO only works on devices ticked in its Configurator. Toneline EQ shows a red note on the page when the current device isn't, with a Fix button. Bluetooth headphones often appear twice (Headphones and Headset); tick both.");
            item("After a Windows or driver update", "Big updates can remove Equalizer APO from your devices. Open the Configurator, tick them again, press OK and restart. If it still doesn't work, tick 'Troubleshooting options' there and try the other install mode.");
            item("Banner says not connected", "Press Connect at the top. If Windows blocks it, use Restart as administrator once.");
            item("Sound got quieter", "That's the preamp making room for boosts. Raise the Windows volume a little. Cutting instead of boosting needs less preamp.");
            item("Crackling or distortion", "A boost is clipping. Press Auto next to the preamp, or switch on Automatic preamp in Settings.");

            h("Handy extras");
            item("Per-device presets", "Pick a preset and press 'Link to <device>' at the top of the page. From then on it loads by itself whenever that device becomes the default, for example when you switch from earbuds to headphones.");
            if (guideKind == PageKind.Output) item("Find my headphones", "Searches the AutoEQ database of measured headphones and IEMs and loads a correction in one click.");
            item("Keyboard shortcuts", "Ctrl+Alt+E and Ctrl+Alt+M switch the headphone and mic EQ on or off; Ctrl+Alt+Page Down / Page Up change the headphone preset. They work in games and show a small message.");
            item("Undo", "Ctrl+Z and Ctrl+Y, or the Undo and Redo buttons, step back and forth through your changes.");
            item("Arrow keys", "Click a dot on the graph, then nudge it with the keyboard: up and down change the gain, left and right the frequency. Hold Shift for small steps. Delete removes the band.");
            item("Share code", "In the ⋯ menu next to Save: Copy share code puts your EQ in one short line (Toneline:...). Send it to anyone; they paste it with Paste from clipboard. Plain Equalizer APO or AutoEQ text pastes too.");

            h("Tips");
            para("Make small moves (2-3 dB) and compare with the EQ on/off switch. Listen at your normal volume, since everything sounds better louder. Cut before you boost. Save presets you like so you can switch in one click from the tray icon.");

            ScrollViewer sv = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            UIElement content = Ui.WrapBackground(sv, ChildGlass(guideWin));
            ((FrameworkElement)content).LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale);
            guideWin.Content = content;
        }
    }
}
