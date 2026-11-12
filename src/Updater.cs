// Toneline EQ - free system-wide EQ for Windows, built on Equalizer APO.
// Copyright (C) 2026 ExecPath7. Licensed under the GNU GPL v3 or later; see LICENSE.txt.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TonelineEQ
{
    public class UpdateInfo
    {
        public string Tag = "";       // "v0.7", as on GitHub
        public string Version = "";   // "0.7"
        public string Notes = "";     // the Release text, as plain text
        public string Page = "";      // the Release page, for "Open the download page"
    }

    // How updates work:
    //  1. Once a day (if you said yes) Toneline EQ asks GitHub for the newest Release of ExecPath7/TonelineEQ.
    //  2. If it is newer, a small "Update" button shows in the window. Nothing happens until you click it.
    //  3. Update now: the source code of that Release is downloaded from GitHub (HTTPS, codeload.github.com),
    //     unpacked with Windows' own tar, and built on this PC by the Release's own build.cmd, the same way
    //     the installer does it.
    //  4. The freshly built exe is started with --finish-update. It closes this copy, puts itself in place of
    //     the installed exe (presets, settings and device links aren't touched), and opens the new version.
    // Every future version must keep build.cmd (building out\TonelineEQ.exe) and --finish-update working.
    public static class Updater
    {
        public const string Owner = "ExecPath7", Repo = "TonelineEQ";
        const string ApiHost = "api.github.com", ZipHost = "codeload.github.com";
        const int MaxApiBytes = 1024 * 1024, MaxZipBytes = 60 * 1024 * 1024;

        public static string WorkDir { get { return Path.Combine(Path.GetTempPath(), "TonelineEQ-update"); } }

        public static Uri LatestUri { get { return new Uri("https://" + ApiHost + "/repos/" + Owner + "/" + Repo + "/releases/latest"); } }

        // The source code of one tagged version. The address is built here from the checked tag; nothing from the
        // answer is used as an address, so the download can't be pointed anywhere else.
        public static Uri ZipUri(string tag) { return new Uri("https://" + ZipHost + "/" + Owner + "/" + Repo + "/zip/refs/tags/" + tag); }

        // "v0.7", "0.7", "v1.2.3": a version tag. Anything else is ignored.
        public static string VersionOfTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            Match m = Regex.Match(tag, @"^v?([0-9]{1,4}(\.[0-9]{1,4}){1,3})$");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Toneline EQ numbers its versions like decimals: 0.6 < 0.65 < 0.7 < 1.0. Versions with three parts
        // (1.2.3) compare part by part.
        public static bool IsNewer(string candidate, string current)
        {
            if (VersionOfTag(candidate) == null || VersionOfTag(current) == null) return false;
            string[] a = candidate.Split('.'), b = current.Split('.');
            if (a.Length == 2 && b.Length == 2)
            {
                decimal x, y;
                if (!decimal.TryParse(candidate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out x) ||
                    !decimal.TryParse(current, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out y)) return false;
                return x > y;
            }
            Version va, vb;
            if (!System.Version.TryParse(candidate, out va) || !System.Version.TryParse(current, out vb)) return false;
            return va > vb;
        }

        // Reads GitHub's answer about the latest Release. Returns null if it isn't a usable, published release.
        public static UpdateInfo ParseRelease(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            if (Regex.IsMatch(json, "\"draft\"\\s*:\\s*true") || Regex.IsMatch(json, "\"prerelease\"\\s*:\\s*true")) return null;
            string tag = JsonString(json, "tag_name");
            string ver = VersionOfTag(tag);
            if (ver == null) return null;
            UpdateInfo u = new UpdateInfo { Tag = tag, Version = ver };
            u.Notes = PlainNotes(JsonString(json, "body") ?? "");
            u.Page = "https://github.com/" + Owner + "/" + Repo + "/releases/tag/" + Uri.EscapeDataString(tag);
            return u;
        }

        // Asks GitHub. Returns the Release if it is newer than this copy, otherwise null. Throws if GitHub can't be reached.
        public static UpdateInfo CheckLatest()
        {
            byte[] data = Get(LatestUri, ApiHost, MaxApiBytes, "application/vnd.github+json", 15000);
            UpdateInfo u = ParseRelease(Encoding.UTF8.GetString(data));
            return u != null && IsNewer(u.Version, Program.Version) ? u : null;
        }

        // Downloads (or, for the self-test, takes a local zip), unpacks and builds the new version.
        // Returns the folder of the unpacked source; the new exe is in its out folder.
        public static string Prepare(UpdateInfo u, string localZip, Action<string> progress)
        {
            if (VersionOfTag(u.Tag) == null) throw new InvalidOperationException("That isn't a Toneline EQ version.");
            string work = WorkDir;
            if (Directory.Exists(work)) Directory.Delete(work, true);
            string unpacked = Path.Combine(work, "src");
            Directory.CreateDirectory(unpacked);
            string zip = Path.Combine(work, "update.zip");

            progress("Downloading Toneline EQ " + u.Version + "…");
            if (localZip != null) File.Copy(localZip, zip);
            else File.WriteAllBytes(zip, Get(ZipUri(u.Tag), ZipHost, MaxZipBytes, null, 60000));

            progress("Unpacking…");
            // Windows' own tar (in Windows 10 and 11) unpacks zip files. It refuses paths that would leave the folder.
            Run(Sys.Tool("tar.exe"), "-xf \"" + zip + "\" -C \"" + unpacked + "\"", unpacked, 120000);
            string root = FindSource(unpacked);
            if (root == null) throw new InvalidOperationException("The download doesn't look like Toneline EQ.");
            string program = File.ReadAllText(Path.Combine(Path.Combine(root, "src"), "Program.cs"));
            if (program.IndexOf("Version = \"" + u.Version + "\"", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("The download is not version " + u.Version + ".");

            progress("Building Toneline EQ " + u.Version + " (about half a minute)…");
            string log = Run(Sys.Tool("cmd.exe"), "/d /c \"\"" + Path.Combine(root, "build.cmd") + "\"\"", root, 300000);
            string exe = Path.Combine(Path.Combine(root, "out"), "TonelineEQ.exe");
            if (!File.Exists(exe))
            {
                try { File.WriteAllText(Path.Combine(work, "build-log.txt"), log); } catch { }
                throw new InvalidOperationException("The new version didn't build on this PC. Details: " + Path.Combine(work, "build-log.txt"));
            }
            return root;
        }

        // Only GitHub's own two addresses, over HTTPS; anything else is refused before a connection is made.
        public static byte[] Get(Uri uri, string host, int maxBytes, string accept, int timeoutMs)
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) ||
                (host != ApiHost && host != ZipHost))
                throw new InvalidOperationException("Toneline EQ only updates from its own GitHub page.");
            return MainWindow.Fetch(uri, maxBytes, accept, timeoutMs);
        }

        // The single top folder of the unpacked zip that holds build.cmd and the app's source.
        public static string FindSource(string unpacked)
        {
            if (IsSource(unpacked)) return unpacked;
            string[] dirs = Directory.GetDirectories(unpacked);
            return dirs.Length == 1 && IsSource(dirs[0]) ? dirs[0] : null;
        }

        static bool IsSource(string d)
        {
            return File.Exists(Path.Combine(d, "build.cmd")) && File.Exists(Path.Combine(Path.Combine(d, "src"), "Program.cs"));
        }

        // Runs a Windows tool hidden, waits (with a time limit) and returns what it printed.
        static string Run(string exe, string args, string dir, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                Task<string> o = p.StandardOutput.ReadToEndAsync(), e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException(Path.GetFileName(exe) + " took too long.");
                }
                p.WaitForExit();
                string text = o.Result + e.Result;
                if (p.ExitCode != 0 && !exe.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Path.GetFileName(exe) + " failed: " + text.Trim());
                return text;
            }
        }

        // A JSON text value, with its escapes decoded. Enough for GitHub's flat release fields.
        public static string JsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"");
            if (!m.Success) return null;
            StringBuilder sb = new StringBuilder();
            for (int i = m.Index + m.Length; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (++i >= json.Length) break;
                char e = json[i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': break;
                    case 't': sb.Append('\t'); break;
                    case 'b': case 'f': break;
                    case 'u':
                        int code;
                        if (i + 4 < json.Length && int.TryParse(json.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        { sb.Append((char)code); i += 4; }
                        break;
                    default: sb.Append(e); break;   // \" \\ \/
                }
                if (sb.Length > 20000) break;
            }
            return null;
        }

        // Release notes are written in GitHub's Markdown; show them as calm plain text.
        public static string PlainNotes(string md)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                line = Regex.Replace(line, @"^#{1,6}\s*", "");
                line = Regex.Replace(line, @"^\s*[-*]\s+", "•  ");
                line = Regex.Replace(line, @"\*\*|__|`", "");
                line = Regex.Replace(line, @"\[([^\]]*)\]\([^)]*\)", "$1");
                line = Regex.Replace(line, @"<[^>]{1,200}>", "");
                sb.Append(line).Append('\n');
            }
            string text = Regex.Replace(sb.ToString(), "\n{3,}", "\n\n").Trim();
            return text.Length > 4000 ? text.Substring(0, 4000).TrimEnd() + "…" : text;
        }
    }

    public partial class MainWindow
    {
        // "1" checks daily, "0" never; missing means Toneline EQ hasn't asked yet.
        string updateCheck = "";
        DateTime updateLastCheck = DateTime.MinValue;
        string updateSkip = "";
        public UpdateInfo UpdateReady;   // a newer version GitHub told us about
        DispatcherTimer updateTimer;
        bool updateBusy, updateBalloonShown;
        Button updatePill;

        void LoadUpdateOptions(Dictionary<string, string> s)
        {
            string v;
            if (s.TryGetValue("updateCheck", out v) && (v == "0" || v == "1")) updateCheck = v;
            if (s.TryGetValue("updateSkip", out v)) updateSkip = v;
            long ticks;
            if (s.TryGetValue("updateLast", out v) && long.TryParse(v, NumberStyles.Integer, IC, out ticks) && ticks > 0 && ticks < DateTime.MaxValue.Ticks)
                updateLastCheck = new DateTime(ticks, DateTimeKind.Utc);
        }

        void SaveUpdateOptions(Dictionary<string, string> s)
        {
            if (updateCheck.Length > 0) s["updateCheck"] = updateCheck;
            if (updateSkip.Length > 0) s["updateSkip"] = updateSkip;
            if (updateLastCheck > DateTime.MinValue) s["updateLast"] = updateLastCheck.Ticks.ToString(IC);
        }

        public bool AutoUpdateCheck { get { return updateCheck == "1"; } }

        public void SetAutoUpdateCheck(bool on)
        {
            updateCheck = on ? "1" : "0";
            SaveAll();
            if (on) MaybeCheckForUpdate();
        }

        // Starts the daily check (installed copy only; the self-test never goes online).
        void StartUpdater()
        {
            if (Program.SelfTest || !IsInstalledCopy()) return;
            if (Program.JustUpdated)
            {
                Dispatcher.BeginInvoke(new Action(delegate { Say("Updated to Toneline EQ " + Program.Version); }), DispatcherPriority.ApplicationIdle);
                Task.Run(delegate { try { Directory.Delete(Updater.WorkDir, true); } catch { } });
            }
            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            updateTimer.Tick += delegate
            {
                if (updateCheck.Length == 0)
                {
                    // The first time, ask, but only while the window is open and in use (never pop up over a game from the tray).
                    if (!IsVisible || !IsActive || WindowState == WindowState.Minimized) return;
                    updateTimer.Interval = TimeSpan.FromHours(1);
                    bool yes = Ask("Check for updates automatically?",
                        "Once a day Toneline EQ asks GitHub whether a newer version is out. Nothing is installed until you click Update, and you can change this any time in Settings.",
                        "Check daily", "Not now");
                    SetAutoUpdateCheck(yes);
                    return;
                }
                updateTimer.Interval = TimeSpan.FromHours(1);
                MaybeCheckForUpdate();
            };
            updateTimer.Start();
        }

        void MaybeCheckForUpdate()
        {
            if (!AutoUpdateCheck || updateBusy) return;
            if ((DateTime.UtcNow - updateLastCheck).TotalHours < 23) { ShowUpdatePill(); return; }
            CheckForUpdate(false);
        }

        // manual: the "Check now" button, which says the result even when there is nothing new.
        public async void CheckForUpdate(bool manual)
        {
            if (updateBusy) return;
            if (!IsInstalledCopy())
            {
                if (manual) Say("Updates work in the installed copy of Toneline EQ");
                return;
            }
            updateBusy = true;
            if (manual) SetStatus("Checking for updates…");
            UpdateInfo found = null;
            string error = null;
            try { found = await Task.Run(() => Updater.CheckLatest()); }
            catch (Exception ex) { error = ex.Message; }
            updateBusy = false;
            if (error != null)
            {
                if (manual) Say("Couldn't reach GitHub. Check your internet and try again.");
                return;   // a quiet daily check simply tries again later
            }
            updateLastCheck = DateTime.UtcNow;
            UpdateReady = found;
            SaveAll();
            if (found == null)
            {
                ShowUpdatePill();
                if (manual) Say("You have the newest version (" + Program.Version + ")");
                return;
            }
            if (manual) { ShowUpdatePill(); OpenUpdate(); return; }
            if (found.Version == updateSkip) return;
            ShowUpdatePill();
            if (!IsVisible && tray != null && Notify && !updateBalloonShown)
            {
                updateBalloonShown = true;
                tray.ShowBalloonTip(5000, "Toneline EQ " + found.Version + " is out", "Open Toneline EQ and click Update when you're ready.", System.Windows.Forms.ToolTipIcon.None);
            }
        }

        // The small "Update to 0.7" button next to the Live chip.
        void ShowUpdatePill()
        {
            if (updatePill == null) return;
            bool show = UpdateReady != null && UpdateReady.Version != updateSkip && Updater.IsNewer(UpdateReady.Version, Program.Version);
            if (show) updatePill.Content = "Update to " + UpdateReady.Version;
            if (show && updatePill.Visibility != Visibility.Visible) { updatePill.Visibility = Visibility.Visible; Ui.FadeIn(updatePill, 0, 260); }
            if (!show) updatePill.Visibility = Visibility.Collapsed;
        }

        Button MakeUpdatePill()
        {
            Button b = Ui.Primary(Ui.Btn("Update", delegate { OpenUpdate(); }));
            b.FontSize = 12;
            b.Padding = new Thickness(13, 5, 13, 5);
            b.Margin = new Thickness(0, 0, 10, 0);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.ToolTip = "A newer version of Toneline EQ is out. Click to see what's new.";
            System.Windows.Automation.AutomationProperties.SetName(b, "Update Toneline EQ");
            b.Visibility = Visibility.Collapsed;
            updatePill = b;
            return b;
        }

        Window updateWin;

        // What's new, then Update now / Later / Skip this version.
        public Window MakeUpdateWindow(UpdateInfo u)
        {
            Window win = new Window
            {
                Title = "Toneline EQ", Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, FontSize = 13, UseLayoutRounding = true,
                Background = Theme.Br("#000000"), Foreground = Theme.Br("#E5E5EA"), FontFamily = Theme.Family
            };
            if (IsVisible) { win.Owner = this; win.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DarkTitle(win);

            StackPanel sp = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            DockPanel top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            FrameworkElement mark = Ui.Logo(44);
            mark.Margin = new Thickness(0, 0, 14, 0);
            top.Children.Add(mark);
            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock head = Ui.Txt("Toneline EQ " + u.Version + " is ready", 17, "#FFFFFF");
            head.FontWeight = FontWeights.SemiBold;
            titles.Children.Add(head);
            titles.Children.Add(Ui.Txt("You have " + Program.Version + ". Presets, settings and device links stay as they are.", 12, "#8E8E93"));
            top.Children.Add(titles);
            sp.Children.Add(top);

            TextBlock notes = Ui.Txt(u.Notes.Length > 0 ? u.Notes : "Bug fixes and improvements.", 13, "#C7C7CC");
            notes.TextWrapping = TextWrapping.Wrap;
            notes.LineHeight = 19;
            ScrollViewer sv = new ScrollViewer { Content = notes, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 12, 14, 12) };
            Border card = new Border { Child = sv, CornerRadius = new CornerRadius(12), Background = Theme.Br("#1C1C1E"), Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(card);

            TextBlock status = Ui.Txt("", 12, "#8E8E93");
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 0, 0, 12);
            status.Visibility = Visibility.Collapsed;
            sp.Children.Add(status);

            DockPanel buttons = new DockPanel { LastChildFill = false };
            Button go = Ui.Primary(Ui.Btn("Update now", null));
            go.Padding = new Thickness(18, 7, 18, 7);
            DockPanel.SetDock(go, Dock.Right);
            buttons.Children.Add(go);
            Button later = Ui.Btn("Later", delegate { win.Close(); });
            later.IsCancel = true;
            later.Padding = new Thickness(18, 7, 18, 7);
            later.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(later, Dock.Right);
            buttons.Children.Add(later);
            Button skip = Ui.Btn("Skip this version", delegate
            {
                updateSkip = u.Version;
                SaveAll();
                ShowUpdatePill();
                win.Close();
            });
            skip.Padding = new Thickness(12, 7, 12, 7);
            skip.Background = Brushes.Transparent;
            skip.Foreground = Theme.Br("#8E8E93");
            DockPanel.SetDock(skip, Dock.Left);
            buttons.Children.Add(skip);
            sp.Children.Add(buttons);

            bool working = false;
            win.Closing += delegate (object s, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; };
            go.Click += async (object sender, RoutedEventArgs args) =>
            {
                if (go.Tag is string) { OpenUrl((string)go.Tag); return; }
                working = true;
                go.IsEnabled = later.IsEnabled = skip.IsEnabled = false;
                status.Visibility = Visibility.Visible;
                status.Foreground = Theme.Br("#8E8E93");
                Action<string> progress = delegate (string text) { Dispatcher.BeginInvoke(new Action(delegate { status.Text = text; })); };
                string root = null, error = null;
                try { root = await Task.Run(() => Updater.Prepare(u, null, progress)); }
                catch (Exception ex) { Store.LogError(ex); error = ex.Message; }
                working = false;
                if (error != null)
                {
                    status.Text = "The update didn't work: " + error + "\nYour current version keeps running. You can also download the new version yourself.";
                    status.Foreground = Theme.Br("#FF6961");
                    go.Content = "Open the download page";
                    go.Tag = u.Page;
                    go.IsEnabled = later.IsEnabled = true;
                    return;
                }
                status.Text = "Restarting Toneline EQ…";
                try
                {
                    string exe = Path.Combine(Path.Combine(root, "out"), "TonelineEQ.exe");
                    ProcessStartInfo psi = new ProcessStartInfo(exe, "--finish-update \"" + root + "\"");
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = root;
                    Process.Start(psi);
                    win.Close();
                    QuitApp();
                }
                catch (Exception ex)
                {
                    Store.LogError(ex);
                    status.Text = "The update didn't work: " + ex.Message;
                    status.Foreground = Theme.Br("#FF6961");
                    go.IsEnabled = later.IsEnabled = true;
                }
            };

            UIElement content = Ui.WrapBackground(sp);
            ((FrameworkElement)content).LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale);
            win.Content = content;
            win.Loaded += delegate { go.Focus(); };
            return win;
        }

        public void OpenUpdate()
        {
            if (UpdateReady == null) return;
            if (updateWin != null && updateWin.IsVisible) { updateWin.Activate(); return; }
            updateWin = MakeUpdateWindow(UpdateReady);
            updateWin.Closed += delegate { updateWin = null; };
            updateWin.Show();
        }
    }
}
