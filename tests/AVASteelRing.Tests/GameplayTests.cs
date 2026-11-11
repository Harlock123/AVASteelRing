using Avalonia.Input;
using AVASteelRing.Engine;

namespace AVASteelRing.Tests;

public sealed class GameplayTests
{
    private static GameEngine NewEngine(int seed = 3) =>
        new(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaring-test-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };

    /// <summary>A fight with the two fighters a set distance apart, both free, the clock running.</summary>
    private static Fight Duel(Character a, Character b, double gap = 110)
    {
        var f = new Fight(a, b);
        Fight.Place(f.P1, 512 - gap / 2);
        Fight.Place(f.P2, 512 + gap / 2);
        return f;
    }

    private static void Run(Fight f, int frames, Controls c1 = default, Controls c2 = default)
    {
        for (int i = 0; i < frames; i++) f.Tick(c1, c2);
    }

    /// <summary>Holds these keys for a few frames, through the real key handler, as player 1.</summary>
    private static void Keys(Fight f, InputHandler input, int frames, params Key[] keys)
    {
        foreach (var k in keys) input.OnKeyDown(k);
        for (int i = 0; i < frames; i++) f.Tick(input.KeysOne, default);
        foreach (var k in keys) input.OnKeyUp(k);
    }

    [Fact]
    public void Up_on_the_keys_jumps_and_down_crouches()
    {
        using var input = new InputHandler();
        var f = Duel(Roster.Kai, Roster.Bruna, 300);
        input.OnKeyDown(Key.W);
        Assert.Equal(1, input.KeysOne.Y);
        f.Tick(input.KeysOne, default);
        input.OnKeyUp(Key.W);
        Run(f, 8);
        Assert.Equal(FighterState.Air, f.P1.State);
        Assert.True(f.P1.Y < Fighter.Ground - 40);
        Run(f, 60);
        Assert.Equal(FighterState.Idle, f.P1.State);

        Keys(f, input, 3, Key.S);
        input.OnKeyDown(Key.S);
        f.Tick(input.KeysOne, default);
        Assert.Equal(FighterState.Crouch, f.P1.State);
    }

    [Fact]
    public void A_quarter_circle_on_the_keys_throws_a_fireball()
    {
        using var input = new InputHandler();
        var f = Duel(Roster.Kai, Roster.Bruna, 400);
        Keys(f, input, 2, Key.S);
        Keys(f, input, 2, Key.S, Key.D);
        input.OnKeyDown(Key.D);
        input.OnKeyDown(Key.U);
        f.Tick(input.KeysOne, default);
        Assert.Equal("TIGER FLAME", f.P1.Move?.Name);
        Run(f, 14);
        Assert.Single(f.Projectiles);
    }

