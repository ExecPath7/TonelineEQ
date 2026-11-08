Toneline EQ 0.5 - a free, system-wide equalizer for Windows
======================================================

Two equalizers in one small app:
  - Headphones: what you hear (headphones, IEMs, speakers)
  - Microphone: how you sound (Discord, games, OBS, recordings)
Toneline EQ is the app you tune in. Equalizer APO (free) applies the EQ inside
Windows. You need both.

INSTALL
1. Install Equalizer APO: https://sourceforge.net/projects/equalizerapo/
   In its Configurator tick your playback devices (headphones, DAC/dongle,
   speakers) and, on the Capture devices tab, your microphone. Restart Windows.
2. Double-click "Install Toneline EQ.cmd". If Windows warns, choose
   More info > Run anyway. It builds Toneline EQ with the compiler that comes with
   Windows and opens the installer:
     - installs for your account only (no admin): %LOCALAPPDATA%\Programs\TonelineEQ
     - Start menu / desktop shortcuts, start with Windows (your choice)
     - appears in Windows Settings > Apps, where you can uninstall it
   Updating from an older Toneline EQ, or from EasyEQ (its old name)?
   Just run it; presets and settings are kept.
   Afterwards you can delete this folder.
3. In Toneline EQ press "Connect" in the banner the first time.

USING IT
  - Switch between Headphones and Microphone at the top.
  - Drag dots on the graph, scroll over a dot for width (Q), double-click to
    add a band, Delete removes it, Ctrl+Z / Ctrl+Y undo and redo.
  - Find my headphones (AutoEQ): search measured headphones and load a
    correction in one click.
  - Link to <device>: that preset loads automatically whenever the device
    becomes the Windows default.
  - Effects & plugins tab: gain, balance, mono, swap, impulse responses,
    VST2 plugins, custom Equalizer APO lines.
  - Shortcuts (work in games): Ctrl+Alt+E headphone EQ on/off,
    Ctrl+Alt+M mic EQ on/off, Ctrl+Alt+PageDown/PageUp next/previous preset.
  - Guide and Settings are top right; hover over anything for help.
  - Simple tab: just three sliders, Low / Mid / High, plus Quick picks.
  - 40+ built-in presets: basics, music, games and movies, speakers, and
    voice styles, fixes and mic types for the microphone.
  - Now Playing: the song from Spotify, a browser or any player, with artwork
    and play / pause / skip, above the EQ (Windows 10 1809 or later).
  - Windows 11: the window uses the translucent Mica material, with Toneline EQ's
    header as the title bar (Settings > Appearance > Translucent window).
  - Ctrl+, opens Settings, Ctrl+Z / Ctrl+Y undo and redo.
  - Settings > Appearance: a Default theme (how Toneline EQ comes) and built-in
    themes (Blue, Graphite, Purple, Pink, Orange, Green, Gold, Light), plus a
    color wheel for the accent, background, text, glow and the live-audio
    colors (five height points, floor to peak). Everything has a Reset.
  - Closing the window keeps Toneline EQ in the tray, using very little memory.

WHERE THINGS ARE
  - Presets and settings: %APPDATA%\TonelineEQ
  - Errors (if any): %APPDATA%\TonelineEQ\error.log
  - Equalizer APO's file: C:\Program Files\EqualizerAPO\config\TonelineEQ.txt

SAFETY
  EQ boosts make sound louder. Start with the volume low after changing a
  preset, and keep the preamp negative (or press Auto) to avoid clipping.

LICENSE AND LEGAL
  Copyright (C) 2026 ExecPath7. Toneline EQ is free software under the GNU
  General Public License v3.0 or later (LICENSE.txt). It comes with
  ABSOLUTELY NO WARRANTY; you use it at your own risk.
  Third-party notices: THIRD-PARTY-NOTICES.txt. Privacy: PRIVACY.txt
  (Toneline EQ collects no data). Toneline EQ is not affiliated with Equalizer APO,
  AutoEQ, Apple, Microsoft or any headphone brand; their names belong to
  their owners.
