// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - self-test (TonelineEQ.exe --selftest <folder>). Exercises every page, window and feature in an
// isolated data folder and writes selftest.txt plus screenshots. Never touches Equalizer APO or startup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using SD = System.Drawing;

namespace TonelineEQ
{
    public partial class MainWindow
    {
        StreamWriter testLog;
        string testDir;
        int testPass, testFail;

        void TLog(string s)
        {
            testLog.WriteLine(s);
            testLog.Flush();
        }

        void T(string step, Action a)
        {
            try
            {
                a();
                testPass++;
                TLog("PASS  " + step);
            }
            catch (Exception ex)
            {
                testFail++;
                TLog("FAIL  " + step + "\r\n      " + ex.GetType().Name + ": " + ex.Message + "\r\n" + ex.StackTrace);
            }
        }

        static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

        static List<TItem> FindAll<TItem>(DependencyObject root) where TItem : DependencyObject
        {
            List<TItem> found = new List<TItem>();
            if (root == null) return found;
            Queue<DependencyObject> q = new Queue<DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                DependencyObject d = q.Dequeue();
                TItem hit = d as TItem;
                if (hit != null) found.Add(hit);
                int n = 0;
                try { n = VisualTreeHelper.GetChildrenCount(d); } catch { }
                for (int i = 0; i < n; i++) q.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
            return found;
        }

        // Renders an element (full size, even inside a scroll viewer) onto the theme background.
        void Snap(FrameworkElement el, string name) { SnapTo(el, Path.Combine(testDir, name), 1); }

        // Sharp 2x pictures for the GitHub page (copied to docs/images).
        void SnapDoc(FrameworkElement el, string name)
        {
            string d = Path.Combine(testDir, "docs");
            Directory.CreateDirectory(d);
            SnapTo(el, Path.Combine(d, name), 2);
        }

