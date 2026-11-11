# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A one-on-one fighting game for one player against the computer or two at one keyboard, in C# on .NET 10 and
Avalonia 12, drawn with Avalonia's `DrawingContext`. An original game in the genre: its fighters, moves and
stages are its own. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The
fighters are posed skeletons drawn in code, and the sounds and music are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASteelRing.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASteelRing.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVASteelRing          # play it; --no-audio for silence
RING_SHOTS=$PWD/Images dotnet test tests/AVASteelRing.Tests --filter ScreenshotTests   # refresh the README's pictures
python3 tools/make_sounds.py              # rebuild src/AVASteelRing/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASteelRing/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, select, demonstration, play, high scores), the one-player ladder,
    bouts of rounds, scoring and the fixed-step loop.
  - `Fight.cs` is one round, a frame at a time: `Fighter` (position, state, the move under way, stun, health,
    meter, the stick's history), reading special motions, hit and hurt boxes, blocking, stun, knockdowns,
    combos, projectiles, the clock and the KO.
  - `Moves.cs` holds `Move` (timings in frames, a hit box, damage, stun, how it looks) and the `Roster`.
  - `Cpu.cs` is the computer at a fighter's controls: it produces `Controls` a frame at a time, like the keys.
  - `InputHandler.cs` (two sets of keys) and `ControllerManager.cs` (two SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASteelRing/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the
  window, and runs the 60 Hz timer. `GameCanvas.Draw.cs` poses and draws the fighters; `GameCanvas.Screens.cs`
  the stages, the bars and the screens.
- `src/AVASteelRing/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASteelRing.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, and screenshot tests that
  draw the game in a headless window.
- `tools/make_sounds.py` writes the sounds.

## Rules that keep it working

- **Frames.** Everything in a fight counts in 1/60 s frames: `GameEngine.Update` turns frame time into fixed
  steps, and `Fight.Tick` advances both fighters by one. Move timings (startup, active, recovery, stun) are in
  frames. Keep them integer frames.
- **Space.** A fighter's `X, Y` is the middle of their feet; screen y grows downward. Move boxes are given
  relative to the feet for a fighter facing right, before `Fighter.S` scales them. `Facing` flips them.
- **Up means up.** `Controls.Y` is +1 for up (jump) and −1 for down (crouch), and `Controls.X` is screen right.
  Motions are read relative to facing (`Fighter.Dir`: 6 forward, 4 back, 2 down). Test controls through
  `InputHandler`, not by building `Controls` alone.
- **Motions end recently.** `Fight.ReadMotion` only counts a motion whose last direction is in the last few
  frames, and tells the rising move (ending down-forward) from a quarter circle (ending forward). A test walks
  forward before a quarter circle to keep it that way.
- **The computer plays by the same controls.** `Cpu` makes `Controls`, and puts specials in as stick motions,
  so anything a person can't do, it can't either. Its reaction time (`Cpu.Reaction`) comes from its skill.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it; the poses come from the fighter's state and how far through the
  move they are.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** Keys and the gamepad stick must agree, and up must mean up. Test new
  controls through `InputHandler`, not by setting `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVASteelRing`); tests load the table from a fresh temporary folder and run with
  `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
