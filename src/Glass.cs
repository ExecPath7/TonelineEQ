// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - macOS-style window materials on Windows 11: the Mica backdrop (the desktop's colours softly showing
// through, like macOS vibrancy) and a unified title bar where Toneline EQ's own header replaces the Windows caption.
// Everything here is done by Windows' compositor, so it costs no CPU. Older Windows keeps the solid look.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace TonelineEQ
{
    public static class Glass
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OsVersionInfo v);

        [StructLayout(LayoutKind.Sequential)]
        struct Margins { public int Left, Right, Top, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OsVersionInfo
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
        }

        const int DarkModeAttr = 20, BackdropAttr = 38, BackdropNone = 1, BackdropMica = 2;
        public const double CaptionButtonsWidth = 150;   // room left free for Windows' minimise / maximise / close

        static int build = -1;
        // Real Windows build (Environment.OSVersion reports 6.2 for apps without a manifest).
        public static int WindowsBuild
        {
            get
            {
                if (build < 0)
                {
                    build = 0;
                    try { OsVersionInfo v = new OsVersionInfo(); v.Size = Marshal.SizeOf(typeof(OsVersionInfo)); if (RtlGetVersion(ref v) == 0) build = v.Build; } catch { }
                }
                return build;
            }
        }

        // The documented backdrop attribute exists from Windows 11 22H2 (build 22621).
        public static bool Supported { get { return WindowsBuild >= 22621; } }

        // Unified title bar: the top strip of the window drags it, Windows still draws the caption buttons.
        public static void SetChrome(Window w, bool on, double captionHeight)
        {
            if (!on) { WindowChrome.SetWindowChrome(w, null); return; }
            WindowChrome ch = new WindowChrome();
            ch.CaptionHeight = captionHeight;
            ch.GlassFrameThickness = new Thickness(-1);
            ch.ResizeBorderThickness = new Thickness(6);
            ch.CornerRadius = new CornerRadius(0);
            ch.UseAeroCaptionButtons = true;
            WindowChrome.SetWindowChrome(w, ch);
        }

        // Turns the Mica backdrop on. Returns false when Windows refused, so the caller can fall back to solid.
        public static bool Enable(Window w, bool extendFrame)
        {
            if (!Supported) return false;
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                HwndSource src = HwndSource.FromHwnd(h);
                if (h == IntPtr.Zero || src == null || src.CompositionTarget == null) return false;
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
                if (extendFrame) { Margins m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }; DwmExtendFrameIntoClientArea(h, ref m); }
                int dark = Theme.IsLight ? 0 : 1;
                DwmSetWindowAttribute(h, DarkModeAttr, ref dark, 4);
                int type = BackdropMica;
                if (DwmSetWindowAttribute(h, BackdropAttr, ref type, 4) != 0) return false;
                w.Background = Brushes.Transparent;
                return true;
            }
            catch { return false; }
        }

        public static void Disable(Window w, Brush solid)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero)
                {
                    int type = BackdropNone;
                    DwmSetWindowAttribute(h, BackdropAttr, ref type, 4);
                    Margins m = new Margins();
                    DwmExtendFrameIntoClientArea(h, ref m);
                    HwndSource src = HwndSource.FromHwnd(h);
                    if (src != null && src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Theme.Bg;
                }
            }
            catch { }
            w.Background = solid;
        }
    }

    // Apple-style spring: a quick move that settles with a tiny overshoot (used for sliding controls).
    public class SpringEase : EasingFunctionBase
    {
        public double Damping = 0.72, Speed = 11;
        public SpringEase() { EasingMode = EasingMode.EaseIn; }   // EaseIn = use EaseInCore's curve as is
        protected override double EaseInCore(double t)
        {
            if (t >= 1) return 1;
            double w = Speed, z = Damping, wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        }
        protected override Freezable CreateInstanceCore() { return new SpringEase { Damping = Damping, Speed = Speed }; }
    }
}
