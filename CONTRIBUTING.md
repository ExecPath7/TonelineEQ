# Contributing

Bug reports, ideas and pull requests are welcome.

- **Bugs:** open an issue with your Windows version, what you did, and what happened. Attach `%APPDATA%\TonelineEQ\error.log` if there is one (read it first, it's plain text).
- **Pull requests:** keep the code C# 5 (no `$""`, no `?.`, no expression-bodied members). Run `build.cmd` and `out\TonelineEQ.exe --selftest test`, and include the result line from `test\selftest.txt` (it must say 0 failed).
- **License of contributions:** by sending a pull request, you agree that your contribution is licensed under the GNU GPL v3.0 or later, like the rest of Toneline EQ, and that you have the right to submit it. Don't submit code, fonts, images or presets copied from somewhere that doesn't allow it.
- **Forks:** under the GPL you may fork and redistribute Toneline EQ, including changed versions, as long as you keep the license and copyright notices and publish your source. Please give a changed version a different name and icon, so people can tell it isn't the official Toneline EQ.

## Building from source

There's no Visual Studio project. Toneline EQ builds with the .NET Framework compiler that ships with Windows:

```cmd
build.cmd
```

That writes `out\TonelineEQ.exe`. To run the built-in self-test, which exercises every page and window and saves screenshots:

```cmd
out\TonelineEQ.exe --selftest test
```

The results are in `test\selftest.txt`. The self-test uses its own data folder and never touches Equalizer APO. It also saves the pictures used on the home page and in the install guide to `test\docs` (they use a made-up song and a made-up signal, so nothing from your PC shows up); copy them to `docs\images` if the look changed.

Code notes:
- The code is C# 5 (no `$""` strings, no `?.`, no expression-bodied members), compiled with `csc /codepage:65001`.
- The UI is WPF built in code. Control styles are a XAML string in `src/Theme.cs`.
- `.cmd` files must keep CRLF line endings (enforced by `.gitattributes`).

| File | What it does |
|---|---|
| `Program.cs` | Entry point, version, single instance |
| `MainWindow.cs` | Window, header, tray, shortcuts |
| `EqPage.cs` | One EQ page: presets, graph, bands, effects |
| `Graph.cs` | EQ curve and spectrum visualizer |
| `Motion.cs` | Smooth scrolling and app-wide animations |
| `Theme.cs` | Colors, fonts and control styles |
| `ColorWheel.cs` | The color wheel in Settings → Appearance |
| `Glass.cs` | Windows 11 Mica and the unified title bar |
| `Audio.cs` | Audio capture, the visualizer feed, Test my mic |
| `Core.cs` | Presets, Equalizer APO format, built-in presets |
| `Features.cs` | AutoEQ search, share codes, on-screen display |
| `NowPlaying.cs` | Windows media session (Now Playing) |
| `Windows.cs` | Settings and Guide windows |
| `Installer.cs` | Install, update and uninstall |
| `SelfTest.cs` | `--selftest` |
