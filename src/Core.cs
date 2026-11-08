// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ExecPath7. Toneline EQ comes with ABSOLUTELY NO WARRANTY; see LICENSE.txt.
// Toneline EQ - core data, Equalizer APO text format, filter maths, help texts, storage and built-in presets.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TonelineEQ
{
    public enum FilterType { Peak, LowShelf, HighShelf, LowPass, HighPass }
    public enum PageKind { Output, Mic }

    public class Band
    {
        public bool Enabled = true;
        public FilterType Type = FilterType.Peak;
        public double Freq = 1000;
        public double Gain = 0;
        public double Q = 1.0;

        public bool HasGain
        {
            get { return Type == FilterType.Peak || Type == FilterType.LowShelf || Type == FilterType.HighShelf; }
        }

        public Band Clone()
        {
            Band b = new Band();
            b.Enabled = Enabled; b.Type = Type; b.Freq = Freq; b.Gain = Gain; b.Q = Q;
            return b;
        }
    }

    public class Preset
    {
        public string Name = "Untitled";
        public string Group = "user";
        public string Note = "";   // one-line description shown under built-in presets
        public double Preamp = 0;
        public List<Band> Bands = new List<Band>();

        public bool IsUser { get { return Group == "user"; } }

        public Preset Clone()
        {
            Preset p = new Preset();
            p.Name = Name; p.Group = Group; p.Note = Note; p.Preamp = Preamp;
            foreach (Band b in Bands) p.Bands.Add(b.Clone());
            return p;
        }
    }

    // ------------------------------------------------------------------ Equalizer APO text format
    public static class ApoFormat
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        public static string N(double v) { return v.ToString("0.##", IC); }

        public static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // Preset names end up in Equalizer APO's config file and in settings.txt, one per line. A name from a share code,
        // a pasted text or a download could hide a line break followed by a real Equalizer APO command (for example one that
        // loads a plugin DLL). So names lose every control or line-break character and are kept short.
        public static string CleanName(string name)
        {
            if (name == null) return "Untitled";
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == '\u2028' || c == '\u2029' || c == '\u0085') { sb.Append(' '); continue; }
                sb.Append(c);
            }
            string n = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (n.Length > 80) n = n.Substring(0, 80).Trim();
            return n.Length == 0 ? "Untitled" : n;
        }

        public static string TypeName(FilterType t)
        {
            switch (t)
            {
                case FilterType.LowShelf: return "Low shelf";
                case FilterType.HighShelf: return "High shelf";
                case FilterType.LowPass: return "Low pass";
                case FilterType.HighPass: return "High pass";
                default: return "Peak";
            }
        }

        static string Code(FilterType t)
        {
            switch (t)
            {
                case FilterType.LowShelf: return "LSC";
                case FilterType.HighShelf: return "HSC";
                case FilterType.LowPass: return "LPQ";
                case FilterType.HighPass: return "HPQ";
                default: return "PK";
            }
        }

        public static string ToText(Preset p, string header)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header)) sb.Append(header);
            sb.Append("Preamp: ").Append(N(p.Preamp)).Append(" dB\r\n");
            int i = 1;
            foreach (Band b in p.Bands)
            {
                sb.Append("Filter ").Append(i++).Append(": ").Append(b.Enabled ? "ON " : "OFF ").Append(Code(b.Type));
                sb.Append(" Fc ").Append(N(b.Freq)).Append(" Hz");
                if (b.HasGain) sb.Append(" Gain ").Append(N(b.Gain)).Append(" dB");
                sb.Append(" Q ").Append(N(b.Q)).Append("\r\n");
            }
            return sb.ToString();
        }

        static double Num(string s, string pattern)
        {
            Match m = Regex.Match(s, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) return double.NaN;
            double v;
            return double.TryParse(m.Groups[1].Value, NumberStyles.Float, IC, out v) ? v : double.NaN;
        }

        // Reads Equalizer APO / AutoEQ "ParametricEQ.txt" text. Returns null when no filters are found.
        public static Preset Parse(string text, string name)
        {
            Preset p = new Preset();
            p.Name = name;
            bool any = false;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;

                Match pm = Regex.Match(line, @"^Preamp\s*:\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
                if (pm.Success)
                {
                    p.Preamp = double.Parse(pm.Groups[1].Value, IC);
                    any = true;
                    continue;
                }

                Match fm = Regex.Match(line, @"^Filter\s*[0-9]*\s*:\s*(ON|OFF)\s+([A-Za-z]+)\s*(.*)$", RegexOptions.IgnoreCase);
                if (!fm.Success) continue;

                string code = fm.Groups[2].Value.ToUpperInvariant();
                string rest = fm.Groups[3].Value;
                Band b = new Band();
                b.Enabled = fm.Groups[1].Value.ToUpperInvariant() == "ON";
                double fc = Num(rest, @"\bFc\s+([0-9]*\.?[0-9]+)");
                if (double.IsNaN(fc)) continue;
                double gain = Num(rest, @"\bGain\s+([-+]?[0-9]*\.?[0-9]+)");
                double q = Num(rest, @"\bQ\s+([0-9]*\.?[0-9]+)");

                switch (code)
                {
                    case "PK": case "PEQ": case "MODAL": b.Type = FilterType.Peak; break;
                    case "LS": case "LSC": b.Type = FilterType.LowShelf; break;
                    case "HS": case "HSC": b.Type = FilterType.HighShelf; break;
                    case "LP": case "LPQ": b.Type = FilterType.LowPass; break;
                    case "HP": case "HPQ": b.Type = FilterType.HighPass; break;
                    default: continue;
                }
                // Equalizer APO also allows a bandwidth in octaves instead of Q ("BW Oct 1").
                double bw = Num(rest, @"\bBW\s+Oct\s+([0-9]*\.?[0-9]+)");
                if (double.IsNaN(q) && !double.IsNaN(bw) && bw > 0) q = Math.Sqrt(Math.Pow(2, bw)) / (Math.Pow(2, bw) - 1);
                if (double.IsNaN(q)) q = b.Type == FilterType.Peak ? 1.0 : 0.707;
                b.Freq = Clamp(fc, 20, 20000);
                b.Gain = double.IsNaN(gain) ? 0 : Clamp(gain, -24, 24);
                b.Q = Clamp(q, 0.1, 20);
                p.Bands.Add(b);
                any = true;
            }
            return any ? p : null;
        }
    }

    // ------------------------------------------------------------------ Filter maths (RBJ cookbook biquads)
    public static class Dsp
    {
        const double Fs = 48000;

        public static double[] Coeffs(Band b)
        {
            double A = Math.Pow(10, b.Gain / 40.0);
            double w0 = 2 * Math.PI * b.Freq / Fs;
            double cs = Math.Cos(w0), sn = Math.Sin(w0);
            double alpha = sn / (2 * b.Q);
            double sa = 2 * Math.Sqrt(A) * alpha;
            switch (b.Type)
            {
                case FilterType.LowShelf:
                    return new double[] {
                        A * ((A + 1) - (A - 1) * cs + sa), 2 * A * ((A - 1) - (A + 1) * cs), A * ((A + 1) - (A - 1) * cs - sa),
                        (A + 1) + (A - 1) * cs + sa, -2 * ((A - 1) + (A + 1) * cs), (A + 1) + (A - 1) * cs - sa };
                case FilterType.HighShelf:
                    return new double[] {
                        A * ((A + 1) + (A - 1) * cs + sa), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - sa),
                        (A + 1) - (A - 1) * cs + sa, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - sa };
                case FilterType.LowPass:
                    return new double[] { (1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + alpha, -2 * cs, 1 - alpha };
                case FilterType.HighPass:
                    return new double[] { (1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + alpha, -2 * cs, 1 - alpha };
                default:
                    return new double[] { 1 + alpha * A, -2 * cs, 1 - alpha * A, 1 + alpha / A, -2 * cs, 1 - alpha / A };
            }
        }

        public static double MagDb(double[] c, double f)
        {
            double w = 2 * Math.PI * f / Fs;
            double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
            double nr = c[0] + c[1] * c1 + c[2] * c2, ni = -(c[1] * s1 + c[2] * s2);
            double dr = c[3] + c[4] * c1 + c[5] * c2, di = -(c[4] * s1 + c[5] * s2);
            double mag = Math.Sqrt((nr * nr + ni * ni) / (dr * dr + di * di));
            return 20 * Math.Log10(Math.Max(mag, 1e-9));
        }

        // Highest boost of the whole curve (EQ only, without preamp).
        public static double PeakGain(List<Band> bands)
        {
            List<double[]> cs = new List<double[]>();
            foreach (Band b in bands) if (b.Enabled) cs.Add(Coeffs(b));
            double max = 0;
            for (int i = 0; i <= 400; i++)
            {
                double f = Math.Pow(10, Math.Log10(20) + i / 400.0 * 3);
                double t = 0;
                foreach (double[] c in cs) t += MagDb(c, f);
                if (t > max) max = t;
            }
            return max;
        }
    }

    // ------------------------------------------------------------------ Plain-language explanations
    public static class Help
    {
        public static string RegionName(PageKind k, double f)
        {
            if (k == PageKind.Mic)
            {
                if (f < 80) return "Rumble";
                if (f < 250) return "Boom";
                if (f < 500) return "Mud";
                if (f < 2000) return "Body";
                if (f < 5000) return "Presence";
                if (f < 9000) return "Sibilance";
                return "Air";
            }
            if (f < 60) return "Sub-bass";
            if (f < 250) return "Bass";
            if (f < 500) return "Low mids";
            if (f < 2000) return "Mids";
            if (f < 5000) return "Presence";
            if (f < 10000) return "Treble";
            return "Air";
        }

        public static string RegionText(PageKind k, double f)
        {
            if (k == PageKind.Mic)
            {
                if (f < 80) return "Rumble (below 80 Hz): desk bumps, footsteps, fans and electrical hum. Your voice has almost nothing here, so a high pass filter removes it safely.";
                if (f < 250) return "Boom (80-250 Hz): chest and warmth. Gets louder the closer you are to the mic (proximity effect). Too much sounds boomy and muffled.";
                if (f < 500) return "Mud (250-500 Hz): room reflections and boxiness. A small cut here makes most voices clearer.";
                if (f < 2000) return "Body (500 Hz-2 kHz): the core of the voice. Around 1 kHz too much sounds nasal or like a phone call.";
                if (f < 5000) return "Presence (2-5 kHz): how clear and understandable you are. A gentle boost helps you cut through game audio; too much sounds harsh.";
                if (f < 9000) return "Sibilance (5-9 kHz): 's' and 'sh' sounds and mouth clicks. Cut a narrow band here if your s's are sharp.";
                return "Air (9-20 kHz): breath and sparkle. A small lift sounds open; on cheap mics it mostly adds hiss.";
            }
            if (f < 60) return "Sub-bass (20-60 Hz): rumble you feel more than hear, like the weight of a kick drum or a movie explosion. Boost for impact; cut if it sounds bloated.";
            if (f < 250) return "Bass (60-250 Hz): punch and warmth from kick drums and bass guitar. Too much sounds boomy and covers everything else.";
            if (f < 500) return "Low mids (250-500 Hz): body of voices and instruments. Too much is the classic 'muddy' or boxy sound; a small cut cleans things up.";
            if (f < 2000) return "Mids (500 Hz-2 kHz): the core of voices, guitars and pianos. Too much sounds honky or nasal; too little sounds hollow.";
            if (f < 5000) return "Presence (2-5 kHz): clarity and detail, where speech and in-game footsteps live. Ears are most sensitive here, so too much is harsh and tiring.";
            if (f < 10000) return "Treble (5-10 kHz): crispness, cymbals and 's' sounds. Too much is sharp and sibilant.";
            return "Air (10-20 kHz): sparkle and a sense of space. Small changes go a long way; many adults hear little above 16 kHz.";
        }

        public static string[] RegionLabels(PageKind k)
        {
            return k == PageKind.Mic
                ? new string[] { "RUMBLE", "BOOM", "MUD", "BODY", "PRESENCE", "SIBILANCE", "AIR" }
                : new string[] { "SUB", "BASS", "LOW MIDS", "MIDS", "PRESENCE", "TREBLE", "AIR" };
        }

        public static double[] RegionEdges(PageKind k)
        {
            return k == PageKind.Mic
                ? new double[] { 20, 80, 250, 500, 2000, 5000, 9000, 20000 }
                : new double[] { 20, 60, 250, 500, 2000, 5000, 10000, 20000 };
        }

        public static string TypeText(FilterType t)
        {
            switch (t)
            {
                case FilterType.LowShelf: return "Low shelf: raises or lowers everything BELOW its frequency by the same amount, with a smooth slope. The cleanest way to add or remove bass.";
                case FilterType.HighShelf: return "High shelf: raises or lowers everything ABOVE its frequency. The cleanest way to make the sound brighter or darker.";
                case FilterType.LowPass: return "Low pass: lets the lows through and removes everything above its frequency. Use it to cut hiss or very harsh highs. Gain does nothing here.";
                case FilterType.HighPass: return "High pass: removes everything below its frequency. On a mic it removes rumble and hum; on speakers it protects them from deep bass. Gain does nothing here.";
                default: return "Peak (bell): boosts or cuts a band around its frequency, shaped like a hill or a dip. Q sets how wide it is. The most common EQ tool.";
            }
        }

        // Width of a bell filter in octaves for a given Q.
        public static double Octaves(double q)
        {
            double x = 1 / (2 * q);
            return 2 / Math.Log(2) * Math.Log(x + Math.Sqrt(x * x + 1));
        }

        public const string Freq = "Frequency: the pitch this band works on, in hertz (Hz). 1 kHz = 1000 Hz. Low numbers are bass, high numbers are treble. You can type 2.5k for 2500 Hz.";
        public const string Gain = "Gain: how much louder (+) or quieter (-) this band makes its frequencies, in decibels (dB). +3 dB is a clear but gentle change, +6 dB is a big one. Cutting is usually cleaner than boosting.";
        public const string Q = "Q: how wide the band is. Low Q (0.5-0.7) is wide and gentle, about 1.4 covers one octave, and high Q (4-10) is a narrow notch for one harsh or boomy spot.";
        public const string Preamp = "Preamp: turns the whole signal down before the EQ. Every boost needs the same amount of headroom, or loud parts will distort (clip). Press Auto to set it for you.";
    }

    // ------------------------------------------------------------------ Effects (plugins) in the chain
    public class FxItem
    {
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        public string Kind = "gain";   // gain, balance, mono, swap, conv, vst, custom
        public bool Enabled = true;
        public bool BeforeEq = false;
        public double Value = 0;
        public string Path = "";
        public string Params = "";

        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case "gain": return "Gain";
                    case "balance": return "Balance";
                    case "mono": return "Mono";
                    case "swap": return "Swap left and right";
                    case "conv": return "Impulse response";
                    case "vst": return "VST plugin";
                    default: return "Custom Equalizer APO lines";
                }
            }
        }

        public string Explain
        {
            get
            {
                switch (Kind)
                {
                    case "gain": return "Raises or lowers the volume of this whole chain, in dB. Use a negative value to make room before a boosting plugin.";
                    case "balance": return "Shifts the sound left (negative) or right (positive) by turning the other side down.";
                    case "mono": return "Mixes left and right together so both ears hear the same thing. Handy with one earbud, or for checking a mix.";
                    case "swap": return "Swaps the left and right channels, for headphones worn backwards or wired the wrong way.";
                    case "conv": return "Applies an impulse response (.wav): room simulation, speaker or headphone correction, or surround virtualizers made for Equalizer APO. The file's sample rate must match your device's (e.g. 48 kHz), or Equalizer APO skips it.";
                    case "vst": return "Runs a VST2 plugin (.dll), for example a noise gate, compressor or limiter. Use 64-bit plugins on 64-bit Windows. Optional settings: paste the ChunkData part of a VSTPlugin line from Equalizer APO's Editor.";
                    default: return "Any Equalizer APO commands, one per line. For experts: anything the Equalizer APO documentation describes works here.";
                }
            }
        }

        static string Db(double v) { return v.ToString("0.##", IC); }

        public string ToApo()
        {
            switch (Kind)
            {
                case "gain":
                    return "Preamp: " + Db(Value) + " dB";
                case "balance":
                    if (Math.Abs(Value) < 0.5) return "";
                    {
                        string side = Value > 0 ? "L" : "R";
                        double g = 20 * Math.Log10(Math.Max(0.001, 1 - Math.Abs(Value) / 100.0));
                        return "Channel: " + side + "\r\nPreamp: " + Db(g) + " dB\r\nChannel: all";
                    }
                case "mono":
                    return "Copy: L=0.5*L+0.5*R R=0.5*L+0.5*R";
                case "swap":
                    return "Copy: L=R R=L";
                case "conv":
                    return Path.Length > 0 && System.IO.File.Exists(Path) ? "Convolution: " + Path : "";
                case "vst":
                    if (Path.Length == 0 || !System.IO.File.Exists(Path)) return "";
                    return "VSTPlugin: Library \"" + Path + "\"" + (Params.Trim().Length > 0 ? " " + Params.Trim() : "");
                default:
                    return Params ?? "";
            }
        }

        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        static string UnB64(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }

        public static string Serialize(List<FxItem> items)
        {
            StringBuilder sb = new StringBuilder();
            foreach (FxItem f in items)
                sb.Append(f.Kind).Append('\t').Append(f.Enabled ? "1" : "0").Append('\t').Append(f.BeforeEq ? "1" : "0").Append('\t')
                  .Append(f.Value.ToString(IC)).Append('\t').Append(B64(f.Path)).Append('\t').Append(B64(f.Params)).Append("\r\n");
            return sb.ToString();
        }

        public static List<FxItem> ParseList(string text)
        {
            List<FxItem> list = new List<FxItem>();
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                string[] p = line.Split('\t');
                if (p.Length < 6) continue;
                FxItem f = new FxItem();
                f.Kind = p[0];
                f.Enabled = p[1] == "1";
                f.BeforeEq = p[2] == "1";
                double v;
                if (double.TryParse(p[3], NumberStyles.Float, IC, out v)) f.Value = v;
                f.Path = UnB64(p[4]);
                f.Params = UnB64(p[5]);
                list.Add(f);
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ Local storage (%APPDATA%\TonelineEQ)
    public static class Store
    {
        public static string DirOverride; // self-test uses its own folder so real settings are never touched

        public static string Dir
        {
            get
            {
                string d = DirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TonelineEQ");
                if (DirOverride == null && !Directory.Exists(d))
                    MigrateFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyEQ"), d);
                Directory.CreateDirectory(d);
                return d;
            }
        }

        // First run after the rename: copy presets and settings from the EasyEQ folder (the old folder stays as a backup).
        public static bool MigrateFolder(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from) || Directory.Exists(to)) return false;
                foreach (string src in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    string rel = src.Substring(from.Length).TrimStart('\\', '/');
                    if (rel.Equals("error.log", StringComparison.OrdinalIgnoreCase)) continue;
                    string dst = Path.Combine(to, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, false);
                }
                // A background picture lives in the settings folder: point the setting at the copy.
                string st = Path.Combine(to, "settings.txt");
                if (File.Exists(st))
                {
                    string text = File.ReadAllText(st);
                    string fixedText = text.Replace(from + "\\", to + "\\");
                    if (fixedText != text) File.WriteAllText(st, fixedText);
                }
                return true;
            }
            catch { return false; }
        }

        public static string PresetDir(PageKind k)
        {
            string d = Path.Combine(Dir, "Presets");
            if (k == PageKind.Mic) d = Path.Combine(d, "Mic");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string ErrorLog { get { return Path.Combine(Dir, "error.log"); } }

        public static void LogError(Exception ex)
        {
            try { File.AppendAllText(ErrorLog, DateTime.Now.ToString("s") + "  " + ex + "\r\n\r\n"); } catch { }
        }

        public static string ReadText(string name)
        {
            try
            {
                string f = Path.Combine(Dir, name);
                return File.Exists(f) ? File.ReadAllText(f) : null;
            }
            catch { return null; }
        }

        public static void WriteText(string name, string text)
        {
            try { File.WriteAllText(Path.Combine(Dir, name), text); } catch (Exception ex) { LogError(ex); }
        }

        public static Dictionary<string, string> LoadSettings()
        {
            Dictionary<string, string> s = new Dictionary<string, string>();
            string t = ReadText("settings.txt");
            if (t != null)
                foreach (string l in t.Replace("\r", "").Split('\n'))
                {
                    int i = l.IndexOf('=');
                    if (i > 0) s[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                }
            return s;
        }

        public static void SaveSettings(Dictionary<string, string> s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in s) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
            WriteText("settings.txt", sb.ToString());
        }

        public static string SafeName(string n)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Trim();
        }

        public static List<Preset> LoadUserPresets(PageKind k)
        {
            List<Preset> list = new List<Preset>();
            try
            {
                foreach (string f in Directory.GetFiles(PresetDir(k), "*.txt").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        Preset p = ApoFormat.Parse(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f));
                        if (p != null) { p.Group = "user"; list.Add(p); }
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }
    }

    // ------------------------------------------------------------------ Built-in presets
    // Share codes: a preset packed into one short line ("Toneline:...", or "EasyEQ:..." from before the rename) that anyone can paste into Toneline EQ.
    public static class ShareCode
    {
        const string Prefix = "Toneline:";

        public static string Encode(Preset p)
        {
            byte[] raw = System.Text.Encoding.UTF8.GetBytes((p.Name ?? "Shared EQ").Replace("\n", " ") + "\n" + ApoFormat.ToText(p, null));
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true))
                    ds.Write(raw, 0, raw.Length);
                return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        // Finds a share code anywhere in the text (a chat message, a forum post). Null when there isn't one.
        public static Preset Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Match m = Regex.Match(text, @"(?:Toneline|EasyEQ):([A-Za-z0-9_\-]{8,})");
            if (!m.Success) return null;
            try
            {
                string b = m.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
                using (System.IO.MemoryStream src = new System.IO.MemoryStream(Convert.FromBase64String(b)))
                using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(src, System.IO.Compression.CompressionMode.Decompress))
                using (System.IO.StreamReader r = new System.IO.StreamReader(ds, System.Text.Encoding.UTF8))
                {
                    string all = r.ReadToEnd();
                    if (all.Length > 200000) return null;
                    int nl = all.IndexOf('\n');
                    if (nl < 0) return null;
                    string name = ApoFormat.CleanName(all.Substring(0, nl));
                    if (name == "Untitled") name = "Shared EQ";
                    return ApoFormat.Parse(all.Substring(nl + 1), name);
                }
            }
            catch { return null; }
        }
    }

    public static class BuiltIns
    {
        static Preset Make(string name, string group, string note, string text)
        {
            Preset p = ApoFormat.Parse(text, name);
            p.Group = group;
            p.Note = note;
            return p;
        }

        // Builds Equalizer APO text from short band specs like "LSC 105 6 0.7", "PK 300 -1 1", "HPQ 80 0.7".
        static string Bands(double preamp, params string[] specs)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Preamp: ").Append(ApoFormat.N(preamp)).Append(" dB\n");
            foreach (string spec in specs)
            {
                string[] t = spec.Split(' ');
                if (t.Length == 3) sb.Append("Filter: ON ").Append(t[0]).Append(" Fc ").Append(t[1]).Append(" Hz Q ").Append(t[2]).Append('\n');
                else sb.Append("Filter: ON ").Append(t[0]).Append(" Fc ").Append(t[1]).Append(" Hz Gain ").Append(t[2]).Append(" dB Q ").Append(t[3]).Append('\n');
            }
            return sb.ToString();
        }

        public static string GroupTitle(string group)
        {
            switch (group)
            {
                case "basic": return "Basics";
                case "music": return "Music";
                case "media": return "Games and movies";
                case "speakers": return "Speakers";
                case "micbasic": return "Voice";
                case "micfix": return "Fix a problem";
                case "mictype": return "Mic type (starting points)";
                default: return "Saved";
            }
        }

        public const string SimpleName = "Simple (Low / Mid / High)";

        // A simple EQ is exactly low shelf + bell + high shelf: Low / Mid / High.
        public static bool IsSimple(Preset p)
        {
            return p != null && p.Bands.Count == 3 && p.Bands[0].Type == FilterType.LowShelf && p.Bands[1].Type == FilterType.Peak && p.Bands[2].Type == FilterType.HighShelf;
        }

        public static List<Preset> All(PageKind k)
        {
            return k == PageKind.Mic ? Mic() : Output();
        }

        static List<Preset> Output()
        {
            List<Preset> l = new List<Preset>();
            l.Add(Make("Flat (10 bands)", "basic", "No change, a clean start",
                Bands(0, "PK 31 0 1.41", "PK 62 0 1.41", "PK 125 0 1.41", "PK 250 0 1.41", "PK 500 0 1.41",
                         "PK 1000 0 1.41", "PK 2000 0 1.41", "PK 4000 0 1.41", "PK 8000 0 1.41", "PK 16000 0 1.41")));
            l.Add(Make(SimpleName, "basic", "The easiest way to tune", Bands(0, "LSC 105 0 0.7", "PK 630 0 0.7", "HSC 5000 0 0.7")));
            l.Add(Make("Bass boost", "basic", "More punch and rumble", Bands(-6, "LSC 105 6 0.7", "PK 300 -1 1")));
            l.Add(Make("Treble boost", "basic", "Brighter, with more detail", Bands(-5, "PK 3000 1 1", "HSC 6000 4.5 0.7")));
            l.Add(Make("Vocal clarity", "basic", "Voices step forward", Bands(-3.5, "PK 250 -2 1", "PK 2800 3 1.1", "HSC 10000 1.5 0.7")));
            l.Add(Make("V-shape", "basic", "Deep bass and crisp highs", Bands(-5.5, "LSC 100 5 0.7", "PK 1000 -2 0.8", "HSC 8000 4 0.7")));
            l.Add(Make("Warm and relaxed", "basic", "Smooth, never harsh", Bands(-3, "LSC 120 3 0.7", "PK 3200 -2 1.4", "HSC 8000 -3 0.7")));
            l.Add(Make("Less bass", "basic", "Tames boomy headphones", Bands(0, "LSC 120 -5 0.7", "PK 250 -1 1")));
            l.Add(Make("Less treble", "basic", "Tames harsh headphones", Bands(0, "PK 3500 -1.5 1.5", "HSC 5000 -5 0.7")));
            l.Add(Make("Loudness (low volume)", "basic", "Fuller when playing quietly", Bands(-6, "LSC 80 6 0.7", "PK 1000 -1 0.7", "HSC 10000 4 0.7")));

            l.Add(Make("Pop", "music", "Punchy and bright", Bands(-3, "LSC 100 2.5 0.7", "PK 1500 -1 0.8", "PK 3500 2 1.2", "HSC 10000 2 0.7")));
            l.Add(Make("Rock", "music", "Guitars and drums bite", Bands(-4, "LSC 100 4 0.7", "PK 400 -2 1", "PK 2500 2.5 1", "HSC 9000 3 0.7")));
            l.Add(Make("Hip-hop and R&B", "music", "Deep 808s, clear vocals", Bands(-6, "LSC 70 6 0.7", "PK 300 -1.5 1", "PK 3000 1.5 1.2")));
            l.Add(Make("Electronic", "music", "Big sub-bass and sparkle", Bands(-5.5, "LSC 60 5.5 0.7", "PK 600 -2 0.8", "HSC 9000 4 0.7")));
            l.Add(Make("Acoustic", "music", "Natural and warm", Bands(-2.5, "LSC 150 1.5 0.7", "PK 400 -1 1", "PK 3000 2 1", "HSC 12000 2 0.7")));
            l.Add(Make("Classical", "music", "Wide and airy", Bands(-3, "LSC 100 2 0.7", "PK 2000 -1 1", "HSC 10000 3 0.7")));
            l.Add(Make("Jazz", "music", "Smooth bass, rich middle", Bands(-3, "LSC 100 3 0.7", "PK 800 1.5 1", "PK 4000 -1 1.5", "HSC 10000 1.5 0.7")));

            l.Add(Make("Footsteps (FPS games)", "media", "Hear steps earlier", Bands(-5, "LSC 120 -4 0.7", "PK 2500 4 1.2", "PK 5000 3 1.5", "HSC 9000 -2 0.7")));
            l.Add(Make("Immersive games", "media", "Big booms, clear details", Bands(-5.5, "LSC 60 5 0.7", "PK 400 -1.5 1", "PK 3000 2 1", "HSC 10000 3 0.7")));
            l.Add(Make("Movies", "media", "Cinema bass, clear voices", Bands(-4.5, "LSC 60 4 0.7", "PK 2000 2 0.8", "HSC 10000 1.5 0.7")));
            l.Add(Make("Dialogue and podcasts", "media", "Voices clear at any volume", Bands(-3, "LSC 150 -3 0.7", "PK 2500 3 1", "PK 6500 -1.5 2")));
            l.Add(Make("Night (low fatigue)", "media", "Softer for long sessions", Bands(0, "LSC 80 -2 0.7", "PK 3500 -2 2", "HSC 6000 -4 0.7")));

            l.Add(Make("Laptop speakers", "speakers", "More body, less tinny", Bands(-3.5, "HPQ 100 0.7", "PK 200 3 1.2", "PK 3000 1.5 1", "PK 8000 -1 2")));
            l.Add(Make("Desktop speakers", "speakers", "Tighter bass, smoother top", Bands(-2, "PK 100 2 1", "PK 250 -1.5 1", "HSC 8000 -1.5 0.7")));
            return l;
        }

        static List<Preset> Mic()
        {
            List<Preset> l = new List<Preset>();
            l.Add(Make("Flat (6 bands)", "micbasic", "No change, a clean start",
                Bands(0, "PK 100 0 1", "PK 250 0 1", "PK 500 0 1", "PK 1000 0 1", "PK 3000 0 1", "PK 8000 0 1")));
            l.Add(Make(SimpleName, "micbasic", "The easiest way to tune", Bands(0, "LSC 150 0 0.7", "PK 1000 0 0.7", "HSC 6000 0 0.7")));
            l.Add(Make("Clean voice", "micbasic", "Clear and natural", Bands(-2.5, "HPQ 80 0.7", "PK 250 -2 1.2", "PK 3500 2.5 1", "HSC 10000 1.5 0.7")));
            l.Add(Make("Warm broadcast", "micbasic", "Radio warmth with presence", Bands(-3, "HPQ 60 0.7", "LSC 150 3 0.7", "PK 400 -2 1.2", "PK 4000 2 1.2", "PK 7000 -1.5 3")));
            l.Add(Make("Podcast", "micbasic", "Rich, close and full", Bands(-3, "HPQ 70 0.7", "LSC 120 2.5 0.7", "PK 300 -2 1", "PK 5000 2.5 1")));
            l.Add(Make("Streaming", "micbasic", "Polished voice for OBS", Bands(-2.5, "HPQ 80 0.7", "PK 200 -1 1", "PK 3000 2 1", "PK 7000 -2 4", "HSC 10000 2 0.7")));
            l.Add(Make("Game chat / Discord", "micbasic", "Cuts through game noise", Bands(-3, "HPQ 120 0.7", "PK 300 -2 1", "PK 2500 3 1.2", "LPQ 9000 0.7")));
            l.Add(Make("Brighter voice", "micbasic", "For dull or muffled mics", Bands(-4, "HPQ 80 0.7", "PK 3000 3 1", "HSC 8000 4 0.7")));
            l.Add(Make("Fuller voice", "micbasic", "Adds body to a thin voice", Bands(-3, "HPQ 60 0.7", "PK 180 3 1", "PK 3000 -1 1.5")));

            l.Add(Make("Less boom (close to the mic)", "micfix", "No bass build-up up close", Bands(-1.5, "HPQ 90 0.7", "LSC 200 -4 0.7", "PK 3000 1.5 1")));
            l.Add(Make("Softer 's' sounds", "micfix", "Tames sharp, hissy esses", Bands(0, "PK 6500 -4 3", "PK 8500 -2 2.5")));
            l.Add(Make("Noisy room", "micfix", "Trims rumble and hiss", Bands(-2, "HPQ 150 0.7", "PK 2500 2 1", "LPQ 8000 0.7")));
            l.Add(Make("Remove hum (50 Hz)", "micfix", "Europe, Asia, Africa, Australia", Bands(0, "PK 50 -15 8", "PK 100 -10 8", "PK 150 -6 8")));
            l.Add(Make("Remove hum (60 Hz)", "micfix", "The Americas", Bands(0, "PK 60 -15 8", "PK 120 -10 8", "PK 180 -6 8")));

            l.Add(Make("Dynamic mic", "mictype", "USB and XLR dynamics", Bands(-3, "HPQ 80 0.7", "PK 200 -1.5 1", "PK 4000 3 1", "HSC 9000 2 0.7")));
            l.Add(Make("Condenser mic", "mictype", "USB condenser mics", Bands(-1, "HPQ 100 0.7", "PK 250 -2 1.2", "PK 5000 1 1", "PK 7500 -2 3")));
            l.Add(Make("Headset mic", "mictype", "Gaming headsets", Bands(-2, "HPQ 120 0.7", "PK 1000 -1 1", "PK 3000 2 1.2", "LPQ 10000 0.7")));
            l.Add(Make("Laptop or webcam mic", "mictype", "Built-in mics", Bands(-3, "HPQ 150 0.7", "PK 400 -2 1", "PK 2500 3 1.2", "LPQ 9000 0.7")));
            return l;
        }
    }
}