    [Fact]
    public void Walking_in_then_a_quarter_circle_is_still_a_quarter_circle()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 500);
        Run(f, 12, new Controls(1, 0));
        Run(f, 2, new Controls(0, -1));
        Run(f, 2, new Controls(1, -1));
        f.Tick(new Controls(1, 0, Buttons.HeavyPunch), default);
        Assert.Equal("TIGER FLAME", f.P1.Move?.Name);
    }

    [Fact]
    public void Forward_down_down_forward_is_the_rising_move()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 300);
        Run(f, 2, new Controls(1, 0));
        Run(f, 2, new Controls(0, -1));
        f.Tick(new Controls(1, -1, Buttons.LightPunch), default);
        Assert.Equal("RISING FIST", f.P1.Move?.Name);
        Run(f, 8);
        Assert.True(f.P1.Y < Fighter.Ground - 20, "it rises off the ground");
    }

    [Fact]
    public void A_jab_lands_on_someone_standing_there()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 100);
        f.Tick(new Controls(0, 0, Buttons.LightPunch), default);
        Run(f, 8);
        Assert.True(f.P2.Health < Fighter.MaxHealth);
        Assert.Equal(FighterState.HitStun, f.P2.State);
        Assert.True(f.P1.Meter > 0, "landing a hit builds the meter");
    }

    [Fact]
    public void A_jab_out_of_reach_misses()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 260);
        f.Tick(new Controls(0, 0, Buttons.LightPunch), default);
        Run(f, 20);
        Assert.Equal(Fighter.MaxHealth, f.P2.Health);
    }

    [Fact]
    public void Holding_back_blocks_and_the_right_height_matters()
    {
        // P2 faces left, so back for P2 is to the right (+1).
        var back = new Controls(1, 0);
        var downBack = new Controls(1, -1);

        var mid = Duel(Roster.Kai, Roster.Bruna, 100);
        mid.Tick(new Controls(0, 0, Buttons.HeavyPunch), back);
        Run(mid, 14, default, back);
        Assert.Equal(Fighter.MaxHealth, mid.P2.Health);

        // A low kick gets under a standing block...
        var low = Duel(Roster.Kai, Roster.Bruna, 80);
        Run(low, 1, new Controls(0, -1), back);
        low.Tick(new Controls(0, -1, Buttons.LightKick), back);
        Run(low, 10, new Controls(0, -1), back);
        Assert.True(low.P2.Health < Fighter.MaxHealth);

        // ...but not a crouching one.
        var lowBlocked = Duel(Roster.Kai, Roster.Bruna, 80);
        Run(lowBlocked, 1, new Controls(0, -1), downBack);
        lowBlocked.Tick(new Controls(0, -1, Buttons.LightKick), downBack);
        Run(lowBlocked, 10, new Controls(0, -1), downBack);
        Assert.Equal(Fighter.MaxHealth, lowBlocked.P2.Health);
    }

    [Fact]
    public void A_blocked_special_chips_a_little()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 300);
        Run(f, 2, new Controls(0, -1), new Controls(1, 0));
        Run(f, 2, new Controls(1, -1), new Controls(1, 0));
        f.Tick(new Controls(1, 0, Buttons.HeavyPunch), new Controls(1, 0));
        Run(f, 120, default, new Controls(1, 0));
        Assert.InRange(f.P2.Health, Fighter.MaxHealth - 30, Fighter.MaxHealth - 1);
    }

    [Fact]
    public void Only_one_fireball_at_a_time_and_two_cancel_out()
    {
        var f = Duel(Roster.Kai, Roster.Orla, 700);
        void Throw(bool p1)
        {
            var seq = new[] { new Controls(0, -1), new Controls(0, -1), new Controls(1, -1), new Controls(1, -1), new Controls(1, 0, Buttons.HeavyPunch), default };
            foreach (var c in seq)
            {
                var mine = p1 ? c : c with { X = -c.X };
                if (p1) f.Tick(mine, default); else f.Tick(default, mine);
            }
        }
        Throw(true);
        Run(f, 14);
        Assert.Single(f.Projectiles);
        Run(f, 30);
        Throw(true);
        Assert.Single(f.Projectiles);

        Throw(false);
        Run(f, 80);
        Assert.Empty(f.Projectiles);
        Assert.Equal(Fighter.MaxHealth, f.P1.Health);
        Assert.Equal(Fighter.MaxHealth, f.P2.Health);
    }

    [Fact]
    public void A_low_kick_cancels_into_a_special_for_a_combo()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 100);
        Run(f, 1, new Controls(0, -1));
        f.Tick(new Controls(0, -1, Buttons.LightKick), default);
        Run(f, 4, new Controls(0, -1));
        Run(f, 2, new Controls(1, -1));
        f.Tick(new Controls(1, 0, Buttons.HeavyPunch), default);
        Assert.Equal("TIGER FLAME", f.P1.Move?.Name);
        Run(f, 30);
        Assert.True(f.P2.ComboTaken >= 2 || f.Callout == "2 HITS" || f.P2.Health < Fighter.MaxHealth - 90,
            $"the fireball should land as the second hit (health {f.P2.Health})");
    }

    [Fact]
    public void The_super_needs_a_full_meter()
    {
        var seq = new[] { new Controls(0, -1), new Controls(1, -1), new Controls(1, 0), new Controls(0, -1), new Controls(1, -1), new Controls(1, 0, Buttons.HeavyPunch) };
        var empty = Duel(Roster.Kai, Roster.Bruna, 400);
        foreach (var c in seq) { empty.Tick(c, default); empty.Tick(c, default); }
        Assert.Equal("TIGER FLAME", empty.P1.Move?.Name);

        var full = Duel(Roster.Kai, Roster.Bruna, 400);
        full.P1.Meter = Fighter.MaxMeter;
        foreach (var c in seq) { full.Tick(c, default); full.Tick(c, default); }
        Assert.Equal("BLAZING TIGER", full.P1.Move?.Name);
        Assert.Equal(0, full.P1.Meter);
        Run(full, 200);
        Assert.True(full.P2.Health < Fighter.MaxHealth - 150);
    }

    [Fact]
    public void A_sweep_knocks_down_and_a_fighter_on_the_floor_cannot_be_hit()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 110);
        Run(f, 1, new Controls(0, -1));
        f.Tick(new Controls(0, -1, Buttons.HeavyKick), default);
        Run(f, 12, new Controls(0, -1));
        Assert.Equal(FighterState.Knockdown, f.P2.State);
        Assert.Null(f.P2.Hurtbox);
        Run(f, 120);
        Assert.True(f.P2.Free);
    }

    [Fact]
    public void Fighters_push_apart_and_stay_on_the_stage()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 200);
        Run(f, 200, new Controls(1, 0), new Controls(-1, 0));
        Assert.True(f.P2.X - f.P1.X >= 40, "they can't walk through each other");
        var g = Duel(Roster.Kai, Roster.Bruna, 200);
        Run(g, 400, new Controls(-1, 0), new Controls(1, 0));
        Assert.Equal(Fight.Left, g.P1.X);
        Assert.Equal(Fight.Right, g.P2.X);
    }

    [Fact]
    public void A_knockout_ends_the_round_and_time_up_goes_to_the_healthier()
    {
        var f = Duel(Roster.Kai, Roster.Bruna, 100);
        f.P2.Health = 10;
        f.Tick(new Controls(0, 0, Buttons.HeavyPunch), default);
        Run(f, 14);
        Assert.True(f.Over);
        Assert.Same(f.P1, f.Winner);
        Assert.Equal(FighterState.Ko, f.P2.State);

        var t = Duel(Roster.Kai, Roster.Bruna, 400);
        t.P1.Health = 500;
        Run(t, Fight.RoundFrames + 1);
        Assert.True(t.Over);
        Assert.Same(t.P2, t.Winner);
    }

    [Fact]
    public void Two_rounds_win_the_bout_and_the_ladder_moves_on()
    {
        var g = NewEngine();
        g.Go(Roster.Kai, Roster.Bruna, Mode.Arcade);
        for (int round = 0; round < 2; round++)
        {
            // Finish player 2 off with one punch.
            for (int i = 0; i < 600 && g.Phase != Phase.Fight; i++) g.Tick();
            var fight = g.Fight!;
            Fight.Place(fight.P1, 470);
            Fight.Place(fight.P2, 570);
            fight.P2.Health = 1;
            g.Tick(new Controls(0, 0, Buttons.HeavyPunch));
            for (int i = 0; i < 20 && !fight.Over; i++) g.Tick();
            Assert.True(fight.Over);
            for (int i = 0; i < 60 * 5 && g.Phase == Phase.RoundOver; i++) g.Tick();
        }
        Assert.Equal(Phase.BoutOver, g.Phase);
        Assert.Equal(0, g.BoutWinner);
        Assert.True(g.Score > 2 * GameEngine.RoundPoints + GameEngine.BoutPoints);
        g.Run(60 * 5);
        Assert.Equal(2, g.Bout);
        Assert.NotEqual(Roster.Bruna, g.Fighter2);
    }

    [Fact]
    public void Losing_a_bout_ends_the_game_and_a_good_score_asks_for_initials()
    {
        var g = NewEngine();
        g.Go(Roster.Zen, Roster.Orla, Mode.Arcade);
        for (int round = 0; round < 2; round++)
        {
            for (int i = 0; i < 600 && g.Phase != Phase.Fight; i++) g.Tick();
            g.Fight!.P1.Health = 1;
            Fight.Place(g.Fight.P1, 470);
            Fight.Place(g.Fight.P2, 570);
            for (int i = 0; i < 60 * 60 && !g.Fight.Over; i++) g.Tick();
            Assert.True(g.Fight.Over);
            for (int i = 0; i < 60 * 5 && g.Phase == Phase.RoundOver; i++) g.Tick();
        }
        g.Run(60 * 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 7);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void The_computer_fights_a_whole_bout_without_getting_stuck()
    {
        var g = NewEngine(11);
        g.Go(Roster.Bruna, Roster.Zen, Mode.Arcade, Skill.Hard);
        g.ComputerPlaysOne(0.8);
        int damage1 = 0, damage2 = 0;
        for (int i = 0; i < 60 * 60 * 6 && g.Phase != Phase.BoutOver; i++)
        {
            g.Tick();
            if (g.Fight is { } f)
            {
                damage1 = Math.Max(damage1, Fighter.MaxHealth - f.P1.Health);
                damage2 = Math.Max(damage2, Fighter.MaxHealth - f.P2.Health);
            }
        }
        Assert.Equal(Phase.BoutOver, g.Phase);
        Assert.True(damage1 > 200 && damage2 > 200, $"both should land hits ({damage1}, {damage2})");
    }

    [Fact]
    public void The_select_screen_changes_mode_fighters_and_skill()
    {
        var g = NewEngine();
        g.OpenSelect();
        g.Adjust(1, 1);
        Assert.Equal(1, g.Pick1);
        g.Adjust(1, -2);
        Assert.Equal(Roster.All.Count - 1, g.Pick1);
        g.Adjust(2, 1);
        Assert.Equal(Skill.Hard, g.Skill);
        g.Adjust(0, 1);
        Assert.Equal(Mode.Versus, g.Mode);
        g.Adjust(2, 1);
        Assert.Equal(2, g.Pick2);
        g.StartGame();
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Roster.All[2], g.Fighter2);
    }

    [Fact]
    public void The_arcade_ladder_ends_with_your_own_shadow()
    {
        var g = NewEngine();
        g.Go(Roster.Orla, Roster.Kai, Mode.Arcade);
        Assert.Equal(4, g.Ladder.Count);
        Assert.Equal("SHADOW", g.Ladder[^1].Name);
        Assert.Equal(Roster.Orla.Moves, g.Ladder[^1].Moves);
        Assert.Equal(3, g.Ladder.Take(3).Distinct().Count());
        Assert.DoesNotContain(Roster.Orla, g.Ladder);
    }

    [Fact]
    public void The_computer_grows_harder_up_the_ladder()
    {
        Assert.True(GameEngine.SkillOf(Skill.Easy, 1) < GameEngine.SkillOf(Skill.Normal, 1));
        Assert.True(GameEngine.SkillOf(Skill.Normal, 1) < GameEngine.SkillOf(Skill.Normal, 4));
        Assert.True(new Cpu(1, new Random(1)).Reaction < new Cpu(0, new Random(1)).Reaction);
    }
}
