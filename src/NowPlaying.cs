// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - "Now Playing": what Spotify, a browser or any media app is playing, with its artwork and
// play/pause/skip, read from Windows' media controls (the same source as the volume flyout).
// It is compiled in when the build finds Windows' media APIs (NOWPLAYING); otherwise it stays hidden.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
#if NOWPLAYING
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
#endif

namespace TonelineEQ
{
    public class NowPlayingInfo
    {
        public string Title = "", Artist = "", Key = "";
        public bool Playing;
        public byte[] Art;    // only filled when the track changed
    }

    public static class NowPlaying
    {
        // Self-test only: shown instead of the real song, so screenshots never reveal what you're listening to.
        public static NowPlayingInfo Demo;

#if NOWPLAYING
        public const bool Available = true;
        static GlobalSystemMediaTransportControlsSessionManager manager;

        // Waits for a Windows async call without the SDK-only helpers: Windows calls Completed when it's done
        // (straight away if it already finished).
        static Task<T> Wait<T>(IAsyncOperation<T> op)
        {
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
            op.Completed = delegate (IAsyncOperation<T> o, AsyncStatus status)
            {
                try
                {
                    if (status == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                    else if (status == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode ?? new Exception("Windows media call failed"));
                    else tcs.TrySetCanceled();
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
            return tcs.Task;
        }

        static async Task<GlobalSystemMediaTransportControlsSession> Session()
        {
            if (manager == null) manager = await Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            return manager != null ? manager.GetCurrentSession() : null;
        }

        // Returns null when nothing is playing or paused anywhere.
        public static async Task<NowPlayingInfo> Read(string lastKey)
        {
            if (Demo != null) return Demo;
            GlobalSystemMediaTransportControlsSession s = await Session();
            if (s == null) return null;
            GlobalSystemMediaTransportControlsSessionMediaProperties props = await Wait(s.TryGetMediaPropertiesAsync());
            if (props == null || string.IsNullOrEmpty(props.Title)) return null;
            NowPlayingInfo info = new NowPlayingInfo();
            info.Title = props.Title ?? "";
            info.Artist = props.Artist ?? "";
            info.Key = info.Title + "\n" + info.Artist;
            GlobalSystemMediaTransportControlsSessionPlaybackInfo pb = s.GetPlaybackInfo();
            info.Playing = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (info.Key != lastKey && props.Thumbnail != null)
            {
                try
                {
                    using (IRandomAccessStreamWithContentType st = await Wait(props.Thumbnail.OpenReadAsync()))
                    {
                        ulong size = st.Size;
                        if (size > 0 && size < 8 * 1024 * 1024)
                        {
                            using (DataReader r = new DataReader(st.GetInputStreamAt(0)))
                            {
                                uint got = await Wait(r.LoadAsync((uint)size));
                                byte[] buf = new byte[got];
                                r.ReadBytes(buf);
                                info.Art = buf;
                            }
                        }
                    }
                }
                catch { }
            }
            return info;
        }

        // 0 = previous, 1 = play/pause, 2 = next
        public static async Task Command(int what)
        {
            GlobalSystemMediaTransportControlsSession s = await Session();
            if (s == null) { MediaKey(what); return; }
            if (what == 0) await Wait(s.TrySkipPreviousAsync());
            else if (what == 2) await Wait(s.TrySkipNextAsync());
            else await Wait(s.TryTogglePlayPauseAsync());
        }
#else
        public const bool Available = false;
        public static Task<NowPlayingInfo> Read(string lastKey) { return Task.FromResult<NowPlayingInfo>(Demo); }
        public static Task Command(int what) { MediaKey(what); return Task.FromResult(0); }
#endif

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);

        // Fallback: the keyboard's media keys.
        static void MediaKey(int what)
        {
            byte vk = (byte)(what == 0 ? 0xB1 : (what == 2 ? 0xB0 : 0xB3));
            keybd_event(vk, 0, 1, IntPtr.Zero);
            keybd_event(vk, 0, 3, IntPtr.Zero);
        }
    }
}
