# FLPTools

A launcher and toolkit for FL Studio on Windows. It finds every FL Studio version you have installed, lets you make launch configs for each one, and comes with tools for working on `.flp` project files and mods that change FL itself.

Not affiliated with or endorsed by Image-Line. FL Studio is a trademark of Image-Line Software.

## Launcher

- Console-style home screen: one tile per launch config, a big Start button, and your recent projects for that version underneath.
- Animated backgrounds (waves, particles, flow, orbs, aurora) tinted with each config's colour, or any picture, including the wallpapers that ship with FL.
- Launch configs per FL version: project to open, startup template, theme, FL background picture, full screen, process priority, CPU cores, extra command-line arguments, startup animation, and a free-text tag.
- Settings a config changes in FL (template, theme, background, full screen, startup animation) are written just before FL starts and put back when it closes.
- Play time and last played per config.
- Shows whether each install's main program files still carry Image-Line's trusted code signature.
- Keyboard, mouse and gamepad navigation. Custom title bar, F11 for full screen.

## Project tools

- **Project editor:** title, artists, genre, tempo, comments; channel, pattern and mixer track names and colours; find missing samples, collect samples next to the project, change sample paths; transpose, quantize and scale notes; export the song or each pattern as MIDI; install a project as an FL template.
- **FL 26 to FL 25:** rewrites an FL Studio 2026 project so FL Studio 2025 can open it (drops FL 26-only data, converts playlist clips, rebuilds the mixer layout). Can also run on a whole folder.
- **FLP to JSON and back:** decompiles every event in a project into readable JSON (notes, playlist clips and mixer parameters as tables) and rebuilds a byte-identical `.flp` from it.
- Projects saved by an unregistered copy of FL are locked by FL; FLPTools detects these and leaves them alone.

## Mods

- **Companion** (experimental): a controller script that runs inside FL. It colours channels, mixer tracks and playlist tracks from words in their names, names empty mixer tracks after the channel routed to them, can autosave, and shows session time in FL's hint bar. Attach it once in FL under Options, MIDI settings, by choosing "FLPTools Companion" as the controller type for a MIDI input.
- **Window animations** (experimental): FL's windows fade in as they open and floating ones rise into place. Done from outside FL while FLPTools is running; FL's files are not changed.
- **Startup animations:** seven animations (Bloom, Waveform, Equalizer, Neon, Assemble, Pulse, Glitch) in a colour you pick, played by FL on its loading screen and rendered from your FL icon.
- **Piano roll scripts:** Scale Snap, Chordify, Strum, Hi-Hat Rolls, Velocity Shaper, Legato and Gaps, Reverse and Mirror, Quantize Plus, Melody Generator, 808 Glide, Octave Doubler, Thin Out, Bassline From Chords, Colorize Notes, Duplicate and Vary.
- **Edison scripts:** Fade, Tape Stop, Stutter, Saturator, Stereo Width, Downsampler, Gain To Peak.
- **Themes:** a generator for FL themes and note colour presets, with a preview.

## Requirements

- Windows 10 or 11
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (already installed on most Windows 10/11 PCs)
- To build: [.NET 9 SDK](https://dotnet.microsoft.com/download)

## Build and run

```
git clone https://github.com/odinong/FLPTools.git
cd FLPTools
dotnet build -c Release
bin\Release\net9.0-windows\FLPTools.exe
```

## Command line

```
FLPTools decompile <in.flp> <out.json>
FLPTools recompile <in.json> <out.flp>
FLPTools convert <in.flp> <out.flp> [version]
FLPTools verify <file.flp>...
```

## Where things go

- Launcher settings and configs: `%APPDATA%\FLPTools\launcher.json`
- Scripts, themes and animations are installed into FL's user data folder (`Documents\Image-Line\FL Studio\Settings`), and can be removed from the Mods page.
- Launch configs write to FL's per-version registry settings under `HKEY_CURRENT_USER\Software\Image-Line` and restore them when FL closes.

## License

MIT
