// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - Windows audio: device list, live capture (speakers via loopback, or a microphone) and FFT.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace TonelineEQ
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    public class AudioDevice
    {
        public string Id;    // full Windows endpoint id
        public string Guid;  // the {...} part, which Equalizer APO accepts in a "Device:" line
        public string Name;
    }

    public static class AudioDevices
    {
        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        // Id of Windows' current default device (0 = playback, 1 = recording), or null.
        public static string DefaultId(int dataFlow)
        {
            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (en.GetDefaultAudioEndpoint(dataFlow, 0, out dev) < 0 || dev == null) return null;
                string id;
                return dev.GetId(out id) >= 0 ? id : null;
            }
            catch { return null; }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        public static string GuidOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int dot = id.LastIndexOf('.');
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }

        static readonly Dictionary<string, KeyValuePair<DateTime, bool?>> apoCache = new Dictionary<string, KeyValuePair<DateTime, bool?>>();

        // Is Equalizer APO installed on this device? null = couldn't tell.
        // Windows keeps each device's audio effects in its FxProperties registry key; we look for a COM class served by EqualizerAPO.dll.
        public static bool? HasApo(string guid, int dataFlow)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string key = dataFlow + guid;
            KeyValuePair<DateTime, bool?> hit;
            if (apoCache.TryGetValue(key, out hit) && (DateTime.Now - hit.Key).TotalSeconds < 30) return hit.Value;
            bool? result = null;
            try
            {
                using (RegistryKey hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey fx = hk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\" + (dataFlow == 0 ? "Render" : "Capture") + "\\" + guid + "\\FxProperties"))
                {
                    if (fx == null) result = false;
                    else
                    {
                        result = false;
                        foreach (string name in fx.GetValueNames())
                        {
                            object v = fx.GetValue(name);
                            List<string> cands = new List<string>();
                            string sv = v as string;
                            string[] arr = v as string[];
                            if (sv != null) cands.Add(sv);
                            if (arr != null) cands.AddRange(arr);
                            foreach (string c in cands)
                            {
                                string t = c.Trim();
                                if (t.StartsWith("{") && IsApoClass(hk, t)) { result = true; break; }
                            }
                            if (result == true) break;
                        }
                    }
                }
            }
            catch { result = null; }
            apoCache[key] = new KeyValuePair<DateTime, bool?>(DateTime.Now, result);
            return result;
        }

        static bool IsApoClass(RegistryKey hklm, string clsid)
        {
            try
            {
                using (RegistryKey k = hklm.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + clsid + @"\InprocServer32"))
                {
                    if (k == null) return false;
                    string p = k.GetValue("") as string;
                    return p != null && p.IndexOf("EqualizerAPO", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        // dataFlow 0 = playback, 1 = recording. Includes unplugged devices so their EQ still applies when plugged back in.
        public static List<AudioDevice> List(int dataFlow, bool includeUnplugged)
        {
            List<AudioDevice> list = new List<AudioDevice>();
            IMMDeviceEnumerator en = null;
            IMMDeviceCollection col = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                int mask = includeUnplugged ? (1 | 8) : 1;
                if (en.EnumAudioEndpoints(dataFlow, mask, out col) < 0 || col == null) return list;
                uint count;
                col.GetCount(out count);
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice dev;
                    if (col.Item(i, out dev) < 0 || dev == null) continue;
                    try
                    {
                        string id;
                        if (dev.GetId(out id) < 0 || id == null) continue;
                        string name = id;
                        IPropertyStore store;
                        if (dev.OpenPropertyStore(0, out store) >= 0 && store != null)
                        {
                            PropertyKey key = new PropertyKey();
                            key.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
                            key.pid = 14;
                            PropVariant pv;
                            if (store.GetValue(ref key, out pv) >= 0)
                            {
                                if (pv.vt == 31 && pv.p != IntPtr.Zero) name = Marshal.PtrToStringUni(pv.p);
                                PropVariantClear(ref pv);
                            }
                            Marshal.ReleaseComObject(store);
                        }
                        int dot = id.LastIndexOf('.');
                        AudioDevice d = new AudioDevice();
                        d.Id = id;
                        d.Guid = dot >= 0 ? id.Substring(dot + 1) : id;
                        d.Name = name;
                        list.Add(d);
                    }
                    finally { Marshal.ReleaseComObject(dev); }
                }
            }
            catch (Exception ex) { Store.LogError(ex); }
            finally
            {
                if (col != null) Marshal.ReleaseComObject(col);
                if (en != null) Marshal.ReleaseComObject(en);
            }
            return list;
        }
    }

    // Captures audio into a mono ring buffer on its own MTA thread.
    // Loopback = what Windows is playing on the default output; otherwise a microphone.
    public class AudioCapture
    {
        public const int RingSize = 16384;
        readonly float[] ring = new float[RingSize];
        int writePos;
        readonly object gate = new object();
        readonly bool loopback;
        volatile bool running;
        volatile int generation;
        public volatile int SampleRate = 48000;
        public volatile string LastError;
        public volatile string DeviceId = "";   // empty = Windows default device

        public AudioCapture(bool loopback) { this.loopback = loopback; }

        // Short recordings for "Test my mic" (taken from the same stream, so the EQ is already applied).
        float[] rec;
        int recPos;
        volatile bool recording;
        public bool Recording { get { return recording; } }
        public bool IsRunning { get { return running; } }

        public void BeginRecording(double seconds)
        {
            lock (gate) { rec = new float[Math.Max(1, (int)(SampleRate * seconds))]; recPos = 0; recording = true; }
        }

        public float[] TakeRecording()
        {
            lock (gate)
            {
                recording = false;
                if (rec == null) return new float[0];
                float[] r = new float[recPos];
                Array.Copy(rec, r, recPos);
                rec = null;
                return r;
            }
        }

        public void Start()
        {
            generation++;
            running = true;
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start(generation);
        }

        public void Stop()
        {
            running = false;
            generation++;
        }

        public void CopyLatest(float[] dest)
        {
            lock (gate)
            {
                int n = Math.Min(dest.Length, RingSize);
                int start = writePos - n + RingSize;
                for (int i = 0; i < n; i++) dest[i] = ring[(start + i) % RingSize];
            }
        }

        static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

        void Run(object state)
        {
            int gen = (int)state;
            while (running && gen == generation)
            {
                // A normal return means the default device changed: reopen right away. After an error, retry in a second.
                try { Session(gen); LastError = null; continue; }
                catch (Exception ex) { LastError = ex.Message; }
                for (int i = 0; i < 10 && running && gen == generation; i++) Thread.Sleep(100);
            }
        }

        void PushSilence(int count)
        {
            lock (gate)
            {
                for (int i = 0; i < count; i++) { ring[writePos] = 0; writePos = (writePos + 1) % RingSize; }
            }
        }

        void Session(int gen)
        {
            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            IAudioClient client = null;
            IAudioCaptureClient cap = null;
            IntPtr fmt = IntPtr.Zero;
            int flow = loopback ? 0 : 1;
            string wanted = DeviceId ?? "";
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (wanted.Length > 0) Check(en.GetDevice(wanted, out dev));
                else Check(en.GetDefaultAudioEndpoint(flow, 0, out dev));
                string devId;
                Check(dev.GetId(out devId));
                Guid iidClient = typeof(IAudioClient).GUID;
                object o;
                Check(dev.Activate(ref iidClient, 23, IntPtr.Zero, out o));
                client = (IAudioClient)o;
                Check(client.GetMixFormat(out fmt));

                int tag = Marshal.ReadInt16(fmt, 0) & 0xFFFF;
                int channels = Marshal.ReadInt16(fmt, 2);
                int rate = Marshal.ReadInt32(fmt, 4);
                int block = Marshal.ReadInt16(fmt, 12);
                int bits = Marshal.ReadInt16(fmt, 14);
                bool isFloat = tag == 3;
                if (tag == 0xFFFE)
                {
                    byte[] g = new byte[16];
                    Marshal.Copy(new IntPtr(fmt.ToInt64() + 24), g, 0, 16);
                    isFloat = new Guid(g) == new Guid("00000003-0000-0010-8000-00aa00389b71");
                }
                if (channels < 1 || block < 1) throw new InvalidOperationException("Unsupported audio format");
                SampleRate = rate;
                int bytesPer = bits / 8;
                int useCh = Math.Min(channels, 2);

                Check(client.Initialize(0, loopback ? 0x00020000 : 0, 2000000, 0, fmt, IntPtr.Zero));
                Guid iidCap = typeof(IAudioCaptureClient).GUID;
                object co;
                Check(client.GetService(ref iidCap, out co));
                cap = (IAudioCaptureClient)co;
                Check(client.Start());

                byte[] buf = new byte[0];
                DateTime lastData = DateTime.UtcNow, lastCheck = DateTime.UtcNow;
                while (running && gen == generation && wanted == (DeviceId ?? ""))
                {
                    Thread.Sleep(10);
                    uint packet;
                    Check(cap.GetNextPacketSize(out packet));
                    bool got = false;
                    while (packet > 0)
                    {
                        IntPtr data;
                        uint frames, flags;
                        ulong pos1, pos2;
                        Check(cap.GetBuffer(out data, out frames, out flags, out pos1, out pos2));
                        int n = (int)frames;
                        bool silent = (flags & 2) != 0;
                        int bytes = n * block;
                        if (!silent && bytes > 0)
                        {
                            if (buf.Length < bytes) buf = new byte[bytes];
                            Marshal.Copy(data, buf, 0, bytes);
                        }
                        lock (gate)
                        {
                            for (int f = 0; f < n; f++)
                            {
                                float sum = 0;
                                if (!silent)
                                {
                                    for (int c = 0; c < useCh; c++)
                                    {
                                        int off = f * block + c * bytesPer;
                                        if (isFloat && bits == 32) sum += BitConverter.ToSingle(buf, off);
                                        else if (bits == 16) sum += BitConverter.ToInt16(buf, off) / 32768f;
                                        else if (bits == 24) sum += ((buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16)) << 8 >> 8) / 8388608f;
                                        else if (bits == 32) sum += BitConverter.ToInt32(buf, off) / 2147483648f;
                                    }
                                    sum /= useCh;
                                }
                                ring[writePos] = sum;
                                writePos = (writePos + 1) % RingSize;
                                if (recording) { rec[recPos++] = sum; if (recPos >= rec.Length) recording = false; }
                            }
                        }
                        Check(cap.ReleaseBuffer(frames));
                        got = true;
                        Check(cap.GetNextPacketSize(out packet));
                    }

                    DateTime now = DateTime.UtcNow;
                    if (got) lastData = now;
                    else if ((now - lastData).TotalMilliseconds > 60) PushSilence(rate / 100);

                    if (wanted.Length == 0 && (now - lastCheck).TotalSeconds > 2)
                    {
                        lastCheck = now;
                        IMMDevice d2;
                        if (en.GetDefaultAudioEndpoint(flow, 0, out d2) >= 0 && d2 != null)
                        {
                            string id2;
                            d2.GetId(out id2);
                            Marshal.ReleaseComObject(d2);
                            if (id2 != devId) return; // default device changed: restart on the new one
                        }
                    }
                }
            }
            finally
            {
                if (client != null) { try { client.Stop(); } catch { } }
                if (fmt != IntPtr.Zero) Marshal.FreeCoTaskMem(fmt);
                if (cap != null) Marshal.ReleaseComObject(cap);
                if (client != null) Marshal.ReleaseComObject(client);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }
    }

    public static class Fft
    {
        // In-place iterative radix-2 FFT. Length must be a power of two.
        public static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double tr = re[i]; re[i] = re[j]; re[j] = tr;
                    double ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                int halfLen = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < halfLen; k++)
                    {
                        int a = i + k, b = a + halfLen;
                        double tr = re[b] * cr - im[b] * ci;
                        double ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