        // A made-up song with plain artwork, so screenshots never show what's really playing.
        static NowPlayingInfo DemoSong()
        {
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0x64, 0xD2, 0xFF), 45), null, new Rect(0, 0, 200, 200));
                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xB0, 0xFF, 0x9F, 0x0A), Color.FromArgb(0, 0xFF, 0x9F, 0x0A)), null, new Point(150, 55), 110, 110);
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            NowPlayingInfo info = new NowPlayingInfo();
            info.Title = "Midnight Drive";
            info.Artist = "Demo song";
            info.Key = "demo";
            info.Playing = true;
            using (MemoryStream ms = new MemoryStream()) { enc.Save(ms); info.Art = ms.ToArray(); }
            return info;
        }

        void SnapTo(FrameworkElement el, string path, int scale)
        {
            Check(el != null, "nothing to capture");
            el.UpdateLayout();
            double w = Math.Ceiling(el.ActualWidth), h = Math.Ceiling(el.ActualHeight);
            Check(w > 0 && h > 0, "element has no size");
            h = Math.Min(h, 6000);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.Br("#000000"), null, new Rect(0, 0, w, h));
                VisualBrush vb = new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) };
                dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap((int)w * scale, (int)h * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
        }

        // Captures a scrolling window page by page, exactly as it looks on screen.
        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WinRect r);

        // A real screen capture (shows the Windows 11 material, which window snapshots can't).
        void RealShot(Window w, string name)
        {
            WinRect r;
            if (!GetWindowRect(new WindowInteropHelper(w).Handle, out r)) return;
            using (SD.Bitmap bmp = new SD.Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)))
            {
                using (SD.Graphics g = SD.Graphics.FromImage(bmp)) g.CopyFromScreen(r.L, r.T, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(testDir, name), SD.Imaging.ImageFormat.Png);
            }
        }

        async Task SnapPages(Window w, string prefix, int maxPages)
        {
            ScrollViewer sv = FindAll<ScrollViewer>(w).FirstOrDefault(x => "content".Equals(x.Tag)) ?? FindAll<ScrollViewer>(w).FirstOrDefault();
            for (int p = 0; p < maxPages; p++)
            {
                if (sv != null) { sv.ScrollToVerticalOffset(p * Math.Max(100, sv.ViewportHeight - 40)); sv.UpdateLayout(); }
                await Task.Delay(250);
                int page = p;
                T("screenshot: " + prefix + " page " + (p + 1), delegate { Snap((FrameworkElement)w.Content, prefix + "-" + (page + 1) + ".png"); });
                if (sv == null || sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - 1) break;
            }
            if (sv != null) sv.ScrollToTop();
        }

        public async void RunSelfTest(string dir)
        {
            testDir = dir;
            NowPlaying.Demo = DemoSong();
            testLog = new StreamWriter(Path.Combine(dir, "selftest.txt"), false);
            try
            {
                TLog("Toneline EQ " + Program.Version + " self-test, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                TLog("Windows " + Environment.OSVersion.Version + ", .NET " + Environment.Version + ", 64-bit process: " + Environment.Is64BitProcess);
                TLog("Equalizer APO config folder: " + (apoDir ?? "not found") + "  writable=" + apoWritable + "  connected=" + apoHooked);
                TLog("");

                T("theme styles loaded", delegate
                {
                    Check(Application.Current.TryFindResource("Tab") is Style, "Tab style missing");
                    Check(Application.Current.TryFindResource("Pill") is Style, "Pill style missing");
                    Check(Application.Current.TryFindResource(typeof(Button)) is Style, "Button style missing");
                    Check(Application.Current.TryFindResource(typeof(CheckBox)) is Style, "switch style missing");
                });

                Show();
                await Task.Delay(1200);
                T("main window builds", delegate { Check(uiBuilt && Content != null, "UI not built"); });
                T("screenshot: headphones page", delegate { Snap((FrameworkElement)Content, "01-headphones.png"); });
                TLog("      Windows build " + Glass.WindowsBuild + ", Mica supported: " + Glass.Supported + ", translucent window: " + GlassOn + ", now playing compiled: " + NowPlaying.Available);
                Topmost = true;
                Activate();
                await Task.Delay(900);
                T("real screen capture", delegate { RealShot(this, "00-real-window.png"); });
                Topmost = false;
                T("now playing", delegate { TLog("      " + OutPage.DebugNowPlaying()); });
                T("toast", delegate { Say("Saved “Test”"); });
                await Task.Delay(300);
                T("screenshot: toast", delegate { Snap((FrameworkElement)Content, "00b-toast.png"); });

                T("devices and Equalizer APO detection", delegate
                {
                    RefreshDeviceLists();
                    TLog("      default output: " + (DefaultDeviceName(PageKind.Output) ?? "?") + "   default mic: " + (DefaultDeviceName(PageKind.Mic) ?? "?"));
                    foreach (AudioDevice d in outDevices) TLog("      out: " + d.Name + "  " + d.Guid + "  APO=" + AudioDevices.HasApo(d.Guid, 0));
                    foreach (AudioDevice d in micDevices) TLog("      mic: " + d.Name + "  " + d.Guid + "  APO=" + AudioDevices.HasApo(d.Guid, 1));
                    Check(outDevices.Count > 0, "no playback devices found");
                });

                T("Equalizer APO config text", delegate
                {
                    string cfg = BuildConfig();
                    File.WriteAllText(Path.Combine(dir, "config-preview.txt"), cfg);
                    Check(cfg.Contains("Preamp:"), "no preamp line");
                    Check(cfg.Contains("Device:"), "no Device line");
                });

                T("format round trip", delegate
                {
                    foreach (Preset p in OutPage.AllPresets().Concat(MicPage.AllPresets()))
                    {
                        Preset back = ApoFormat.Parse(ApoFormat.ToText(p, null), p.Name);
                        Check(back != null && back.Bands.Count == p.Bands.Count && Math.Abs(back.Preamp - p.Preamp) < 0.01, "round trip failed for " + p.Name);
                    }
                });

                OutPage.VizOn = true;
                UpdateVisualizers();
                await Task.Delay(2500);
                T("headphone visualizer (loopback)", delegate { string v = OutPage.DebugViz(); TLog("      " + v); Check(v.Contains("running=True"), "not running"); Check(v.Contains("error=none"), "capture error"); });
                T("screenshot: headphones with visualizer", delegate { Snap((FrameworkElement)Content, "02-headphones-visualizer.png"); });

                T("built-in presets are valid (no 'Your gear')", delegate
                {
                    foreach (PageKind pk in new[] { PageKind.Output, PageKind.Mic })
                    {
                        List<Preset> bl = BuiltIns.All(pk);
                        Check(bl.Count >= 15, pk + ": only " + bl.Count + " presets");
                        Check(bl.Select(x => x.Name).Distinct().Count() == bl.Count, pk + ": duplicate names");
                        foreach (Preset bp in bl)
                        {
                            Check(bp.Bands.Count > 0 && bp.Bands.Count <= 20, bp.Name + ": " + bp.Bands.Count + " bands");
                            Check(bp.Preamp <= 0, bp.Name + ": positive preamp");
                            Check(bp.Group != "gear" && bp.Group != "micgear", bp.Name + " is still in Your gear");
                            Check(!string.IsNullOrEmpty(bp.Note), bp.Name + " has no description");
                            Check(BuiltIns.GroupTitle(bp.Group) != "Saved", bp.Name + ": unknown group " + bp.Group);
                            // A preset must not push the signal over 0 dB by more than 1 dB (its preamp covers the boost).
                            double peakGain = Dsp.PeakGain(bp.Bands);
                            Check(peakGain + bp.Preamp <= 1.0, bp.Name + ": boost " + peakGain.ToString("0.0") + " dB vs preamp " + bp.Preamp);
                        }
                        TLog("      " + pk + ": " + bl.Count + " presets in " + bl.Select(x => x.Group).Distinct().Count() + " groups");
                    }
                    Check(FindAll<PresetSpark>(this).Any() || !SidebarOpen, "no preset mini-curves in the list");
                });

                T("text on accent buttons is readable for every accent", delegate
                {
                    foreach (string accentHex in Theme.AccentSwatches.Concat(Theme.Presets.Select(x => x.Accent)))
                    {
                        Color fillC = Theme.Parse(accentHex);
                        double cr = Theme.Contrast(Theme.OnColor(fillC), fillC);
                        Check(cr >= 3.0, accentHex + ": contrast only " + cr.ToString("0.0"));
                    }
                });

                T("a share code can't smuggle Equalizer APO commands", delegate
                {
                    Preset evil = BuiltIns.All(PageKind.Output)[2].Clone();
                    evil.Name = "Nice EQ\rInclude: C:\\evil.txt\u2028VSTPlugin: Library C:\\evil.dll";
                    byte[] raw = System.Text.Encoding.UTF8.GetBytes(evil.Name + "\n" + ApoFormat.ToText(evil, null) + "Include: C:\\evil.txt\r\nVSTPlugin: Library C:\\evil.dll\r\n");
                    string code;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true)) ds.Write(raw, 0, raw.Length);
                        code = "EasyEQ:" + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    }
                    Preset got = ShareCode.Decode(code);
                    Check(got != null, "code not read");
                    Check(got.Name.IndexOfAny(new[] { '\r', '\n', '\u2028' }) < 0, "line break kept in the name");
                    OutPage.ImportPreset(got, got.Name);
                    string cfg = BuildConfig();
                    foreach (string line in cfg.Split(new[] { "\r\n", "\n", "\r", "\u2028" }, StringSplitOptions.None))
                    {
                        string t = line.TrimStart();
                        Check(!t.StartsWith("Include", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("VSTPlugin", StringComparison.OrdinalIgnoreCase), "injected line in config: " + t);
                    }
                    OutPage.LoadPresetByName("Flat (10 bands)");
                });
                T("a preset from outside can't set a dangerous volume", delegate
                {
                    Preset loud = ApoFormat.Parse("Preamp: 96 dB\r\nFilter 1: ON PK Fc 1000 Hz Gain 99 dB Q 1\r\n", "Loud");
                    Check(loud != null && loud.Bands.Count == 1, "not read");
                    Check(loud.Preamp <= ApoFormat.MaxPreamp, "preamp " + loud.Preamp);
                    Check(loud.Bands[0].Gain <= 24, "gain " + loud.Bands[0].Gain);
                    Preset quiet = ApoFormat.Parse("Preamp: -500 dB\r\nFilter 1: ON PK Fc 1000 Hz Gain 1 dB Q 1\r\n", "Quiet");
                    Check(quiet != null && quiet.Preamp >= ApoFormat.MinPreamp, "preamp too low");
                    Check(ApoFormat.Clamp(double.NaN, -24, 24) == 0, "'not a number' passed through");
                });
                T("huge or endless pasted text is cut off", delegate
                {
                    System.Text.StringBuilder many = new System.Text.StringBuilder("Preamp: -3 dB\r\n");
                    for (int i = 0; i < 5000; i++) many.Append("Filter ").Append(i).Append(": ON PK Fc 1000 Hz Gain 1 dB Q 1\r\n");
                    Preset big = ApoFormat.Parse(many.ToString(), "Big");
                    Check(big != null && big.Bands.Count <= ApoFormat.MaxParsedBands, "too many bands read");
                    Preset longLine = ApoFormat.Parse("Preamp: -3 dB\r\nFilter 1: ON PK Fc " + new string('9', 100000) + " Hz Gain 1 dB Q 1\r\n", "Long");
                    Check(longLine != null && longLine.Bands.Count == 0, "an endless line was read");
                });
                T("a share code can't unpack into gigabytes", delegate
                {
                    byte[] filler = new byte[8 * 1024 * 1024];
                    for (int i = 0; i < filler.Length; i++) filler[i] = (byte)'A';
                    string bomb;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true)) ds.Write(filler, 0, filler.Length);
                        bomb = "Toneline:" + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    }
                    TLog("      a " + bomb.Length + "-character code that unpacks to 8 MB");
                    System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                    Check(ShareCode.Decode(bomb) == null, "oversized code accepted");
                    Check(clock.ElapsedMilliseconds < 2000, "took " + clock.ElapsedMilliseconds + " ms");
                    Check(ShareCode.Decode("Toneline:" + new string('A', ShareCode.MaxCodeChars + 4)) == null, "overlong code accepted");
                });
                T("downloads only come from the AutoEQ project", delegate
                {
                    foreach (string bad in new[] { "http://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/INDEX.md", "https://example.com/x.txt", "https://raw.githubusercontent.com.evil.example/x", "file:///C:/Windows/win.ini", "C:\\Windows\\win.ini" })
                    {
                        bool blocked = false;
                        try { Download(bad, 1000); } catch (InvalidOperationException) { blocked = true; }
                        Check(blocked, "not blocked: " + bad);
                    }
                    Check(AutoEqPathOk("oratory1990/over-ear/Sennheiser%20HD%20650"), "a normal AutoEQ path was refused");
                    foreach (string badPath in new[] { "../../../../evil/repo/main/x", "/abs", "a//b", "a\\b", "x?y", "https://evil.example/a", "" })
                        Check(!AutoEqPathOk(badPath), "path accepted: " + badPath);
                });
                T("Windows tools are started by full path", delegate
                {
                    foreach (string tool in new[] { "explorer.exe", "control.exe", "cmd.exe" })
                    {
                        string full = Sys.Tool(tool);
                        Check(Path.IsPathRooted(full) && File.Exists(full), tool + " -> " + full);
                    }
                });
                T("plugin settings can't add a second command", delegate
                {
                    string dll = Path.Combine(dir, "Gate.dll");
                    File.WriteAllText(dll, "");
                    string line = new FxItem { Kind = "vst", Path = dll, Params = "ChunkData \"abc\"\r\nInclude: C:\\evil.txt\u2028Copy: L=R" }.ToApo();
                    Check(line.StartsWith("VSTPlugin:") && line.IndexOfAny(new[] { '\r', '\n', '\u2028' }) < 0, "line break kept: " + line);
                    Check(new FxItem { Kind = "vst", Path = dll + "\r\nInclude: evil.txt" }.ToApo() == "", "a path with a line break was accepted");
                    string gainLine = new FxItem { Kind = "gain", Value = 500 }.ToApo();
                    Check(gainLine == "Preamp: 24 dB", "gain effect not limited: " + gainLine);
                });
                T("mic test keeps the recording in memory", delegate
                {
                    float[] tone = new float[4800];
                    for (int i = 0; i < tone.Length; i++) tone[i] = (float)(0.2 * Math.Sin(i * 0.05));
                    using (MemoryStream ms = new MemoryStream())
                    {
                        EqPage.WriteWav(ms, tone, 48000);
                        byte[] wav = ms.ToArray();
                        Check(wav.Length == 44 + tone.Length * 2, "wav is " + wav.Length + " bytes");
                        Check(wav[0] == 'R' && wav[1] == 'I' && wav[2] == 'F' && wav[3] == 'F', "no RIFF header");
                        Check(ms.CanWrite, "the stream was closed");
                    }
                    Check(!File.Exists(Path.Combine(Path.GetTempPath(), "TonelineEQ-mic-test.wav")), "a mic recording is on the disk");
                });
                T("arrow keys move the selected band", delegate
                {
                    Check(OutPage.DebugLoad("Bass boost"), "Bass boost missing");
                    double g0 = OutPage.Current.Bands[0].Gain, f0 = OutPage.Current.Bands[0].Freq;
                    Band nb = OutPage.DebugNudge(Key.Up);
                    Check(nb != null && Math.Abs(nb.Gain - (g0 + 0.5)) < 0.01, "gain went from " + g0 + " to " + (nb == null ? "nothing" : nb.Gain.ToString()));
                    OutPage.DebugNudge(Key.Right);
                    Check(OutPage.Current.Bands[0].Freq > f0, "frequency didn't move from " + f0);
                    OutPage.Undo();
                    OutPage.LoadPresetByName("Flat (10 bands)");
                });

                T("presets load and step", delegate
                {
                    Check(OutPage.LoadPresetByName("Bass boost"), "Bass boost not found");
                    Check(OutPage.Current.Name == "Bass boost", "wrong preset loaded");
                    string a = OutPage.NextPreset(1), b = OutPage.NextPreset(-1);
                    Check(b == "Bass boost", "next/previous didn't return: " + a + " / " + b);
                });

                T("undo and redo", delegate
                {
                    OutPage.LoadPresetByName("Flat (10 bands)");
                    double before = OutPage.Current.Bands[0].Gain;
                    OutPage.DebugEditFirstBand(5);
                    Check(OutPage.Current.Bands[0].Gain == 5, "edit didn't apply");
                    OutPage.Undo();
                    Check(OutPage.Current.Bands[0].Gain == before, "undo failed: " + OutPage.Current.Bands[0].Gain);
                    OutPage.Redo();
                    Check(OutPage.Current.Bands[0].Gain == 5, "redo failed");
                    OutPage.Undo();
                });

                T("switch flips (animated)", delegate
                {
                    CheckBox sw = FindAll<CheckBox>(OutPage.View).FirstOrDefault();
                    Check(sw != null, "no switch found");
                    bool was = sw.IsChecked == true;
                    sw.IsChecked = !was;
                    sw.IsChecked = was;
                });
                ContextMenu menu = null;
                T("preset menu opens", delegate { menu = OutPage.DebugMoreMenu(); Check(menu != null && menu.IsOpen && menu.Items.Count >= 5, "menu not open"); });
                await Task.Delay(500);
                T("screenshot: preset menu", delegate { if (menu != null) { Snap(menu, "16-preset-menu.png"); menu.IsOpen = false; } });
                T("bundled Apple-style font", delegate
                {
                    GlyphTypeface gt;
                    Check(new Typeface(Theme.Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out gt), "font not found");
                    TLog("      font: " + gt.FamilyNames.Values.FirstOrDefault() + "  " + gt.FontUri);
                    string fu = gt.FontUri.ToString().ToUpperInvariant();
                    Check(fu.Contains("INTER") || fu.Contains("SF-PRO") || fu.Contains("SFPRO"), "fell back to " + gt.FontUri);
                });
                T("BW Oct filters import", delegate
                {
                    Preset bwp = ApoFormat.Parse("Filter: ON PK Fc 1000 Hz Gain 3 dB BW Oct 1", "bw");
                    Check(bwp != null && Math.Abs(bwp.Bands[0].Q - 1.414) < 0.01, "Q from BW wrong");
                });
                T("share codes round trip", delegate
                {
                    foreach (Preset p in OutPage.AllPresets().Concat(MicPage.AllPresets()))
                    {
                        string code = ShareCode.Encode(p);
                        Preset back = ShareCode.Decode("Try my EQ: " + code + " (made in Toneline EQ)");
                        Check(back != null && back.Name == p.Name && back.Bands.Count == p.Bands.Count && Math.Abs(back.Preamp - p.Preamp) < 0.01, "share code failed for " + p.Name);
                    }
                    TLog("      example: " + OutPage.DebugShareCode());
                    Check(ShareCode.Decode("no code here") == null && ShareCode.Decode("EasyEQ:!!!!!!!!!!") == null, "bad codes accepted");
                });
                T("rename: new share codes start with Toneline:", delegate
                {
                    string code = ShareCode.Encode(OutPage.AllPresets().First());
                    Check(code.StartsWith("Toneline:"), "code starts " + code.Substring(0, Math.Min(12, code.Length)));
                    Preset viaOld = ShareCode.Decode("EasyEQ:" + code.Substring("Toneline:".Length));
                    Check(viaOld != null, "old EasyEQ: prefix no longer decodes");
                });
                T("rename: Equalizer APO moves from EasyEQ.txt to TonelineEQ.txt", delegate
                {
                    string d = Path.Combine(Path.GetTempPath(), "TonelineEQ-apo-test-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(d);
                    try
                    {
                        File.WriteAllText(Path.Combine(d, "config.txt"), "Device: all\r\n# Managed by EasyEQ.\r\nInclude: EasyEQ.txt\r\n");
                        File.WriteAllText(Path.Combine(d, "EasyEQ.txt"), "Preamp: -3 dB\r\nFilter: ON PK Fc 1000 Hz Gain 2 dB Q 1\r\n");
                        Check(!Apo.IsHooked(d), "old include counted as hooked");
                        Check(Apo.MigrateLegacy(d), "migration did nothing");
                        string cfg = File.ReadAllText(Path.Combine(d, "config.txt"));
                        Check(Apo.IsHooked(d), "not hooked after migration");
                        Check(cfg.IndexOf("EasyEQ.txt", StringComparison.OrdinalIgnoreCase) < 0, "config.txt still includes EasyEQ.txt");
                        Check(cfg.Contains("Device: all"), "other config lines lost");
                        Check(File.ReadAllText(Path.Combine(d, "TonelineEQ.txt")).Contains("Gain 2 dB"), "EQ not carried over");
                        Check(!File.ReadAllText(Path.Combine(d, "EasyEQ.txt")).Contains("Filter:"), "old file still has filters (double EQ risk)");
                        Check(!Apo.MigrateLegacy(d), "migration ran twice");
                    }
                    finally { try { Directory.Delete(d, true); } catch { } }
                });
                T("rename: settings copy over from the EasyEQ folder", delegate
                {
                    string root = Path.Combine(Path.GetTempPath(), "TonelineEQ-store-test-" + Guid.NewGuid().ToString("N"));
                    string from = Path.Combine(root, "EasyEQ"), to = Path.Combine(root, "TonelineEQ");
                    Directory.CreateDirectory(Path.Combine(from, "presets"));
                    try
                    {
                        File.WriteAllText(Path.Combine(Path.Combine(from, "presets"), "Mine.txt"), "Preamp: 0 dB\r\n");
                        File.WriteAllText(Path.Combine(from, "settings.txt"), "theme.image=" + Path.Combine(from, "bg.png") + "\r\n");
                        File.WriteAllText(Path.Combine(from, "bg.png"), "x");
                        Check(Store.MigrateFolder(from, to), "nothing copied");
                        Check(File.Exists(Path.Combine(Path.Combine(to, "presets"), "Mine.txt")), "preset not copied");
                        Check(File.ReadAllText(Path.Combine(to, "settings.txt")).Contains(Path.Combine(to, "bg.png")), "background path not updated");
                        Check(Directory.Exists(from), "old folder should stay as a backup");
                        Check(!Store.MigrateFolder(from, to), "copied over existing settings");
                    }
                    finally { try { Directory.Delete(root, true); } catch { } }
                });
                int recorded = await MicPage.DebugRecord();
                T("mic test recording", delegate { TLog("      recorded " + recorded + " samples"); Check(recorded > 0, "nothing recorded"); });
                T("presets sidebar opens", delegate { if (!SidebarOpen) ToggleSidebar(); });
                await Task.Delay(700);
                T("screenshot: presets sidebar open", delegate { Snap((FrameworkElement)Content, "17-sidebar-open.png"); });
                T("docs: V-shape preset loads", delegate { Check(OutPage.DebugLoad("V-shape"), "V-shape missing"); EqPage.DemoSound = true; });
                await Task.Delay(1500);
                T("docs: main picture", delegate
                {
                    if (NowPlaying.Available) Check(OutPage.DebugNowPlaying() == "Midnight Drive - Demo song", "real song showing: " + OutPage.DebugNowPlaying());
                    SnapDoc((FrameworkElement)Content, "main.png");
                });
                EqPage.DemoSound = false;
                T("presets sidebar closes", delegate { ToggleSidebar(); Check(!SidebarOpen, "still open"); });
                await Task.Delay(500);
                T("simple EQ (Low / Mid / High)", delegate
                {
                    double g = OutPage.DebugSimple(4);
                    Check(Math.Abs(g - 4) < 0.01, "low gain is " + g);
                    Check(OutPage.DebugSimpleLabels, "graph doesn't show LOW / MID / HIGH");
                });
                await Task.Delay(700);
                EqPage.DemoSound = true;
                await Task.Delay(1200);
                T("screenshot: simple EQ", delegate { Snap((FrameworkElement)Content, "18-simple.png"); SnapDoc((FrameworkElement)Content, "simple.png"); });
                EqPage.DemoSound = false;
                T("simple quick picks", delegate
                {
                    Button qp = FindAll<Button>(this).FirstOrDefault(x => "V-shape".Equals(x.Content));
                    Check(qp != null, "no Quick picks on the Simple tab");
                    qp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    List<Band> qb = OutPage.Current.Bands;
                    Check(qb[0].Gain == 4 && qb[1].Gain == -2 && qb[2].Gain == 4, "gains " + qb[0].Gain + " / " + qb[1].Gain + " / " + qb[2].Gain);
                    Check(OutPage.Current.Preamp <= -4, "preamp didn't follow: " + OutPage.Current.Preamp);
                });
                T("level-matched EQ off", delegate
                {
                    OutPage.LoadPresetByName("Bass boost");
                    OutPage.SetBypass(true);
                    string body = OutPage.ApoBody();
                    Check(body.Contains("Preamp: -6 dB") && !body.Contains("Filter"), "bypass body wrong: " + body);
                    OutPage.SetBypass(false);
                    Check(OutPage.ApoBody().Contains("Filter"), "EQ didn't come back on");
                });

                T("effects chain", delegate
                {
                    List<FxItem> keep = new List<FxItem>(OutPage.Effects);
                    string fakeWav = Path.Combine(dir, "room.wav"), fakeVst = Path.Combine(dir, "Limiter.dll");
                    File.WriteAllText(fakeWav, "");
                    File.WriteAllText(fakeVst, "");
                    OutPage.Effects.Add(new FxItem { Kind = "gain", Value = -3 });
                    OutPage.Effects.Add(new FxItem { Kind = "balance", Value = 20 });
                    OutPage.Effects.Add(new FxItem { Kind = "mono" });
                    OutPage.Effects.Add(new FxItem { Kind = "swap", Enabled = false });
                    OutPage.Effects.Add(new FxItem { Kind = "conv", Path = fakeWav });
                    OutPage.Effects.Add(new FxItem { Kind = "vst", Path = fakeVst, BeforeEq = true });
                    OutPage.Effects.Add(new FxItem { Kind = "vst", Path = @"C:\missing\Gone.dll" });
                    OutPage.Effects.Add(new FxItem { Kind = "custom", Params = "Delay: 0 ms" });
                    string body = OutPage.ApoBody();
                    File.WriteAllText(Path.Combine(dir, "effects-preview.txt"), body);
                    Check(body.IndexOf("VSTPlugin") >= 0 && body.IndexOf("VSTPlugin") < body.IndexOf("Filter"), "before-EQ effect not first");
                    Check(body.Contains("Convolution:"), "impulse response missing");
                    Check(!body.Contains("Gone.dll"), "missing plugin file was written to the config");
                    Check(body.Contains("Copy: L=0.5*L+0.5*R"), "mono missing");
                    Check(!body.Contains("Copy: L=R R=L"), "disabled effect was applied");
                    Check(body.Contains("Channel: L"), "balance missing");
                    OutPage.DebugShowEffects(true);
                    Snap((FrameworkElement)Content, "03-effects.png");
                    OutPage.Effects.Clear();
                    OutPage.Effects.AddRange(keep);
                    OutPage.DebugShowEffects(false);
                });

                ShowPage(MicPage);
                MicPage.VizOn = true;
                UpdateVisualizers();
                await Task.Delay(2000);
                T("microphone page", delegate { Check(MicPage.View != null && MicPage.View.Visibility == Visibility.Visible, "mic page not visible"); });
                T("page switch shows Microphone", delegate
                {
                    RadioButton mic = FindAll<RadioButton>(this).FirstOrDefault(r => (r.Content as string) == "Microphone");
                    Check(mic != null && mic.IsChecked == true, "the Headphones / Microphone switch didn't follow the page");
                });
                T("mic visualizer", delegate { TLog("      " + MicPage.DebugViz()); });
                T("screenshot: microphone page", delegate { Snap((FrameworkElement)Content, "04-microphone.png"); });
                T("docs: Warm broadcast mic preset loads", delegate { Check(MicPage.DebugLoad("Warm broadcast"), "Warm broadcast missing"); EqPage.DemoSound = true; });
                await Task.Delay(1500);
                T("docs: microphone picture", delegate { SnapDoc((FrameworkElement)Content, "microphone.png"); });
                EqPage.DemoSound = false;
                ShowPage(OutPage);
                await Task.Delay(300);

                T("open settings", delegate { OpenSettings(); Check(settingsWin != null, "settings window missing"); });
                await Task.Delay(1000);
                T("legal notice in settings, no Apple names on screen", delegate
                {
                    string all = "";
                    foreach (string legalPage in SettingsPageNames())
                    {
                        ShowSettingsPage(legalPage);
                        settingsWin.UpdateLayout();
                        foreach (TextBlock tb in FindAll<TextBlock>(settingsWin)) all += tb.Text + "\n";
                    }
                    foreach (TextBlock tb in FindAll<TextBlock>(this)) all += tb.Text + "\n";
                    Check(all.Contains("ABSOLUTELY NO WARRANTY"), "no-warranty notice missing");
                    Check(all.Contains("GNU General Public License"), "license notice missing");
                    all = all.Replace(LegalText, "");   // the not-affiliated notice may name them
                    foreach (string bad in new[] { "Apple", "AirPods", "macOS", "iPhone" })
                    {
                        int at = all.IndexOf(bad, StringComparison.Ordinal);
                        Check(at < 0, "'" + bad + "' is shown to users: ..." + (at < 0 ? "" : all.Substring(Math.Max(0, at - 60), Math.Min(120, all.Length - Math.Max(0, at - 60))).Replace("\n", " | ")) + "...");
                    }
                    ShowSettingsPage("General");
                });
                List<string> spList = SettingsPageNames();
                T("settings categories", delegate { TLog("      " + string.Join(", ", spList)); Check(spList.Count >= 5, "only " + spList.Count + " categories"); });
                int spIdx = 0;
                foreach (string spEach in spList)
                {
                    string spName = spEach;
                    spIdx++;
                    T("settings page: " + spName, delegate { ShowSettingsPage(spName); });
                    await Task.Delay(400);
                    await SnapPages(settingsWin, "05-settings-" + spIdx, 2);
                }
                ShowSettingsPage("General");

                string prevBanner = bannerMsg, prevBannerBtn = bannerBtnText;
                Action prevBannerAction = bannerAction;
                T("motion: banner slides in", delegate { ShowBanner("Test banner", "OK", null); Check(banner.Visibility == Visibility.Visible, "banner not shown"); });
                await Task.Delay(350);
                T("motion: banner fades out and collapses", delegate { HideBanner(); });
                await Task.Delay(400);
                T("motion: banner gone", delegate { Check(banner.Visibility == Visibility.Collapsed, "banner still visible"); Check(banner.Opacity > 0.99, "banner left transparent"); });
                if (prevBanner != null) ShowBanner(prevBanner, prevBannerBtn, prevBannerAction);

                T("open guide (headphones)", delegate { OpenGuide(PageKind.Output); Check(guideWin != null, "guide missing"); });
                await Task.Delay(800);
                await SnapPages(guideWin, "06-guide-headphones", 6);
                T("guide (microphone)", delegate { OpenGuide(PageKind.Mic); });
                await Task.Delay(800);
                await SnapPages(guideWin, "07-guide-mic", 6);
                // Motion: the mouse wheel glides instead of jumping, and ends exactly where it was aimed.
                ScrollViewer wheelSv = FindAll<ScrollViewer>(guideWin).FirstOrDefault(x => "content".Equals(x.Tag)) ?? FindAll<ScrollViewer>(guideWin).OrderByDescending(x => x.ScrollableHeight).FirstOrDefault();
                double wheelStart = 0, wheelMid = 0;
                T("motion: smooth wheel scroll starts", delegate
                {
                    Check(wheelSv != null, "guide has no scroll area");
                    Check(wheelSv.ScrollableHeight > 0, "guide doesn't scroll: extent " + wheelSv.ExtentHeight + ", viewport " + wheelSv.ViewportHeight);
                    wheelSv.ScrollToTop();
                    wheelSv.UpdateLayout();
                    wheelStart = wheelSv.VerticalOffset;
                    MouseWheelEventArgs we = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120);
                    we.RoutedEvent = UIElement.PreviewMouseWheelEvent;
                    wheelSv.RaiseEvent(we);
                    Check(we.Handled, "wheel wasn't taken by smooth scrolling (animations off in Windows?)");
                    Check(Motion.ActiveScrolls > 0, "no scroll animation running");
                });
                await Task.Delay(40);
                T("motion: scroll is gliding", delegate { wheelMid = wheelSv.VerticalOffset; Check(wheelMid > wheelStart, "didn't move"); });
                await Task.Delay(700);
                T("motion: scroll settles", delegate
                {
                    Check(Motion.ActiveScrolls == 0, "still animating");
                    Check(wheelSv.VerticalOffset > wheelMid, "stopped early at " + wheelMid);
                    TLog("      offset " + wheelStart + " -> " + wheelMid.ToString("0.0") + " -> " + wheelSv.VerticalOffset.ToString("0.0"));
                    wheelSv.ScrollToTop();
                });

                T("close guide", delegate { guideWin.Close(); });

                T("theme presets", delegate
                {
                    foreach (ThemePreset tp in Theme.Presets)
                    {
                        Theme.Bg = Theme.Parse(tp.Bg); Theme.Text = Theme.Parse(tp.Text); Theme.Accent = Theme.Parse(tp.Accent);
                        ApplyTheme();
                    }
                });
                await Task.Delay(800);
                EqPage.DemoSound = true;
                await Task.Delay(1200);
                T("screenshot: light theme", delegate { Snap((FrameworkElement)Content, "08-theme-light.png"); SnapDoc((FrameworkElement)Content, "light.png"); });
                EqPage.DemoSound = false;
                // Light mode tour: every main surface, so light-mode mistakes show up in the screenshots.
                T("light: sidebar", delegate { if (!SidebarOpen) ToggleSidebar(); });
                await Task.Delay(700);
                T("screenshot: light sidebar", delegate { Snap((FrameworkElement)Content, "08c-light-sidebar.png"); });
                T("light: simple tab", delegate { ToggleSidebar(); OutPage.DebugSimple(4); });
                await Task.Delay(700);
                T("screenshot: light simple", delegate { Snap((FrameworkElement)Content, "08d-light-simple.png"); });
                ContextMenu lightMenu = null;
                T("light: preset menu", delegate { lightMenu = OutPage.DebugMoreMenu(); });
                await Task.Delay(500);
                T("screenshot: light menu", delegate { if (lightMenu != null) { Snap(lightMenu, "08e-light-menu.png"); lightMenu.IsOpen = false; } });
                T("light: microphone page", delegate { ShowPage(MicPage); });
                await Task.Delay(700);
                T("screenshot: light mic", delegate { Snap((FrameworkElement)Content, "08f-light-mic.png"); ShowPage(OutPage); });
                T("light: settings", delegate { ShowSettingsPage("Appearance"); });
                await Task.Delay(500);
                await SnapPages(settingsWin, "08g-light-settings", 3);
                T("light: guide", delegate { OpenGuide(PageKind.Output); });
                await Task.Delay(800);
                T("screenshot: light guide", delegate { Snap((FrameworkElement)guideWin.Content, "08h-light-guide.png"); guideWin.Close(); });
                T("light: OSD", delegate { ShowOsd("Light mode"); });
                await Task.Delay(400);
                T("screenshot: light OSD", delegate { Snap((FrameworkElement)osd.Content, "08i-light-osd.png"); });
                T("Apple theme with a selected band", delegate
                {
                    Theme.ResetAll();
                    ApplyTheme();
                    OutPage.DebugSelectBand(0);
                });
                await Task.Delay(1000);
                T("screenshot: Apple theme, band selected", delegate { Snap((FrameworkElement)Content, "08b-apple-selected.png"); });
                T("font, size and background picture", delegate
                {
                    Theme.Bg = Theme.Parse("#120E10"); Theme.Text = Theme.Parse("#EDE3E6"); Theme.Accent = Theme.Parse("#E3A3A8");
                    string pic = Path.Combine(dir, "test-background.png");
                    RenderTargetBitmap rb = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
                    DrawingVisual dv = new DrawingVisual();
                    using (DrawingContext dc = dv.RenderOpen())
                        dc.DrawRectangle(new LinearGradientBrush(Colors.DarkSlateBlue, Colors.DarkOrange, 30), null, new Rect(0, 0, 320, 180));
                    rb.Render(dv);
                    PngBitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rb));
                    using (FileStream fs = File.Create(pic)) enc.Save(fs);
                    Theme.ImagePath = pic;
                    Theme.FontName = "Bahnschrift";
                    Theme.Scale = 1.1;
                    Theme.PanelOpacity = 0.6;
                    ApplyTheme();
                });
                await Task.Delay(1000);
                T("screenshot: custom look", delegate { Snap((FrameworkElement)Content, "09-custom-look.png"); });
                T("settings: appearance page", delegate { ShowSettingsPage("Appearance"); });
                await Task.Delay(400);
                await SnapPages(settingsWin, "10-settings-custom", 2);
                T("color wheel math", delegate
                {
                    foreach (string hx in Theme.AccentSwatches.Concat(Theme.BgSwatches).Concat(Theme.TextSwatches))
                    {
                        Color c0 = Theme.Parse(hx);
                        double hh, ss, vv;
                        ColorWheel.ToHsv(c0, out hh, out ss, out vv);
                        Color c1 = ColorWheel.FromHsv(hh, ss, vv);
                        Check(Math.Abs(c0.R - c1.R) <= 1 && Math.Abs(c0.G - c1.G) <= 1 && Math.Abs(c0.B - c1.B) <= 1, hx + " came back as " + Theme.Hex(c1));
                    }
                    Check(Theme.Hex(new ColorWheel(Theme.Parse("#FF375F")).Value) == "#FF375F", "wheel doesn't start on its color");
                });
                T("color wheel opens in Appearance", delegate
                {
                    Theme.ResetAll(); ApplyTheme();
                    openWheel = "accent";
                    RebuildSettings();
                    ShowSettingsPage("Appearance");
                    settingsWin.UpdateLayout();
                    Check(FindAll<ColorWheel>(settingsWin).Count() == 1, FindAll<ColorWheel>(settingsWin).Count() + " wheels open");
                });
                await Task.Delay(500);
                T("docs: appearance picture", delegate { SnapDoc((FrameworkElement)settingsWin.Content, "appearance.png"); });
                await SnapPages(settingsWin, "10b-color-wheel", 4);
                T("glow color and live audio colors", delegate
                {
                    Theme.GlowColor = Theme.Parse("#BF5AF2");
                    Theme.Glow = 1.8;
                    Color[] mine = { Theme.Parse("#1E3A8A"), Theme.Parse("#7C3AED"), Theme.Parse("#EC4899"), Theme.Parse("#F97316"), Theme.Parse("#FDE047") };
                    Theme.VizStops = mine;
                    VizColor = 3;
                    OptionsChanged();
                    openWheel = "viz";
                    ApplyTheme();
                    Check(Theme.Hex(LevelColors.Stops(3)[4]) == "#FDE047", "custom peak color not used");
                    Dictionary<string, string> saved = new Dictionary<string, string>();
                    Theme.Save(saved);
                    Theme.VizStops = null; Theme.GlowColor = null;
                    Theme.Load(saved);
                    Check(Theme.VizStops != null && Theme.Hex(Theme.VizStops[0]) == "#1E3A8A", "live audio colors not saved");
                    Check(Theme.GlowColor.HasValue && Theme.Hex(Theme.GlowColor.Value) == "#BF5AF2", "glow color not saved");
                    Check(!Theme.IsDefaultLook, "custom look reported as default");
                });
                await Task.Delay(900);
                T("screenshot: glow color and custom live audio colors", delegate { Snap((FrameworkElement)Content, "09b-glow-and-audio-colors.png"); });
                await SnapPages(settingsWin, "10c-audio-colors", 4);
                T("light background fixes white text", delegate
                {
                    Theme.Bg = Theme.Parse("#F2F2F7"); Theme.Text = Theme.Parse("#FFFFFF");
                    Theme.FixTextContrast();
                    Check(Theme.Contrast(Theme.Text, Theme.Bg) >= 4.5, "text still unreadable: " + Theme.Hex(Theme.Text));
                });
                T("Default theme is how Toneline EQ comes", delegate
                {
                    Theme.ResetAll(); VizColor = 0; openWheel = null; OptionsChanged(); ApplyTheme();
                    Check(Theme.IsDefaultLook, "not the default look after Default");
                });
                T("reset appearance", delegate { Theme.ResetAll(); ApplyTheme(); Check(Theme.Hex(Theme.Accent) == "#0A84FF", "accent not reset"); });
                T("close settings", delegate { settingsWin.Close(); });
                await Task.Delay(300);

                T("per-device link", delegate
                {
                    string before = LinkedPreset(PageKind.Output);
                    LinkPreset(OutPage);
                    string after = LinkedPreset(PageKind.Output);
                    TLog("      linked: " + (after ?? "none"));
                    if (DefaultDeviceName(PageKind.Output) != null) Check(after == OutPage.Current.Name || before == OutPage.Current.Name, "link not stored");
                    if (after != before) LinkPreset(OutPage);
                });

                T("on-screen message", delegate { ShowOsd("Toneline EQ self-test"); Check(osd != null && osd.IsVisible, "OSD not shown"); });
                await Task.Delay(400);
                T("screenshot: on-screen message", delegate { Snap((FrameworkElement)osd.Content, "11-osd.png"); });

                Window autoEq = null;
                T("open AutoEQ search", delegate { autoEq = OpenAutoEq(OutPage); });
                if (autoEq != null)
                {
                    TextBox search = FindAll<TextBox>(autoEq).FirstOrDefault();
                    ListBox list = FindAll<ListBox>(autoEq).FirstOrDefault();
                    for (int i = 0; i < 40 && (list == null || list.Items.Count == 0); i++) await Task.Delay(500);
                    T("AutoEQ list downloaded", delegate { Check(list != null && list.Items.Count > 0, "list is empty (no internet?)"); });
                    if (search != null) search.Text = "hd 650";
                    await Task.Delay(900);
                    T("AutoEQ search", delegate { Check(list.Items.Count > 0, "no results for 'hd 650'"); TLog("      results: " + list.Items.Count); });
                    T("screenshot: AutoEQ search", delegate { Snap((FrameworkElement)autoEq.Content, "12-autoeq.png"); SnapDoc((FrameworkElement)autoEq.Content, "find-my-headphones.png"); });
                    if (list != null && list.Items.Count > 0)
                    {
                        list.SelectedIndex = 0;
                        Button load = FindAll<Button>(autoEq).FirstOrDefault(b => (b.Content as string) == "Load correction");
                        if (load != null) load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        for (int i = 0; i < 30 && autoEq.IsVisible; i++) await Task.Delay(500);
                        T("AutoEQ download and load", delegate { Check(OutPage.Current.Name.Contains("AutoEQ"), "preset not loaded: " + OutPage.Current.Name); TLog("      loaded: " + OutPage.Current.Name + " (" + OutPage.Current.Bands.Count + " bands)"); });
                    }
                    if (autoEq.IsVisible) autoEq.Close();
                }

                T("low-memory tray mode", delegate
                {
                    Hide();
                    FreeUiIfHidden();
                    Check(!uiBuilt && Content == null, "UI wasn't released");
                    OutPage.SetBypass(true);
                    OutPage.SetBypass(false);
                    OutPage.NextPreset(1);
                    ApplyNow();
                    ShowFromTray();
                    Check(uiBuilt && Content != null, "UI didn't come back");
                });
                await Task.Delay(800);
                T("screenshot: after returning from tray", delegate { Snap((FrameworkElement)Content, "13-reopened.png"); });

                Window setup = null, uninstall = null;
                T("installer window", delegate { setup = Installer.SetupWindow(null); setup.Show(); });
                await Task.Delay(700);
                T("screenshot: installer", delegate { Snap((FrameworkElement)setup.Content, "14-installer.png"); setup.Close(); });
                Window fresh = null;
                T("docs: first-time installer opens", delegate
                {
                    Installer.DebugFreshInstall = true;
                    try { fresh = Installer.SetupWindow(null); fresh.Show(); }
                    finally { Installer.DebugFreshInstall = false; }
                });
                await Task.Delay(700);
                T("docs: first-time installer picture", delegate { SnapDoc((FrameworkElement)fresh.Content, "installer.png"); fresh.Close(); });
                T("uninstaller window", delegate { uninstall = Installer.UninstallWindow(); uninstall.Show(); });
                await Task.Delay(700);
                T("screenshot: uninstaller", delegate { Snap((FrameworkElement)uninstall.Content, "15-uninstaller.png"); uninstall.Close(); });

                Window confirmWin = null;
                T("confirm question before deleting", delegate
                {
                    confirmWin = MakeConfirm("Delete “My headphones”?", "This saved preset is removed for good. Built-in presets and your other saved presets stay.", "Delete", null);
                    confirmWin.Show();
                    confirmWin.UpdateLayout();
                    Check(FindAll<Button>(confirmWin).Count == 2, "expected Cancel and Delete");
                });
                await Task.Delay(700);
                T("screenshot: confirm question", delegate { Snap((FrameworkElement)confirmWin.Content, "16-confirm.png"); confirmWin.Close(); });
                T("status dot keeps its state when the window is rebuilt", delegate
                {
                    bool was = lastLive;
                    SetLive(true);
                    BuildUI();
                    Check(statusDot != null && ((SolidColorBrush)statusDot.Fill).Color == Theme.Green, "the dot went grey");
                    SetLive(was);
                });

                T("save settings", delegate { SaveAll(); Check(File.Exists(Path.Combine(Store.Dir, "settings.txt")), "settings.txt not written"); });
                string log = Store.ReadText("error.log");
                T("no errors logged by the app", delegate { Check(string.IsNullOrEmpty(log), "error.log:\r\n" + log); });
            }
            catch (Exception ex)
            {
                testFail++;
                TLog("FAIL  test runner stopped: " + ex);
            }
            TLog("");
            TLog("RESULT: " + testPass + " passed, " + testFail + " failed");
            testLog.Close();
            QuitApp();
        }
    }
}
