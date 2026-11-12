// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - main window: header, pages, Equalizer APO connection, saving, tray and startup.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace TonelineEQ
{
    // ------------------------------------------------------------------ Equalizer APO integration
    public static class Apo
    {
        public const string FileName = "TonelineEQ.txt";
        public const string LegacyFileName = "EasyEQ.txt";   // what this app wrote when it was called EasyEQ

        public static string ConfigDir()
        {
            try
            {
                using (RegistryKey hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = hk.OpenSubKey(@"SOFTWARE\EqualizerAPO"))
                {
                    if (k != null)
                    {
                        string v = k.GetValue("ConfigPath") as string;
                        if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v;
                    }
                }
            }
            catch { }
            string pf = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string d = Path.Combine(Path.Combine(pf, "EqualizerAPO"), "config");
            return Directory.Exists(d) ? d : null;
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                using (FileStream fs = new FileStream(Path.Combine(dir, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite)) { }
                return true;
            }
            catch { return false; }
        }

        public static bool IsHooked(string dir)
        {
            try
            {
                string cfg = Path.Combine(dir, "config.txt");
                if (!File.Exists(cfg)) return false;
                foreach (string l in File.ReadAllLines(cfg))
                    if (Regex.IsMatch(l.Trim(), @"^Include\s*:\s*TonelineEQ\.txt$", RegexOptions.IgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        // Before the rename, config.txt included EasyEQ.txt. Point it at TonelineEQ.txt instead (keeping the same EQ),
        // and empty the old file so the EQ is never applied twice. Returns true when something was moved.
        public static bool MigrateLegacy(string dir)
        {
            try
            {
                string cfg = Path.Combine(dir, "config.txt");
                if (!File.Exists(cfg)) return false;
                string text = File.ReadAllText(cfg);
                Regex inc = new Regex(@"^(\s*Include\s*:\s*)EasyEQ\.txt(\s*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (!inc.IsMatch(text)) return false;
                string oldFile = Path.Combine(dir, LegacyFileName), newFile = Path.Combine(dir, FileName);
                if (File.Exists(oldFile) && !File.Exists(newFile)) File.Copy(oldFile, newFile);
                string next = inc.Replace(text, "${1}" + FileName + "${2}").Replace("# Managed by EasyEQ.", "# Managed by Toneline EQ.");
                File.WriteAllText(cfg, next, new UTF8Encoding(false));
                if (File.Exists(oldFile)) File.WriteAllText(oldFile, "# EasyEQ is now Toneline EQ, which writes " + FileName + ".\r\n", new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public static string Hook(string dir)
        {
            string cfg = Path.Combine(dir, "config.txt");
            string backup = null;
            if (File.Exists(cfg))
            {
                backup = "config-before-TonelineEQ-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                File.Copy(cfg, Path.Combine(dir, backup), true);
            }
            string text = "# Managed by Toneline EQ." + (backup != null ? " Your previous settings are in " + backup + "." : "") + "\r\nInclude: " + FileName + "\r\n";
            File.WriteAllText(cfg, text, new UTF8Encoding(false));
            return backup;
        }

        public static string Configurator(string configDir)
        {
            if (configDir == null) return null;
            string exe = Path.Combine(Path.GetDirectoryName(configDir.TrimEnd('\\')), "Configurator.exe");
            return File.Exists(exe) ? exe : null;
        }
    }

    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public EqPage OutPage, MicPage;
        EqPage active;

        // options (Settings window)
        public bool AutoPreamp = false, VizFilled = true, CloseToTray = true, Notify = true;
        public int VizColor = 0;
        public bool SidebarOpen = false;
        public bool Translucent = true;
        bool glassOn;
        Border toast;
        TextBlock toastText;
        public bool GlassOn { get { return glassOn; } }
        bool WantGlass() { return Translucent && Glass.Supported && string.IsNullOrEmpty(Theme.ImagePath); }
        Button sidebarBtn;
        public bool LevelMatch = true, Hotkeys = true, LowMemory = true;
        readonly Dictionary<string, string> deviceMap = new Dictionary<string, string>(); // "out|{guid}" -> preset name
        public double GraphRange = 15, VizRange = 80;
        public int VizFps = 0, VizSmooth = 1;

        string apoDir;
        bool apoWritable, apoHooked;
        Action bannerAction;
        string bannerMsg, bannerBtnText;
        string lastStatus = "";
        DispatcherTimer applyTimer, saveTimer, watchTimer, trimTimer;
        string lastConfig;
        bool uiBuilt;

        List<AudioDevice> outDevices = new List<AudioDevice>(), micDevices = new List<AudioDevice>();
        DateTime devicesRead = DateTime.MinValue;

        TextBlock statusText, bannerText;
        Border statusChip;
        bool cfgChangedPulse;
        System.Windows.Shapes.Ellipse statusDot;
        Border banner;
        Button bannerBtn;
        Grid pageHost;

        WF.NotifyIcon tray;
        WF.ContextMenuStrip trayMenu;
        WF.ToolStripMenuItem trayOut, trayMic, trayPresets, trayMicPresets, trayStartup;
        bool quitting, trayTipShown;

        public MainWindow()
        {
            Title = "Toneline EQ";
            Width = 1220; Height = 820; MinWidth = 960; MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13;
            UseLayoutRounding = true;
            DarkTitle(this);

            applyTimer = new DispatcherTimer();
            applyTimer.Interval = TimeSpan.FromMilliseconds(220);
            applyTimer.Tick += delegate { applyTimer.Stop(); ApplyNow(); };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveAll(); };
            trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            trimTimer.Tick += delegate { trimTimer.Stop(); FreeUiIfHidden(); };
            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            watchTimer.Tick += delegate { WatchDevices(); };

            Dictionary<string, string> s = Store.LoadSettings();
            // Only the installed copy may switch on "Start with Windows" by itself (the installer shows that choice).
            // A copy that is just being tried from a download or build folder never adds itself to Windows startup.
            bool askStartup = !s.ContainsKey("startupAsked") && IsInstalledCopy();
            Theme.Load(s);
            LoadOptions(s);
            OutPage = new EqPage(PageKind.Output, this);
            MicPage = new EqPage(PageKind.Mic, this);
            OutPage.LoadState(s);
            MicPage.LoadState(s);
            string tab;
            active = s.TryGetValue("tab", out tab) && tab == "mic" ? MicPage : OutPage;

            try { Application.Current.Resources = Theme.Resources(); } catch (Exception ex) { Store.LogError(ex); }
            // Windows 11: translucent Mica window with Toneline EQ's header as the title bar, like a Mac app.
            glassOn = WantGlass();
            if (glassOn) Glass.SetChrome(this, true, 58);
            SourceInitialized += delegate { if (glassOn && !Glass.Enable(this, false)) { glassOn = false; Glass.SetChrome(this, false, 0); if (uiBuilt) BuildUI(); } };
            // Started quietly with Windows: don't build any UI until the window is actually opened.
            if (!Program.StartInTray) BuildUI();

            PreviewKeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (Keyboard.FocusedElement is TextBox) return;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.Z) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) active.Redo(); else active.Undo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Y) { active.Redo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.OemComma) { OpenSettings(); e.Handled = true; }
            else if (e.Key == Key.Delete || e.Key == Key.Back) { active.RemoveSelected(); e.Handled = true; }
            };
            Closing += delegate (object sender, CancelEventArgs e)
            {
                if (applyTimer.IsEnabled) { applyTimer.Stop(); ApplyNow(); }
                if (quitting) return;
                e.Cancel = true;
                if (!CloseToTray) { Dispatcher.BeginInvoke(new Action(QuitApp)); return; }
                Hide();
                if (Notify && !trayTipShown && tray != null)
                {
                    trayTipShown = true;
                    tray.ShowBalloonTip(4000, "Toneline EQ is still running", "Your EQ stays on. Click the tray icon to open Toneline EQ, or right-click it to quit.", WF.ToolTipIcon.None);
                }
            };
            IsVisibleChanged += delegate
            {
                UpdateVisualizers();
                if (!IsVisible && LowMemory) { trimTimer.Stop(); trimTimer.Start(); }
            };
            StateChanged += delegate { UpdateVisualizers(); FitMaximized(); };

            if (!Program.SelfTest) SetupTray();
            Application.Current.SessionEnding += delegate { quitting = true; };
            Application.Current.Activated += delegate { AppActive = true; };
            Application.Current.Deactivated += delegate { AppActive = false; };
            // Older versions kept the last "Test my mic" recording in the temp folder. It now stays in memory; remove the leftover.
            try { string oldRec = Path.Combine(Path.GetTempPath(), "TonelineEQ-mic-test.wav"); if (File.Exists(oldRec)) File.Delete(oldRec); } catch { }
            Application.Current.Exit += delegate { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } };
            if (askStartup && !Program.SelfTest) SetStartup(true);
            StartUpdater();
            new WindowInteropHelper(this).EnsureHandle();
            if (!Program.SelfTest) SetupHotkeys();
            WatchDevicesNow();
            watchTimer.Start();
            RefreshApo();
            ApplyNow();
            if (Program.StartInTray) Dispatcher.BeginInvoke(new Action(TrimMemory), DispatcherPriority.ApplicationIdle);
        }

        // True while Toneline EQ (any of its windows) is the app you're using. The visualizer slows down when it isn't.
        public bool AppActive = true;

        static bool IsInstalledCopy()
        {
            try { return string.Equals(Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName), Path.GetFullPath(Installer.InstalledExe), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ---------------------------------------------------------------- options
        void LoadOptions(Dictionary<string, string> s)
        {
            string v;
            double d;
            int n;
            if (s.TryGetValue("closeToTray", out v)) CloseToTray = v != "0";
            if (s.TryGetValue("notify", out v)) Notify = v != "0";
            if (s.TryGetValue("autoPreamp", out v)) AutoPreamp = v == "1";
            if (s.TryGetValue("vizFilled", out v)) VizFilled = v != "0";
            if (s.TryGetValue("vizColor", out v) && int.TryParse(v, NumberStyles.Integer, IC, out n)) VizColor = Math.Max(0, Math.Min(3, n));
            // Values from the settings file are kept inside what the Settings window offers, so a damaged file can't break the graph.
            if (s.TryGetValue("graphRange", out v) && double.TryParse(v, NumberStyles.Float, IC, out d)) GraphRange = ApoFormat.Clamp(d, 6, 24);
            if (s.TryGetValue("vizRange", out v) && double.TryParse(v, NumberStyles.Float, IC, out d)) VizRange = ApoFormat.Clamp(d, 40, 120);
            if (s.TryGetValue("vizRate", out v) && int.TryParse(v, NumberStyles.Integer, IC, out n)) VizFps = Math.Max(0, Math.Min(360, n));
            if (s.TryGetValue("vizSmooth", out v) && int.TryParse(v, NumberStyles.Integer, IC, out n)) VizSmooth = Math.Max(0, Math.Min(2, n));
            if (s.TryGetValue("levelMatch", out v)) LevelMatch = v != "0";
            if (s.TryGetValue("hotkeys", out v)) Hotkeys = v != "0";
            if (s.TryGetValue("lowMemory", out v)) LowMemory = v != "0";
            if (s.TryGetValue("translucent", out v)) Translucent = v != "0";
            if (s.TryGetValue("sidebar", out v)) SidebarOpen = v == "1";
            LoadUpdateOptions(s);
            deviceMap.Clear();
            foreach (KeyValuePair<string, string> kv in s)
                if (kv.Key.StartsWith("map.") && kv.Value.Length > 0) deviceMap[kv.Key.Substring(4)] = kv.Value;
        }

        public void SaveAll()
        {
            try
            {
                Dictionary<string, string> s = new Dictionary<string, string>();
                s["startupAsked"] = "1";
                s["tab"] = active == MicPage ? "mic" : "out";
                s["closeToTray"] = CloseToTray ? "1" : "0";
                s["notify"] = Notify ? "1" : "0";
                s["autoPreamp"] = AutoPreamp ? "1" : "0";
                s["vizFilled"] = VizFilled ? "1" : "0";
                s["vizColor"] = VizColor.ToString(IC);
                s["graphRange"] = GraphRange.ToString(IC);
                s["vizRange"] = VizRange.ToString(IC);
                s["vizRate"] = VizFps.ToString(IC);
                s["vizSmooth"] = VizSmooth.ToString(IC);
                s["levelMatch"] = LevelMatch ? "1" : "0";
                s["hotkeys"] = Hotkeys ? "1" : "0";
                s["lowMemory"] = LowMemory ? "1" : "0";
                s["translucent"] = Translucent ? "1" : "0";
                s["sidebar"] = SidebarOpen ? "1" : "0";
                SaveUpdateOptions(s);
                foreach (KeyValuePair<string, string> kv in deviceMap) s["map." + kv.Key] = kv.Value;
                Theme.Save(s);
                OutPage.SaveState(s);
                MicPage.SaveState(s);
                Store.SaveSettings(s);
            }
            catch (Exception ex) { Store.LogError(ex); }
        }

        public void OptionsChanged()
        {
            OutPage.ApplyOptions();
            MicPage.ApplyOptions();
            SaveAll();
            ScheduleApply();
        }

        // ---------------------------------------------------------------- layout
        static void DarkTitle(Window w)
        {
            w.SourceInitialized += delegate
            {
                try
                {
                    IntPtr h = new WindowInteropHelper(w).Handle;
                    int on = Theme.IsLight ? 0 : 1;
                    if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
                }
                catch { }
            };
        }

        void BuildUI()
        {
            OutPage.SetVisualizerActive(false);
            MicPage.SetVisualizerActive(false);
            if (Theme.Translucent != glassOn) { Theme.Translucent = glassOn; Theme.Invalidate(); }
            Background = glassOn ? Brushes.Transparent : Theme.Br("#000000");
            Foreground = Theme.Br("#E5E5EA");
            FontFamily = Theme.Family;

            Grid root = new Grid { Margin = new Thickness(22, 16, 22, 20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // header
            DockPanel header = new DockPanel { Margin = new Thickness(0, 0, glassOn ? Glass.CaptionButtonsWidth - 22 : 0, 16), MinHeight = 36 };
            StackPanel chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            statusDot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = lastLive ? Theme.Solid(Theme.Green) : Theme.Br("#636366"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            statusText = Ui.Txt(lastStatus, 12, "#C7C7CC");
            statusText.FontWeight = FontWeights.Medium;
            chipRow.Children.Add(statusDot);
            chipRow.Children.Add(statusText);
            Border chip = new Border { Child = chipRow, Padding = new Thickness(11, 6, 13, 6), VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(15), Background = Theme.Br("#1C1C1E") };
            statusChip = chip;
            DockPanel.SetDock(chip, Dock.Right);
            header.Children.Add(chip);
            Button pill = MakeUpdatePill();
            DockPanel.SetDock(pill, Dock.Right);
            header.Children.Add(pill);
            ShowUpdatePill();
            Button setBtn = Ui.Icon("\uE713", "Settings (Ctrl+,): startup, tray, devices, visualizer, look and plugins", delegate { OpenSettings(); });
            setBtn.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(setBtn, Dock.Right);
            header.Children.Add(setBtn);
            Button guideBtn = Ui.Icon("\uE897", "Guide: what Hz, kHz, gain, Q, filter types and effects mean", delegate { OpenGuide(active.Kind); });
            guideBtn.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(guideBtn, Dock.Right);
            header.Children.Add(guideBtn);

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal };
            sidebarBtn = Ui.Icon(SidebarOpen ? "\uE89F" : "\uE8A0", "Show or hide your presets", delegate { ToggleSidebar(); });
            sidebarBtn.Margin = new Thickness(0, 0, 14, 0);
            sidebarBtn.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(sidebarBtn);
            Border logo = Ui.Logo(34);
            logo.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(logo);
            TextBlock name = Ui.Txt("Toneline EQ", 21, "#FFFFFF");
            name.FontWeight = FontWeights.Bold;
            left.Children.Add(name);

            RadioButton tabOut = Ui.Radio("Headphones", "Tab", "pages");
            tabOut.ToolTip = "EQ for what you hear: headphones, IEMs and speakers";
            RadioButton tabMic = Ui.Radio("Microphone", "Tab", "pages");
            tabMic.ToolTip = "EQ for how you sound: your microphone in Discord, games and recordings";
            tabOut.IsChecked = active == OutPage;
            tabMic.IsChecked = active == MicPage;
            tabOut.Checked += delegate { if (!syncingTabs) ShowPage(OutPage); };
            tabMic.Checked += delegate { if (!syncingTabs) ShowPage(MicPage); };
            pageTabOut = tabOut; pageTabMic = tabMic;
            Border tabsBox = Ui.SegBox("#1C1C1E", tabOut, tabMic);
            tabsBox.Margin = new Thickness(28, 0, 0, 0);
            left.Children.Add(tabsBox);
            header.Children.Add(left);
            root.Children.Add(header);
            foreach (IInputElement el in new IInputElement[] { sidebarBtn, tabsBox, guideBtn, setBtn, logo }) WindowChrome.SetIsHitTestVisibleInChrome(el, true);

            // banner
            DockPanel bp = new DockPanel();
            bannerBtn = Ui.Btn("", delegate { if (bannerAction != null) bannerAction(); });
            Ui.Primary(bannerBtn);
            DockPanel.SetDock(bannerBtn, Dock.Right);
            bp.Children.Add(bannerBtn);
            bannerText = Ui.Txt("", 13, "#F2F2F7");
            bannerText.TextWrapping = TextWrapping.Wrap;
            bannerText.Margin = new Thickness(0, 0, 14, 0);
            bp.Children.Add(bannerText);
            banner = Ui.Card(bp);
            banner.Background = Theme.Br("#1C1C1E");
            banner.BorderThickness = new Thickness(1);
            banner.BorderBrush = Theme.Br("#5C4D2E");
            banner.Margin = new Thickness(0, 0, 0, 12);
            banner.Visibility = Visibility.Collapsed;
            Grid.SetRow(banner, 1);
            root.Children.Add(banner);
            if (bannerMsg != null) ShowBanner(bannerMsg, bannerBtnText, bannerAction);

            pageHost = new Grid();
            pageHost.Children.Add(OutPage.Build());
            pageHost.Children.Add(MicPage.Build());
            Grid.SetRow(pageHost, 2);
            root.Children.Add(pageHost);

            // Apple-style toast for confirmations ("Saved", "Copied"...), floating above the page.
            toastText = Ui.Txt("", 13, "#FFFFFF");
            toastText.FontWeight = FontWeights.SemiBold;
            toast = new Border { Child = toastText, Background = Theme.Br("#F02C2C2E"), BorderBrush = Theme.Br("#3A3A3C"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 9, 18, 10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 22), IsHitTestVisible = false, Opacity = 0 };
            Grid.SetRow(toast, 2);
            root.Children.Add(toast);

            root.LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale);
            Content = Ui.WrapBackground(root, glassOn);
            uiBuilt = true;
            FitMaximized();
            ShowPage(active);
            UpdateApoWarnings();
        }

        // A maximised window with a custom title bar hangs slightly off-screen; pad it back in.
        void FitMaximized()
        {
            FrameworkElement c = Content as FrameworkElement;
            if (c != null) c.Margin = glassOn && WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        }

        // Turns the translucent window on or off (Settings) without restarting.
        public void ApplyGlass()
        {
            bool want = WantGlass();
            if (want == glassOn) return;
            glassOn = want;
            if (want) { Glass.SetChrome(this, true, 58); if (new WindowInteropHelper(this).Handle != IntPtr.Zero && !Glass.Enable(this, false)) { glassOn = false; Glass.SetChrome(this, false, 0); } }
            else { Glass.SetChrome(this, false, 0); Glass.Disable(this, Theme.Br("#000000")); }
            if (uiBuilt) BuildUI();
        }

        // Status line plus a floating toast that fades away.
        public void Say(string text)
        {
            SetStatus(text);
            if (toast == null || !IsVisible) return;
            toastText.Text = text;
            DoubleAnimationUsingKeyFrames op = new DoubleAnimationUsingKeyFrames();
            op.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000))));
            op.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2350)), new CubicEase { EasingMode = EasingMode.EaseIn }));
            toast.BeginAnimation(UIElement.OpacityProperty, op);
            TranslateTransform t = new TranslateTransform(0, 14);
            toast.RenderTransform = t;
            DoubleAnimation up = new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(420)));
            up.EasingFunction = new SpringEase();
            t.BeginAnimation(TranslateTransform.YProperty, up);
        }

        public void ToggleSidebar()
        {
            SidebarOpen = !SidebarOpen;
            OutPage.SetSidebar(SidebarOpen);
            MicPage.SetSidebar(SidebarOpen);
            TextBlock glyph = sidebarBtn != null ? sidebarBtn.Content as TextBlock : null;
            if (glyph != null) glyph.Text = SidebarOpen ? "\uE89F" : "\uE8A0";
            saveTimer.Stop();
            saveTimer.Start();
        }

        EqPage lastShown;
        RadioButton pageTabOut, pageTabMic;
        bool syncingTabs;

        void ShowPage(EqPage page)
        {
            active = page;
            // Keep the Headphones / Microphone switch in step when the page changes some other way.
            if (pageTabOut != null && pageTabMic != null)
            {
                syncingTabs = true;
                pageTabOut.IsChecked = page == OutPage;
                pageTabMic.IsChecked = page == MicPage;
                syncingTabs = false;
            }
            OutPage.View.Visibility = page == OutPage ? Visibility.Visible : Visibility.Collapsed;
            MicPage.View.Visibility = page == MicPage ? Visibility.Visible : Visibility.Collapsed;
            // Headphones -> Microphone glides left, back glides right, like an iOS segmented switch.
            if (lastShown != null && lastShown != page) Motion.Slide(page.View, page == MicPage ? 26 : -26, 0, 320, 0);
            else Ui.FadeIn(page.View, 6, 230);
            lastShown = page;
            UpdateVisualizers();
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void UpdateVisualizers()
        {
            bool visible = IsVisible && WindowState != WindowState.Minimized;
            OutPage.SetVisualizerActive(visible && active == OutPage && OutPage.VizOn);
            MicPage.SetVisualizerActive(visible && active == MicPage && MicPage.VizOn);
            OutPage.SetShown(visible && active == OutPage);
        }

        public void ApplyTheme()
        {
            Theme.Invalidate();
            try { Application.Current.Resources = Theme.Resources(); } catch (Exception ex) { Store.LogError(ex); }
            if (WantGlass() != glassOn) ApplyGlass(); else BuildUI();
            if (trayMenu != null) { StyleMenu(trayMenu); StyleMenu(trayPresets.DropDown); StyleMenu(trayMicPresets.DropDown); }
            RebuildSettings();
            RebuildGuide();
            SaveAll();
        }

        // ---------------------------------------------------------------- status
        public void SetStatus(string text)
        {
            lastStatus = text;
            if (statusText != null) statusText.Text = text;
        }

        bool lastLive;   // kept so the dot stays green when the window is rebuilt (theme change, back from the tray)

        void SetLive(bool live)
        {
            lastLive = live;
            if (statusDot != null) statusDot.Fill = live ? Theme.Solid(Theme.Green) : Theme.Br("#636366");
        }

        // ---------------------------------------------------------------- applying to Equalizer APO
        public void ScheduleApply()
        {
            applyTimer.Stop();
            applyTimer.Start();
        }

        List<string> Guids(EqPage page, List<AudioDevice> all)
        {
            if (page.DeviceGuid.Length > 0) return new List<string> { page.DeviceGuid };
            return all.Select(d => d.Guid).ToList();
        }

        string BuildConfig()
        {
            if (devicesRead == DateTime.MinValue) RefreshDeviceLists();
            StringBuilder sb = new StringBuilder();
            sb.Append("# Written by Toneline EQ. Change it in Toneline EQ; this file is rewritten on every edit.\r\n\r\n");
            List<string> outs = Guids(OutPage, outDevices);
            if (outs.Count > 0)
            {
                sb.Append("# ---- Headphones & speakers: ").Append(ApoFormat.CleanName(OutPage.Current.Name)).Append("\r\n");
                sb.Append("Device: ").Append(string.Join(";", outs)).Append("\r\n");
                sb.Append(OutPage.ApoBody());
                List<string> mics = Guids(MicPage, micDevices);
                if (mics.Count > 0)
                {
                    sb.Append("\r\n# ---- Microphone: ").Append(ApoFormat.CleanName(MicPage.Current.Name)).Append("\r\n");
                    sb.Append("Device: ").Append(string.Join(";", mics)).Append("\r\n");
                    sb.Append(MicPage.ApoBody());
                }
            }
            else
            {
                // Device list unavailable: apply the headphone EQ everywhere, as older versions did.
                sb.Append(OutPage.ApoBody());
            }
            return sb.ToString();
        }

        public void ApplyNow()
        {
            saveTimer.Stop();
            saveTimer.Start();
            UpdateTrayText();
            if (apoDir == null || !apoWritable)
            {
                SetStatus("Not applied yet");
                SetLive(false);
                return;
            }
            try
            {
                string cfg = BuildConfig();
                cfgChangedPulse = cfg != lastConfig;
                if (cfg != lastConfig && !Program.SelfTest)
                {
                    File.WriteAllText(Path.Combine(apoDir, Apo.FileName), cfg, new UTF8Encoding(false));
                    lastConfig = cfg;
                }
                bool live = apoHooked && (!OutPage.Bypass || !MicPage.Bypass);
                SetStatus(apoHooked ? (live ? "Live" : "Paused") : "Saved, not connected");
                if (statusChip != null) statusChip.ToolTip = (apoHooked ? "Equalizer APO is applying your EQ. " : "Equalizer APO isn't connected yet. ") + "Last saved " + DateTime.Now.ToString("HH:mm:ss");
                if (live && statusDot != null && cfgChangedPulse) Motion.Pulse(statusDot);
                SetLive(live);
            }
            catch (Exception ex)
            {
                Store.LogError(ex);
                SetStatus("Couldn't save: " + ex.Message);
                RefreshApo();
            }
        }

        void RefreshApo()
        {
            apoDir = Apo.ConfigDir();
            if (apoDir == null)
            {
                apoWritable = apoHooked = false;
                ShowBanner("Equalizer APO isn't installed. Toneline EQ needs it to change Windows sound. Install it, tick your headphones, DAC and microphone in its Configurator, restart Windows, then open Toneline EQ again.",
                    "Get Equalizer APO", delegate { OpenUrl("https://sourceforge.net/projects/equalizerapo/"); });
                return;
            }
            apoWritable = Apo.CanWrite(apoDir);
            if (apoWritable && !Program.SelfTest) Apo.MigrateLegacy(apoDir);
            apoHooked = Apo.IsHooked(apoDir);
            if (!apoWritable)
                ShowBanner("Windows blocked Toneline EQ from saving into Equalizer APO's config folder. Run it as administrator once to connect.", "Restart as administrator", RestartAsAdmin);
            else if (!apoHooked)
                ShowBanner("Equalizer APO isn't reading Toneline EQ yet. Connect backs up your current config.txt and points Equalizer APO at Toneline EQ.", "Connect", Connect);
            else
                HideBanner();
            UpdateApoWarnings();
        }

        void ShowBanner(string text, string button, Action action)
        {
            bannerMsg = text;
            bannerBtnText = button;
            bannerAction = action;
            if (banner == null) return;
            bannerText.Text = text;
            bannerBtn.Content = button;
            Motion.Reveal(banner, true);
        }

        void HideBanner()
        {
            bannerMsg = null;
            bannerAction = null;
            if (banner != null) Motion.Reveal(banner, false);
        }

        void Connect()
        {
            try
            {
                string backup = Apo.Hook(apoDir);
                RefreshApo();
                ApplyNow();
                SetStatus(backup != null ? "Connected · old config saved as " + backup : "Connected");
            }
            catch (Exception ex)
            {
                Store.LogError(ex);
                ShowBanner("Couldn't change config.txt: " + ex.Message, "Restart as administrator", RestartAsAdmin);
            }
        }

        void RestartAsAdmin()
        {
            try
            {
                ApplyNow();
                ProcessStartInfo psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Program.ReleaseSingleInstance();
                Process.Start(psi);
                QuitApp();
            }
            catch (Win32Exception) { SetStatus("Administrator restart was cancelled"); }
            catch (Exception ex) { Store.LogError(ex); SetStatus(ex.Message); }
        }

        public void OpenApoFolder()
        {
            if (apoDir == null) { SetStatus("Equalizer APO isn't installed"); return; }
            try { Process.Start(Sys.Tool("explorer.exe"), "\"" + apoDir + "\""); } catch (Exception ex) { SetStatus(ex.Message); }
        }

        public void OpenConfigurator()
        {
            string exe = Apo.Configurator(apoDir);
            if (exe == null) { SetStatus("Equalizer APO's Configurator wasn't found"); return; }
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" }); }
            catch (Win32Exception) { }
            catch (Exception ex) { SetStatus(ex.Message); }
        }

        public static void OpenRecordingSettings()
        {
            try { Process.Start(Sys.Tool("control.exe"), "mmsys.cpl,,1"); } catch { }
        }

        // Opens a web page in the browser. Only real web addresses are passed on to Windows, never a file or a program.
        public static void OpenUrl(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
            try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }

        // ---------------------------------------------------------------- tray / background
        void SetupTray()
        {
            tray = new WF.NotifyIcon();
            try { tray.Icon = SD.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); }
            catch { tray.Icon = SD.SystemIcons.Application; }
            tray.MouseClick += delegate (object s, WF.MouseEventArgs e) { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };

            trayMenu = new WF.ContextMenuStrip();
            WF.ToolStripMenuItem open = new WF.ToolStripMenuItem("Open Toneline EQ", null, delegate { ShowFromTray(); });
            open.Font = new SD.Font("Segoe UI", 9.5f, SD.FontStyle.Bold);
            trayOut = new WF.ToolStripMenuItem("Headphone EQ on", null, delegate { OutPage.SetBypass(!OutPage.Bypass); });
            trayMic = new WF.ToolStripMenuItem("Mic EQ on", null, delegate { MicPage.SetBypass(!MicPage.Bypass); });
            trayPresets = new WF.ToolStripMenuItem("Headphone presets");
            trayMicPresets = new WF.ToolStripMenuItem("Mic presets");
            trayStartup = new WF.ToolStripMenuItem("Start with Windows", null, delegate { SetStartup(!IsStartup()); });
            WF.ToolStripMenuItem quit = new WF.ToolStripMenuItem("Quit Toneline EQ", null, delegate { QuitApp(); });
            trayMenu.Items.AddRange(new WF.ToolStripItem[] { open, new WF.ToolStripSeparator(), trayOut, trayPresets, trayMic, trayMicPresets, new WF.ToolStripSeparator(), trayStartup, quit });
            StyleMenu(trayMenu);
            StyleMenu(trayPresets.DropDown);
            StyleMenu(trayMicPresets.DropDown);
            trayMenu.Opening += delegate { UpdateTrayMenu(); };
            tray.ContextMenuStrip = trayMenu;
            tray.Visible = true;
            UpdateTrayText();
        }

        static void StyleMenu(WF.ToolStripDropDown m)
        {
            m.Renderer = new WF.ToolStripProfessionalRenderer(new ThemedMenuColors());
            m.BackColor = Theme.Win("#1C1C1E");
            m.ForeColor = Theme.Win("#FFFFFF");
            m.Font = new SD.Font("Segoe UI", 9.5f);
        }

        void FillPresetMenu(WF.ToolStripMenuItem menu, EqPage page)
        {
            menu.DropDownItems.Clear();
            string lastGroup = null;
            foreach (Preset p in page.AllPresets())
            {
                if (lastGroup != null && p.Group != lastGroup) menu.DropDownItems.Add(new WF.ToolStripSeparator());
                lastGroup = p.Group;
                Preset cap = p;
                WF.ToolStripMenuItem it = new WF.ToolStripMenuItem(p.Name.Replace("&", "&&"), null, delegate { page.LoadPreset(cap); page.SelectInList(cap.Name); });
                it.Checked = !page.Dirty && page.Current.Name == p.Name;
                menu.DropDownItems.Add(it);
            }
        }

        void UpdateTrayMenu()
        {
            trayOut.Checked = !OutPage.Bypass;
            trayMic.Checked = !MicPage.Bypass;
            trayStartup.Checked = IsStartup();
            FillPresetMenu(trayPresets, OutPage);
            FillPresetMenu(trayMicPresets, MicPage);
        }

        void UpdateTrayText()
        {
            if (tray == null) return;
            string t = "Toneline EQ · " + (OutPage.Bypass ? "EQ off" : OutPage.Current.Name);
            if (t.Length > 63) t = t.Substring(0, 60) + "...";
            tray.Text = t;
        }

        public void ShowFromTray()
        {
            if (!uiBuilt) BuildUI();
            bool wasHidden = !IsVisible || WindowState == WindowState.Minimized;
            Show();
            if (wasHidden) Ui.FadeIn(Content as UIElement, 0, 180);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        public void QuitApp()
        {
            if (quitting) return;
            quitting = true;
            SaveAll();
            UnregisterHotkeys();
            OutPage.SetVisualizerActive(false);
            MicPage.SetVisualizerActive(false);
            if (applyTimer.IsEnabled) { applyTimer.Stop(); ApplyNow(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Application.Current.Shutdown();
        }

        public static bool IsStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("TonelineEQ") != null;
            }
            catch { return false; }
        }

        public void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("TonelineEQ", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" --tray");
                    else k.DeleteValue("TonelineEQ", false);
                }
            }
            catch (Exception ex) { Store.LogError(ex); SetStatus("Couldn't change startup: " + ex.Message); }
        }
    }
}
