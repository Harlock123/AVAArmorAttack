using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAArmorAttack.Engine;
using AVAArmorAttack.Views;
using Controls = AVAArmorAttack.Engine.Controls;

namespace AVAArmorAttack.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ARMOR_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaarmorattack-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ARMOR_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAArmorAttack-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 61850, 9);
        g.HighScores.Insert("AVA", 44200, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 90)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Jeep.Alive && g.Jeep.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_old_town() => Fight(1, "old-town", g => g.Wave == 1 && g.Tanks.Count(t => t.X > 60 && t.X < 960) >= 2 && g.Shells.Count >= 2, seed: 1);

    [AvaloniaFact]
    public void The_docks() => Fight(2, "docks", g => g.Tanks.Any(t => t.Damaged) && g.Wrecks.Count >= 1 && g.Shells.Count >= 2);

    [AvaloniaFact]
    public void The_gunship() => Fight(3, "gunship", g => g.Gunship is { Leaving: false } u && u.X > 120 && u.X < 900 && g.Shells.Any(s => s.Rocket), seconds: 120);

    [AvaloniaFact]
    public void A_tank_destroyed() => Fight(4, "wreck", g => g.Wrecks.Any(w => w.Age < 0.4) && g.Wreckage.Count > 8 && g.Tanks.Count >= 2, seconds: 120);

    [AvaloniaFact]
    public void A_district_cleared()
    {
        var (w, c, g) = Open(4);
        g.Go(1);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.5, 120));
        Shot(w, c, "cleared");
    }
}
