// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - an Easy Effects style equalizer for Windows (headphones and microphone), powered by Equalizer APO.
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4), so it builds without Visual Studio.
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TonelineEQ
{
    public static class Program
    {
        public const string Version = "0.5";
        public static bool StartInTray;
        public static bool SelfTest;
        static Mutex instanceLock;

        public static void ReleaseSingleInstance()
        {
            try { if (instanceLock != null) { instanceLock.ReleaseMutex(); instanceLock.Dispose(); instanceLock = null; } }
            catch { }
        }

        static bool Has(string[] args, string flag)
        {
            return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        }

        static void HookErrors(Application app)
        {
            app.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Store.LogError(e.Exception);
                MessageBox.Show("Toneline EQ hit a problem and wrote it to:\n" + Store.ErrorLog + "\n\n" + e.Exception.Message, "Toneline EQ", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // Installer and uninstaller run as their own small windows.
            if (Has(args, "--setup") || Has(args, "--uninstall"))
            {
                Application setupApp = new Application();
                HookErrors(setupApp);
                try { Theme.Load(Store.LoadSettings()); setupApp.Resources = Theme.Resources(); } catch (Exception ex) { Store.LogError(ex); }
                string source = null;
                int i = Array.FindIndex(args, a => string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && i + 1 < args.Length) source = args[i + 1];
                Window w = Has(args, "--uninstall") ? Installer.UninstallWindow() : Installer.SetupWindow(source);
                setupApp.Run(w);
                return;
            }

            // Self-test: runs every page and window in an isolated data folder, writes a report and screenshots, then exits.
            // It never writes to Equalizer APO, startup, hotkeys or the tray, and ignores any running Toneline EQ.
            if (Has(args, "--selftest"))
            {
                SelfTest = true;
                int t = Array.FindIndex(args, a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));
                string dir = t + 1 < args.Length ? args[t + 1].Trim('"') : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TonelineEQ-selftest");
                System.IO.Directory.CreateDirectory(dir);
                Store.DirOverride = System.IO.Path.Combine(dir, "appdata");
                string crash = System.IO.Path.Combine(dir, "selftest-crash.txt");
                Thread watchdog = new Thread(() => { Thread.Sleep(240000); try { System.IO.File.AppendAllText(crash, "TIMEOUT after 240 s\r\n"); } catch { } Environment.Exit(3); });
                watchdog.IsBackground = true;
                watchdog.Start();
                Application testApp = new Application();
                testApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                testApp.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    try { System.IO.File.AppendAllText(crash, "UNHANDLED " + e.Exception + "\r\n\r\n"); } catch { }
                    e.Handled = true;
                };
                try
                {
                    MainWindow tw = new MainWindow();
                    testApp.Dispatcher.BeginInvoke(new Action(delegate { tw.RunSelfTest(dir); }));
                    testApp.Run();
                }
                catch (Exception ex)
                {
                    try { System.IO.File.AppendAllText(crash, "STARTUP FAILED " + ex + "\r\n"); } catch { }
                }
                return;
            }

            StartInTray = Has(args, "--tray");
            bool created;
            instanceLock = new Mutex(true, "TonelineEQ.SingleInstance", out created);
            EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "TonelineEQ.ShowWindow");
            if (!created)
            {
                if (!StartInTray) showSignal.Set();
                return;
            }
            EventWaitHandle quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "TonelineEQ.Quit");

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            HookErrors(app);
            try
            {
                MainWindow mw = new MainWindow();
                ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { mw.Dispatcher.BeginInvoke(new Action(mw.ShowFromTray)); }, null, -1, false);
                ThreadPool.RegisterWaitForSingleObject(quitSignal, delegate { mw.Dispatcher.BeginInvoke(new Action(mw.QuitApp)); }, null, -1, false);
                if (!StartInTray) mw.Show();
                app.Run();
                ReleaseSingleInstance();
            }
            catch (Exception ex)
            {
                Store.LogError(ex);
                MessageBox.Show("Toneline EQ couldn't start:\n" + ex.Message + "\n\nDetails are in " + Store.ErrorLog, "Toneline EQ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
