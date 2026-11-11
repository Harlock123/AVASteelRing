using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASteelRing.Engine;
using AVASteelRing.Views;
using Controls = AVASteelRing.Engine.Controls;

namespace AVASteelRing.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set RING_SHOTS to a folder to save them
/// as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 5)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaring-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("RING_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASteelRing-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer fight on, both sides, until the moment comes.</summary>
    private static bool Until(GameEngine g, GameCanvas c, Func<GameEngine, bool> moment, int seconds = 300)
    {
        for (int i = 0; i < 60 * seconds; i++)
        {
            if (moment(g)) return true;
            g.Tick();
            if (i % 3 == 0) c.Advance(0.05);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_select_screen()
    {
        var (w, c, g) = Open();
        g.Run(20);
        c.Advance(4.25);
        Shot(w, c, "title");
        g.OpenSelect();
        g.Adjust(1, 2);
        c.Advance(0.3);
        Shot(w, c, "select");
    }

    [AvaloniaFact]
    public void The_versus_card()
    {
        var (w, c, g) = Open();
        g.OpenSelect();
        g.Adjust(1, 1);
        g.StartGame();
        g.Run(40);
        Shot(w, c, "versus");
    }

    [AvaloniaFact]
    public void A_hit_landing()
    {
        var (w, c, g) = Open(7);
        g.Go(Roster.Kai, Roster.Bruna, Mode.Arcade, Skill.Hard);
        g.ComputerPlaysOne(0.8);
        Assert.True(Until(g, c, g => g.Fight is { } f && g.Phase == Phase.Fight && f.Sparks.Any(s => !s.Blocked && f.Frames - s.Born == 2) && f.P1.Health < 900 && f.P2.Health < 900));
        Shot(w, c, "hit");
    }

    [AvaloniaFact]
    public void A_fireball_in_flight()
    {
        var (w, c, g) = Open(4);
        g.Go(Roster.Orla, Roster.Zen, Mode.Arcade, Skill.Normal);
        g.ComputerPlaysOne(0.7);
        Assert.True(Until(g, c, g => g.Fight is { } f && f.Projectiles.Any(p => p.Age == 6)));
        Shot(w, c, "fireball");
    }

    [AvaloniaFact]
    public void A_super()
    {
        var (w, c, g) = Open(8);
        g.Go(Roster.Kai, Roster.Orla, Mode.Arcade, Skill.Normal);
        var f = g.Fight!;
        Fight.Place(f.P1, 300);
        Fight.Place(f.P2, 720);
        f.P1.Meter = Fighter.MaxMeter;
        var seq = new[] { new Controls(0, -1), new Controls(1, -1), new Controls(1, 0), new Controls(0, -1), new Controls(1, -1), new Controls(1, 0, Buttons.HeavyPunch) };
        foreach (var k in seq) { g.Tick(k); g.Tick(k); }
        for (int i = 0; i < 40; i++) g.Tick();
        c.Advance(0.2);
        Shot(w, c, "super");
    }

    [AvaloniaFact]
    public void A_knockout()
    {
        var (w, c, g) = Open(9);
        g.Go(Roster.Zen, Roster.Bruna, Mode.Arcade, Skill.Normal);
        g.ComputerPlaysOne(0.9);
        Assert.True(Until(g, c, g => g.Phase == Phase.RoundOver && g.PhaseTime > 0.5));
        Shot(w, c, "ko");
        Assert.True(Until(g, c, g => g.Phase == Phase.RoundOver && g.PhaseTime > 2.4));
        Shot(w, c, "round-over");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Each_stage(int stage)
    {
        var (w, c, g) = Open(stage + 20);
        var other = stage < 4 ? Roster.All[stage] : GameEngine.Shadow(Roster.Kai);
        var me = Roster.All[(stage + 1) % 4];
        g.Go(me, other, Mode.Versus);
        g.ComputerPlaysOne(0.6);
        // The fight runs a little with player 2 standing, then the picture.
        for (int i = 0; i < 100; i++) { g.Tick(); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, $"stage-{stage}");
    }

    [AvaloniaFact]
    public void The_high_scores()
    {
        var (w, c, g) = Open();
        g.HighScores.Insert("AVA", 64000, 4);
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        for (int i = 0; i < 60 * 400 && g.State != GameState.HighScores; i++) g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
        c.Advance(1.5);
        Shot(w, c, "high-scores");
    }
}
