// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - per-user installer and uninstaller (no admin rights needed).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace TonelineEQ
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLinkCom { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, int flags);
        void GetIDList(out IntPtr idl);
        void SetIDList(IntPtr idl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TonelineEQ";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string InstallDir { get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "TonelineEQ"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "TonelineEQ.exe"); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Toneline EQ.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Toneline EQ.lnk"); } }
        static string SelfExe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        static void CreateShortcut(string lnk, string target, string args, string description)
        {
            IShellLinkW link = (IShellLinkW)new ShellLinkCom();
            link.SetPath(target);
            link.SetArguments(args ?? "");
            link.SetWorkingDirectory(Path.GetDirectoryName(target));
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(lnk, false);
            Marshal.ReleaseComObject(link);
        }

        // Before the rename this app was called EasyEQ. Installing Toneline EQ quits and removes that old copy:
        // its startup entry, Start menu and desktop shortcuts, Apps entry and program folder. Settings and presets
        // are copied over by Store the first time Toneline EQ runs.
        static void RemoveLegacyEasyEQ()
        {
            try { using (EventWaitHandle quit = EventWaitHandle.OpenExisting("EasyEQ.Quit")) quit.Set(); } catch { }
            foreach (Process p in Process.GetProcessesByName("EasyEQ"))
            {
                try { if (!p.WaitForExit(5000)) p.Kill(); } catch { }
            }
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("EasyEQ", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\EasyEQ", false); } catch { }
            foreach (string lnk in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "EasyEQ.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EasyEQ.lnk") })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            string oldDir = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "EasyEQ");
            try { if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true); } catch { }
        }

        static string LegacyDesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EasyEQ.lnk"); } }

        // Keep the old copy's "start with Windows" choice when updating from EasyEQ.
        static bool LegacyStartsWithWindows()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("EasyEQ") != null; }
            catch { return false; }
        }

        public static bool DebugFreshInstall;   // self-test: show the first-time install screen (with a generic user folder)

        public static bool LegacyInstalled
        {
            get { return File.Exists(Path.Combine(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "EasyEQ"), "EasyEQ.exe")); }
        }

        // Asks a running Toneline EQ to quit and waits for it, so its exe can be replaced or removed.
        static void StopRunningInstances()
        {
            try
            {
                using (EventWaitHandle quit = EventWaitHandle.OpenExisting("TonelineEQ.Quit")) quit.Set();
            }
            catch { }
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("TonelineEQ"))
            {
                if (p.Id == self) continue;
                try { if (!p.WaitForExit(5000)) p.Kill(); } catch { }
            }
        }

        static void SetRun(bool on, string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("TonelineEQ", "\"" + exe + "\" --tray");
                else k.DeleteValue("TonelineEQ", false);
            }
        }

        static void Register(string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Toneline EQ");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "Toneline EQ");
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        // ---------------------------------------------------------------- shared look
        static Window MakeWindow(string title, double w, double h)
        {
            Window win = new Window
            {
                Title = title, Width = w, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Theme.Br("#000000"), Foreground = Theme.Br("#E5E5EA"),
                FontFamily = Theme.Family, FontSize = 13, UseLayoutRounding = true
            };
            return win;
        }

        static UIElement Header(string title, string subtitle)
        {
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            Border logo = Ui.Logo(46);
            logo.Margin = new Thickness(0, 0, 14, 0);
            head.Children.Add(logo);
            StackPanel t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock big = Ui.Txt(title, 21, "#FFFFFF");
            big.FontWeight = FontWeights.Bold;
            t.Children.Add(big);
            t.Children.Add(Ui.Txt(subtitle, 12, "#8E8E93"));
            head.Children.Add(t);
            return head;
        }

        // A grouped card of switch rows, Apple Settings style.
        static StackPanel OptionGroup(StackPanel parent)
        {
            StackPanel opts = new StackPanel();
            parent.Children.Add(new Border { Child = opts, Background = Theme.Panel(), CornerRadius = new CornerRadius(12) });
            return opts;
        }

        static CheckBox Option(StackPanel parent, string text, bool value)
        {
            if (parent.Children.Count > 0) parent.Children.Add(new Border { Height = 1, Background = Theme.Br("#38383A"), Margin = new Thickness(16, 0, 0, 0), Opacity = 0.7 });
            DockPanel row = new DockPanel { Margin = new Thickness(16, 9, 14, 9), Background = Brushes.Transparent };
            CheckBox c = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(c, Dock.Right);
            row.Children.Add(c);
            TextBlock l = Ui.Txt(text, 13.5, "#FFFFFF");
            l.Margin = new Thickness(0, 0, 12, 0);
            l.TextWrapping = TextWrapping.Wrap;
            row.MouseLeftButtonUp += delegate (object s, System.Windows.Input.MouseButtonEventArgs e) { if (!(e.OriginalSource is DependencyObject) || !IsInside(e.OriginalSource as DependencyObject, c)) c.IsChecked = c.IsChecked != true; };
            row.Children.Add(l);
            parent.Children.Add(row);
            return c;
        }

        static bool IsInside(DependencyObject d, DependencyObject ancestor)
        {
            while (d != null) { if (d == ancestor) return true; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
            return false;
        }

        static Button Primary(string text)
        {
            return Ui.Primary(new Button { Content = text, Padding = new Thickness(22, 8, 22, 8) });
        }

        // The bundled Inter font (SIL Open Font License) goes next to TonelineEQ.exe in a fonts folder.
        static void CopyFonts(string exeDir, string sourceDir)
        {
            string from = null;
            if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(Path.Combine(sourceDir.Trim('"'), "fonts"))) from = Path.Combine(sourceDir.Trim('"'), "fonts");
            else if (Directory.Exists(Path.Combine(exeDir, "fonts"))) from = Path.Combine(exeDir, "fonts");
            if (from == null) return;
            string to = Path.Combine(InstallDir, "fonts");
            if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
            {
                try { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); }
                catch (IOException) { }   // a running Toneline EQ may still hold the old font open; it is identical
            }
        }

        // ---------------------------------------------------------------- setup
        public static Window SetupWindow(string sourceDir)
        {
            bool update = !DebugFreshInstall && (IsInstalled || LegacyInstalled);
            Window win = MakeWindow(update ? "Update Toneline EQ" : "Install Toneline EQ", 560, 560);
            StackPanel sp = new StackPanel { Margin = new Thickness(30, 26, 30, 24) };
            sp.Children.Add(Header(update ? "Update Toneline EQ" : "Install Toneline EQ", "Version " + Program.Version + "  ·  system-wide EQ for headphones and microphone"));

            TextBlock where = Ui.Txt("Installs for your Windows account only, so no administrator rights are needed:\n" + (DebugFreshInstall ? @"C:\Users\you\AppData\Local\Programs\TonelineEQ" : InstallDir), 12, "#8E8E93");
            where.TextWrapping = TextWrapping.Wrap;
            where.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(where);
            TextBlock terms = Ui.Txt("By installing you accept the GNU GPL v3 license (LICENSE.txt). Toneline EQ is free and comes with no warranty; use it at your own risk. It collects no data.", 11.5, "#8E8E93");
            terms.TextWrapping = TextWrapping.Wrap;
            terms.Margin = new Thickness(0, 0, 0, 18);
            sp.Children.Add(terms);

            StackPanel opts = OptionGroup(sp);
            CheckBox startMenu = Option(opts, "Add to the Start menu", true);
            CheckBox desktop = Option(opts, "Add a desktop shortcut", !DebugFreshInstall && (File.Exists(DesktopLink) || File.Exists(LegacyDesktopLink)));
            CheckBox startup = Option(opts, "Start with Windows (quietly, in the tray)", !update || MainWindow.IsStartup() || LegacyStartsWithWindows());
            CheckBox open = Option(opts, "Open Toneline EQ when done", true);

            bool apo = Apo.ConfigDir() != null;
            Border apoBox = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 10, 0, 0), Background = apo ? Theme.Panel() : Theme.Br("#312323") };
            DockPanel apoRow = new DockPanel();
            if (!apo)
            {
                Button get = Ui.Btn("Get Equalizer APO", delegate { MainWindow.OpenUrl("https://sourceforge.net/projects/equalizerapo/"); });
                DockPanel.SetDock(get, Dock.Right);
                apoRow.Children.Add(get);
            }
            TextBlock apoText = Ui.Txt(apo
                ? "Equalizer APO is installed. When Toneline EQ opens, click Connect once to link them."
                : "Equalizer APO isn't installed yet. Toneline EQ needs it to change Windows sound. Install it (tick your headphones, DAC and mic in its Configurator), then restart Windows.",
                12.5, apo ? "#AEAEB2" : "#FF6961");
            apoText.TextWrapping = TextWrapping.Wrap;
            apoText.Margin = new Thickness(0, 0, 12, 0);
            apoRow.Children.Add(apoText);
            apoBox.Child = apoRow;
            sp.Children.Add(apoBox);

            TextBlock status = Ui.Txt("", 12.5, "#AEAEB2");
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 14, 0, 0);

            DockPanel buttons = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
            Button go = Primary(update ? "Update" : "Install");
            DockPanel.SetDock(go, Dock.Right);
            buttons.Children.Add(go);
            Button cancel = Ui.Btn("Cancel", delegate { win.Close(); });
            cancel.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            sp.Children.Add(status);

            bool done = false;
            go.Click += delegate
            {
                if (done) { win.Close(); return; }
                go.IsEnabled = false;
                status.Text = "Installing…";
                try
                {
                    RemoveLegacyEasyEQ();
                    StopRunningInstances();
                    Directory.CreateDirectory(InstallDir);
                    string self = SelfExe;
                    if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                        File.Copy(self, InstalledExe, true);
                    if (!string.IsNullOrEmpty(sourceDir))
                    {
                        // The readme plus the license and notices travel with the app (GPL-3.0 requires the license).
                        foreach (string doc in new[] { "README.txt", "LICENSE.txt", "THIRD-PARTY-NOTICES.txt", "PRIVACY.txt" })
                        {
                            string src = Path.Combine(sourceDir.Trim('"'), doc);
                            if (File.Exists(src)) File.Copy(src, Path.Combine(InstallDir, doc), true);
                        }
                    }
                    CopyFonts(Path.GetDirectoryName(self), sourceDir);
                    if (startMenu.IsChecked == true) CreateShortcut(StartMenuLink, InstalledExe, "", "Toneline EQ equalizer");
                    else if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink);
                    if (desktop.IsChecked == true) CreateShortcut(DesktopLink, InstalledExe, "", "Toneline EQ equalizer");
                    else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);
                    SetRun(startup.IsChecked == true, InstalledExe);
                    Register(InstalledExe);
                    // Remember the startup choice so Toneline EQ doesn't ask again on first run.
                    Dictionary<string, string> s = Store.LoadSettings();
                    s["startupAsked"] = "1";
                    Store.SaveSettings(s);

                    status.Text = "Done. You can delete the folder you installed from. Toneline EQ is in the Start menu, and you can remove it any time from Windows Settings > Apps.";
                    if (open.IsChecked == true)
                    {
                        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
                        win.Close();
                    }
                    else
                    {
                        done = true;
                        go.Content = "Close";
                        go.IsEnabled = true;
                    }
                }
                catch (Exception ex)
                {
                    Store.LogError(ex);
                    status.Text = "Install failed: " + ex.Message;
                    status.Foreground = Theme.Br("#FF6961");
                    go.IsEnabled = true;
                }
            };

            win.Content = Ui.WrapBackground(sp);
            return win;
        }

        // ---------------------------------------------------------------- uninstall
        static string LatestBackup(string dir)
        {
            if (dir == null || !Directory.Exists(dir)) return null;
            // Backups made by Toneline EQ, or by EasyEQ before the rename.
            string[] files = Directory.GetFiles(dir, "config-before-TonelineEQ-*.txt").Concat(Directory.GetFiles(dir, "config-before-EasyEQ-*.txt")).ToArray();
            return files.Length == 0 ? null : files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Last();
        }

        public static Window UninstallWindow()
        {
            Window win = MakeWindow("Uninstall Toneline EQ", 560, 470);
            StackPanel sp = new StackPanel { Margin = new Thickness(30, 26, 30, 24) };
            sp.Children.Add(Header("Uninstall Toneline EQ", "Version " + Program.Version));

            string apoDir = Apo.ConfigDir();
            string backup = LatestBackup(apoDir);
            StackPanel opts = OptionGroup(sp);
            CheckBox restore = Option(opts, backup != null ? "Put back my Equalizer APO settings from before Toneline EQ" : "Turn off Toneline EQ's EQ in Equalizer APO", true);
            CheckBox keep = Option(opts, "Keep my presets and settings (for a later reinstall)", true);
            TextBlock note = Ui.Txt("Equalizer APO itself stays installed. Remove it separately in Windows Settings > Apps if you want.", 12, "#8E8E93");
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(4, 10, 0, 0);
            sp.Children.Add(note);

            TextBlock status = Ui.Txt("", 12.5, "#AEAEB2");
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 14, 0, 0);
            DockPanel buttons = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
            Button go = Primary("Uninstall");
            DockPanel.SetDock(go, Dock.Right);
            buttons.Children.Add(go);
            Button cancel = Ui.Btn("Cancel", delegate { win.Close(); });
            cancel.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(cancel, Dock.Right);
            buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            sp.Children.Add(status);

            bool finished = false;
            go.Click += delegate
            {
                if (finished) { win.Close(); return; }
                go.IsEnabled = false;
                List<string> problems = new List<string>();
                StopRunningInstances();

                if (restore.IsChecked == true && apoDir != null)
                {
                    try
                    {
                        if (backup != null) File.Copy(backup, Path.Combine(apoDir, "config.txt"), true);
                        string ours = Path.Combine(apoDir, Apo.FileName);
                        if (File.Exists(ours)) File.WriteAllText(ours, "# Toneline EQ was uninstalled\r\n");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        problems.Add("Equalizer APO's settings need administrator rights to change. Run the uninstaller again as administrator, or restore config.txt by hand.");
                    }
                    catch (Exception ex) { problems.Add("Equalizer APO settings: " + ex.Message); }
                }

                try { SetRun(false, ""); } catch { }
                try { if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink); } catch { }
                try { if (File.Exists(DesktopLink)) File.Delete(DesktopLink); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
                if (keep.IsChecked != true)
                {
                    try { Directory.Delete(Store.Dir, true); } catch (Exception ex) { problems.Add("Settings folder: " + ex.Message); }
                }

                // The running exe can't delete itself: let a hidden command finish after we exit.
                try
                {
                    if (Directory.Exists(InstallDir))
                    {
                        bool inside = SelfExe.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase);
                        if (inside)
                        {
                            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"");
                            psi.CreateNoWindow = true;
                            psi.WindowStyle = ProcessWindowStyle.Hidden;
                            psi.UseShellExecute = false;
                            Process.Start(psi);
                        }
                        else Directory.Delete(InstallDir, true);
                    }
                }
                catch (Exception ex) { problems.Add("Program folder: " + ex.Message); }

                status.Text = problems.Count == 0 ? "Toneline EQ was removed." : "Toneline EQ was removed, with these notes:\n" + string.Join("\n", problems);
                if (problems.Count > 0) status.Foreground = Theme.Br("#FF6961");
                finished = true;
                go.Content = "Close";
                go.IsEnabled = true;
                cancel.Visibility = Visibility.Collapsed;
            };

            win.Content = Ui.WrapBackground(sp);
            return win;
        }
    }
}
