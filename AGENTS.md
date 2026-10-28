# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game and its controls;
this file covers how the code is put together and the rules that keep it working.

## What this is

A colour vector tank battle in the style of Cinematronics' 1980 arcade game Armor Attack, in C# on .NET 10 and
Avalonia 12, drawn with Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS
and Linux, x64 and ARM). The graphics are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAArmorAttack.slnx                  # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAArmorAttack.slnx                   # every test runs headless: no window opens
dotnet run --project src/AVAArmorAttack           # play it; --no-audio for silence
ARMOR_SHOTS=$PWD/Images dotnet test tests/AVAArmorAttack.Tests --filter ScreenshotTests   # refresh the README's pictures
ARMOR_PROBE=1 dotnet test tests/AVAArmorAttack.Tests --filter PilotProbe   # how the computer player gets on ("lazy" for a dumb driver)
python3 tools/make_sounds.py                      # rebuild src/AVAArmorAttack/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings for
every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAArmorAttack/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` is the fight: the jeep's driving, the tanks hunting along the streets, the gunship,
    shells and collisions.
  - `Entities.cs` has `Controls`, the screen and grid measures (`Field`) and the three city districts (`City`).
  - `JeepPilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAArmorAttack/Views/`: `GameCanvas` (split over partial files) draws the frame and runs the 60 Hz timer.
- `src/AVAArmorAttack/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAArmorAttack/Sounds/`: the generated WAV files. Don't edit them by hand.
- `tests/AVAArmorAttack.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`.
  - `GameplayTests.cs` holds the gameplay tests.
  - `ScreenshotTests.cs` draws the README's pictures.
  - `PilotProbe.cs` is a tuning probe that does nothing unless `ARMOR_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances the game in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`
  (`new Random(seed)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Keys and pads both produce `Controls(Turn, Drive, Swivel, Fire)`.
  - `Turn` is -1 for left and 1 for right.
  - `Drive` is 1 for forward (Up) and -1 for back. On the stick, negative Y is up.
  - Keep keys, D-pad and sticks agreeing on these signs.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData/AVAArmorAttack`). Tests load the table from a fresh temporary folder and set
  `Silent = true`. `Calm = true` keeps the tanks still and quiet for tests that need it.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features and controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site (Harlock123.github.io) to rebuild, if `SITE_REFRESH_TOKEN` is set.
