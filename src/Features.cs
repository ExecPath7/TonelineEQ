// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - device watching, per-device presets, Equalizer APO health check, global shortcuts,
// on-screen display, low-memory tray mode and AutoEQ search.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TonelineEQ
{
    public class AutoEqEntry
    {
        public string Name, Path, Source;
    }

    public partial class MainWindow
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

        string defOutId = "", defMicId = "";
        bool watchedOnce;
        string deviceSignature = "";

        // ---------------------------------------------------------------- devices
        void RefreshDeviceLists()
        {
            outDevices = AudioDevices.List(0, true);
            micDevices = AudioDevices.List(1, true);
            devicesRead = DateTime.Now;
        }

        // Runs every 3 seconds, also in the tray. The Windows audio queries run on a worker thread, so the
        // window and visualizer never stall; only the (cheap) result handling runs on the UI thread.
        bool watchBusy;
        void WatchDevices()
        {
            if (watchBusy) return;
            watchBusy = true;
            string knownOut = defOutId, knownMic = defMicId;
            bool listDue = (DateTime.Now - devicesRead).TotalSeconds > 20;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string o = "", m = "";
                List<AudioDevice> outs = null, mics = null;
                try
                {
                    o = AudioDevices.DefaultId(0) ?? "";
                    m = AudioDevices.DefaultId(1) ?? "";
                    if (listDue || o != knownOut || m != knownMic) { outs = AudioDevices.List(0, true); mics = AudioDevices.List(1, true); }
                }
                catch (Exception ex) { Store.LogError(ex); }
                Dispatcher.BeginInvoke(new Action(delegate { watchBusy = false; ApplyWatch(o, m, outs, mics); }));
            });
        }

        // First check at startup, synchronously, so the first Equalizer APO write already knows the devices.
        void WatchDevicesNow()
        {
            string o = AudioDevices.DefaultId(0) ?? "", m = AudioDevices.DefaultId(1) ?? "";
            ApplyWatch(o, m, AudioDevices.List(0, true), AudioDevices.List(1, true));
        }

        void ApplyWatch(string o, string m, List<AudioDevice> outs, List<AudioDevice> mics)
        {
            bool changed = false;
            if (o != defOutId)
            {
                defOutId = o;
                changed = true;
                if (watchedOnce) OnDefaultChanged(OutPage, o);
            }
            if (m != defMicId)
            {
                defMicId = m;
                changed = true;
                if (watchedOnce) OnDefaultChanged(MicPage, m);
            }
            if (outs != null && mics != null)
            {
                outDevices = outs;
                micDevices = mics;
                devicesRead = DateTime.Now;
                string sig = string.Join(";", outDevices.Select(d => d.Guid)) + "|" + string.Join(";", micDevices.Select(d => d.Guid));
                if (sig != deviceSignature)
                {
                    bool first = deviceSignature.Length == 0;
                    deviceSignature = sig;
                    if (!first) ScheduleApply(); // a device was added or removed: include it in the EQ
                }
            }
            if (changed)
            {
                OutPage.UpdateLink();
                MicPage.UpdateLink();
                UpdateApoWarnings();
            }
            watchedOnce = true;
        }

        string DefaultId(PageKind k) { return k == PageKind.Mic ? defMicId : defOutId; }

        public string DefaultDeviceName(PageKind k)
        {
            string id = DefaultId(k);
            if (id.Length == 0) return null;
            foreach (AudioDevice d in k == PageKind.Mic ? micDevices : outDevices)
                if (d.Id == id) return d.Name;
            return null;
        }

        static string MapKey(PageKind k, string guid) { return (k == PageKind.Mic ? "mic|" : "out|") + guid.ToLowerInvariant(); }

        public string LinkedPreset(PageKind k)
        {
            string guid = AudioDevices.GuidOf(DefaultId(k));
            if (guid.Length == 0) return null;
            string name;
            return deviceMap.TryGetValue(MapKey(k, guid), out name) ? name : null;
        }

        public void LinkPreset(EqPage page)
        {
            string guid = AudioDevices.GuidOf(DefaultId(page.Kind));
            string dev = DefaultDeviceName(page.Kind);
            if (guid.Length == 0 || dev == null) return;
            string key = MapKey(page.Kind, guid);
            string existing;
            if (deviceMap.TryGetValue(key, out existing) && existing == page.Current.Name)
            {
                deviceMap.Remove(key);
                Say("Unlinked " + dev);
            }
            else
            {
                deviceMap[key] = page.Current.Name;
                SetStatus(dev + " now loads “" + page.Current.Name + "”");
            }
            page.UpdateLink();
            SaveAll();
            RebuildSettings();
        }

        public List<KeyValuePair<string, string>> DeviceLinks()
        {
            return deviceMap.ToList();
        }

        public string DeviceNameForKey(string key)
        {
            bool mic = key.StartsWith("mic|");
            string guid = key.Substring(4);
            foreach (AudioDevice d in mic ? micDevices : outDevices)
                if (string.Equals(d.Guid, guid, StringComparison.OrdinalIgnoreCase)) return d.Name;
            return (mic ? "Microphone " : "Output ") + guid;
        }

        public void RemoveLink(string key)
        {
            deviceMap.Remove(key);
            OutPage.UpdateLink();
            MicPage.UpdateLink();
            SaveAll();
        }

        void OnDefaultChanged(EqPage page, string id)
        {
            string guid = AudioDevices.GuidOf(id);
            string preset;
            if (guid.Length == 0 || !deviceMap.TryGetValue(MapKey(page.Kind, guid), out preset)) return;
            if (page.Current.Name == preset && !page.Dirty) return;
            if (page.LoadPresetByName(preset))
            {
                if (devicesRead == DateTime.MinValue || (DateTime.Now - devicesRead).TotalSeconds > 1) RefreshDeviceLists();
                ShowOsd((DefaultDeviceName(page.Kind) ?? "Device") + "  ·  " + preset);
            }
        }

        // Warn on a page when the current default device has no Equalizer APO (common after Windows or driver updates).
        void UpdateApoWarnings()
        {
            WarnFor(OutPage, 0);
            WarnFor(MicPage, 1);
        }

        void WarnFor(EqPage page, int flow)
        {
            string id = flow == 0 ? defOutId : defMicId;
            string name = DefaultDeviceName(page.Kind);
            if (apoDir == null || id.Length == 0 || name == null) { page.ShowApoWarning(null); return; }
            if (page.DeviceGuid.Length > 0 && !string.Equals(page.DeviceGuid, AudioDevices.GuidOf(id), StringComparison.OrdinalIgnoreCase)) { page.ShowApoWarning(null); return; }
            bool? has = AudioDevices.HasApo(AudioDevices.GuidOf(id), flow);
            page.ShowApoWarning(has == false ? name : null);
        }

        // ---------------------------------------------------------------- global keyboard shortcuts
        HwndSource hwndSource;
        public readonly List<string> HotkeyProblems = new List<string>();
        static readonly string[] HotkeyNames = { "", "Ctrl+Alt+E", "Ctrl+Alt+M", "Ctrl+Alt+Page Down", "Ctrl+Alt+Page Up" };

        void SetupHotkeys()
        {
            IntPtr h = new WindowInteropHelper(this).EnsureHandle();
            if (hwndSource == null)
            {
                hwndSource = HwndSource.FromHwnd(h);
                if (hwndSource != null) hwndSource.AddHook(WndProc);
            }
            UnregisterHotkeys();
            HotkeyProblems.Clear();
            if (!Hotkeys) return;
            const uint mods = 0x0002 | 0x0001 | 0x4000; // Ctrl + Alt, no auto-repeat
            uint[] keys = { 0, 0x45, 0x4D, 0x22, 0x21 };   // E, M, Page Down, Page Up
            for (int id = 1; id < keys.Length; id++)
                if (!RegisterHotKey(h, id, mods, keys[id])) HotkeyProblems.Add(HotkeyNames[id]);
        }

        void UnregisterHotkeys()
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            for (int id = 1; id < HotkeyNames.Length; id++) UnregisterHotKey(h, id);
        }

        public void ApplyHotkeySetting()
        {
            SetupHotkeys();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312)
            {
                OnHotkey(wParam.ToInt32());
                handled = true;
            }
            return IntPtr.Zero;
        }

        void OnHotkey(int id)
        {
            switch (id)
            {
                case 1:
                    OutPage.SetBypass(!OutPage.Bypass);
                    ShowOsd("Headphone EQ " + (OutPage.Bypass ? "off" : "on"));
                    break;
                case 2:
                    MicPage.SetBypass(!MicPage.Bypass);
                    ShowOsd("Mic EQ " + (MicPage.Bypass ? "off" : "on"));
                    break;
                case 3:
                    ShowOsd(OutPage.NextPreset(1));
                    break;
                case 4:
                    ShowOsd(OutPage.NextPreset(-1));
                    break;
            }
        }

        // ---------------------------------------------------------------- on-screen display
        Window osd;
        DispatcherTimer osdTimer;

        public void ShowOsd(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (osd == null)
            {
                osd = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Focusable = false,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.WidthAndHeight
                };
                osd.SourceInitialized += delegate
                {
                    IntPtr h = new WindowInteropHelper(osd).Handle;
                    // no activation, click-through, hidden from Alt+Tab
                    SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x20 | 0x80);
                };
                osdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                osdTimer.Tick += delegate
                {
                    osdTimer.Stop();
                    DoubleAnimation fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                    fade.Completed += delegate { if (osd != null && !osdTimer.IsEnabled) osd.Hide(); };
                    osd.BeginAnimation(UIElement.OpacityProperty, fade);
                };
            }
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Theme.Br("#D4B470"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br("#FFFFFF"), FontFamily = Theme.Family, VerticalAlignment = VerticalAlignment.Center });
            osd.Content = new Border { Child = row, Background = Theme.Br("#F21C1C1E"), BorderBrush = Theme.Br("#38383A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 12, 22, 12) };
            osd.BeginAnimation(UIElement.OpacityProperty, null);
            osd.Opacity = 1;
            osd.Show();
            osd.UpdateLayout();
            Rect wa = SystemParameters.WorkArea;
            osd.Left = wa.Left + (wa.Width - osd.ActualWidth) / 2;
            osd.Top = wa.Bottom - osd.ActualHeight - 90;
            osdTimer.Stop();
            osdTimer.Start();
        }

        // ---------------------------------------------------------------- low-memory tray mode
        void FreeUiIfHidden()
        {
            if (IsVisible || !uiBuilt || settingsWin != null || guideWin != null) return;
            OutPage.ReleaseView();
            MicPage.ReleaseView();
            Content = null;
            statusText = null; statusDot = null; banner = null; bannerText = null; bannerBtn = null; pageHost = null; pageTabOut = null; pageTabMic = null;
            uiBuilt = false;
            TrimMemory();
        }

        void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        // ---------------------------------------------------------------- AutoEQ search
        const string AutoEqBase = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/";

        static string Download(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "TonelineEQ";
                return wc.DownloadString(url);
            }
        }

        static List<AutoEqEntry> LoadAutoEqIndex()
        {
            string cache = Path.Combine(Store.Dir, "autoeq-index.md");
            string text = null;
            bool fresh = File.Exists(cache) && (DateTime.Now - File.GetLastWriteTime(cache)).TotalDays < 14;
            if (fresh) text = File.ReadAllText(cache);
            else
            {
                try
                {
                    text = Download(AutoEqBase + "INDEX.md");
                    File.WriteAllText(cache, text);
                }
                catch
                {
                    if (File.Exists(cache)) text = File.ReadAllText(cache);
                    else throw;
                }
            }
            List<AutoEqEntry> list = new List<AutoEqEntry>();
            Regex rx = new Regex(@"^- \[(.*?)\]\(\./(.*)\) by (.*)$");
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                Match m = rx.Match(line);
                if (!m.Success) continue;
                list.Add(new AutoEqEntry { Name = m.Groups[1].Value, Path = m.Groups[2].Value, Source = m.Groups[3].Value });
            }
            return list;
        }

        public Window OpenAutoEq(EqPage page)
        {
            Window win = MakeChildWindow("Find my headphones (AutoEQ)", 640, 700);
            Grid g = new Grid { Margin = new Thickness(22, 18, 22, 20) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBox search = new TextBox { FontSize = 15, Padding = new Thickness(10, 6, 10, 6) };
            g.Children.Add(search);
            TextBlock info = Ui.Txt("Loading the AutoEQ list…", 12, "#8E8E93");
            info.Margin = new Thickness(2, 8, 0, 8);
            info.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(info, 1);
            g.Children.Add(info);
            ListBox list = new ListBox();
            Grid.SetRow(list, 2);
            g.Children.Add(list);
            DockPanel bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            Button load = Ui.Btn("Load correction", null);
            Ui.Primary(load);
            load.IsEnabled = false;
            DockPanel.SetDock(load, Dock.Right);
            bottom.Children.Add(load);
            TextBlock credit = Ui.Txt("Measurements and corrections by the AutoEQ project (github.com/jaakkopasanen/AutoEq). Several sources measure the same model; any of them is a good start.", 11, "#8E8E93");
            credit.TextWrapping = TextWrapping.Wrap;
            credit.Margin = new Thickness(0, 0, 14, 0);
            bottom.Children.Add(credit);
            Grid.SetRow(bottom, 3);
            g.Children.Add(bottom);

            List<AutoEqEntry> entries = null;
            DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            Action filter = delegate
            {
                list.Items.Clear();
                if (entries == null) return;
                string[] words = search.Text.ToLowerInvariant().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int shown = 0, total = 0;
                foreach (AutoEqEntry e in entries)
                {
                    string hay = (e.Name + " " + e.Source).ToLowerInvariant();
                    bool ok = true;
                    foreach (string w in words) if (hay.IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; }
                    if (!ok) continue;
                    total++;
                    if (shown >= 250) continue;
                    StackPanel sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = e.Name, TextWrapping = TextWrapping.Wrap });
                    sp.Children.Add(new TextBlock { Text = e.Source, FontSize = 11.5, Foreground = Theme.Br("#8E8E93") });
                    list.Items.Add(new ListBoxItem { Content = sp, Tag = e });
                    shown++;
                }
                info.Text = words.Length == 0
                    ? entries.Count + " headphones and IEMs. Type a brand or model, e.g. \"hd 650\"."
                    : total + " matches" + (total > shown ? " (showing the first " + shown + ")" : "") + ".";
            };
            debounce.Tick += delegate { debounce.Stop(); filter(); };
            search.TextChanged += delegate { debounce.Stop(); debounce.Start(); };
            list.SelectionChanged += delegate { load.IsEnabled = list.SelectedItem != null; };

            bool downloading = false;
            Action doLoad = delegate
            {
                ListBoxItem it = list.SelectedItem as ListBoxItem;
                AutoEqEntry e = it != null ? it.Tag as AutoEqEntry : null;
                if (e == null || downloading) return;   // a double-click while a download runs starts no second one
                downloading = true;
                load.IsEnabled = false;
                info.Text = "Downloading " + e.Name + "…";
                string last = e.Path.Substring(e.Path.LastIndexOf('/') + 1);
                string url = AutoEqBase + e.Path + "/" + last + "%20ParametricEQ.txt";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string text = null, error = null;
                    try { text = Download(url); } catch (Exception ex) { error = ex.Message; }
                    win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        downloading = false;
                        if (!win.IsVisible) return;   // closed while downloading: don't load it behind the user's back
                        Preset p = text != null ? ApoFormat.Parse(text, e.Name) : null;
                        if (p == null)
                        {
                            info.Text = "Couldn't load that one" + (error != null ? ": " + error : "") + ". Try another source for the same model.";
                            load.IsEnabled = true;
                            return;
                        }
                        page.ImportPreset(p, e.Name + " (AutoEQ, " + e.Source.Split(' ')[0] + ")");
                        ShowOsd("Loaded " + e.Name);
                        win.Close();
                    }));
                });
            };
            load.Click += delegate { doLoad(); };
            list.MouseDoubleClick += delegate { doLoad(); };

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<AutoEqEntry> loaded = null;
                string error = null;
                try { loaded = LoadAutoEqIndex(); } catch (Exception ex) { error = ex.Message; }
                win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (loaded == null) { info.Text = "Couldn't download the AutoEQ list. Check your internet connection. (" + error + ")"; return; }
                    entries = loaded;
                    filter();
                }));
            });

            UIElement content = Ui.WrapBackground(g, ChildGlass(win));
            ((FrameworkElement)content).LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale);
            win.Content = content;
            win.Show();
            search.Focus();
            return win;
        }
    }
}
