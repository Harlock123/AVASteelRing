namespace AVASteelRing.Engine;

public enum GameState { Title, Select, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The two fighters' names, face to face, before a bout.</summary>
    Versus,
    /// <summary>"ROUND 1 ... FIGHT!": the fighters stand ready.</summary>
    Intro,
    Fight,
    /// <summary>A knockout or time up: the round's result.</summary>
    RoundOver,
    /// <summary>The bout's winner.</summary>
    BoutOver,
}

public enum Mode { Arcade, Versus }

public enum Skill { Easy, Normal, Hard }

/// <summary>
/// The whole game with no UI: the title, choosing fighters, the one-player ladder against the computer, two players
/// at one keyboard, rounds, scoring and the high-score table. <see cref="Update"/> runs in fixed 60 Hz steps.
/// </summary>
public sealed class GameEngine
{
    public const double Step = 1.0 / 60;
    public const double TitleIdleSeconds = 16, HighScoreSeconds = 10, VersusSeconds = 2.6, IntroSeconds = 2.2;
    public const double RoundOverSeconds = 3.6, BoutOverSeconds = 4, OverSeconds = 6;
    public const int RoundsToWin = 2, MaxRounds = 5;
    public const int RoundPoints = 1000, PerfectPoints = 2000, BoutPoints = 3000;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true;
    private Controls _menuPrev;
    private readonly Random _random;
    private Cpu? _cpu1, _cpu2;
    private int _lastRoundWinner = -1;

    public GameEngine(HighScoreTable? table = null, int? seed = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        _random = seed is { } s ? new Random(s) : new Random();
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Versus;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    // The select screen's choices.
    public Mode Mode { get; private set; } = Mode.Arcade;
    public int Pick1 { get; private set; }
    public int Pick2 { get; private set; } = 1;
    public Skill Skill { get; private set; } = Skill.Normal;
    public int SelectRow { get; private set; }
    public const int SelectRows = 4;

    public Fight? Fight { get; private set; }
    public Character Fighter1 { get; private set; } = Roster.Kai;
    public Character Fighter2 { get; private set; } = Roster.Bruna;
    /// <summary>The bout in the arcade ladder, from 1.</summary>
    public int Bout { get; private set; }
    public int Round { get; private set; }
    public int Wins1 { get; private set; }
    public int Wins2 { get; private set; }
    public int Score { get; private set; }
    /// <summary>The side that won the last round (0 or 1), or −1 for a draw.</summary>
    public int RoundWinner { get; private set; } = -1;
    public int BoutWinner { get; private set; } = -1;
    public bool Perfect { get; private set; }
    public bool Champion { get; private set; }
    /// <summary>The opponents of the ladder, in order, the last being your own shadow.</summary>
    public IReadOnlyList<Character> Ladder { get; private set; } = [];
    public int Stage => Roster.All.ToList().IndexOf(Fighter2) is var i and >= 0 ? i : Roster.All.Count;

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c1, Controls c2, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Step)
        {
            _accumulator -= Step;
            Tick(c1, c2, start);
        }
    }

    public void Tick(Controls c1 = default, Controls c2 = default, bool start = false)
    {
        StateTime += Step;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        bool accept = c1.Held != Buttons.None && _menuPrev.Held == Buttons.None;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += Step;
                if (startPressed || accept) OpenSelect();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Select:
                SelectStep(c1, startPressed, accept);
                break;
            case GameState.Attract:
                Simulate(default, default);
                if (startPressed) OpenSelect();
                break;
            case GameState.Playing:
                Simulate(c1, c2);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds || (startPressed || accept) && StateTime > 1.5) AfterGameOver();
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Bout);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed || accept) OpenSelect();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
        _menuPrev = c1;
    }

    // ───────────────────────────── choosing ─────────────────────────────

    public void OpenSelect()
    {
        State = GameState.Select;
        StateTime = 0;
        SelectRow = 1;
        _menuPrev = new Controls(1, 1, Buttons.LightPunch);
        Play(Sfx.Select);
        Play(Sfx.MusicStop);
    }

    private void SelectStep(Controls c, bool startPressed, bool accept)
    {
        int move = c.Y != _menuPrev.Y ? c.Y : 0;
        int turn = c.X != _menuPrev.X ? c.X : 0;
        // Up the screen is up the list.
        if (move != 0)
        {
            SelectRow = (SelectRow - move + SelectRows) % SelectRows;
            Play(Sfx.Select);
        }
        if (turn != 0) { Adjust(SelectRow, turn); Play(Sfx.Select); }
        if (startPressed || accept && SelectRow == SelectRows - 1) StartGame();
        else if (accept) { Adjust(SelectRow, 1); Play(Sfx.Select); }
    }

    /// <summary>Changes the select row's choice one step: the mode, player 1's fighter, the computer's skill or player 2's fighter.</summary>
    public void Adjust(int row, int by)
    {
        int n = Roster.All.Count;
        switch (row)
        {
            case 0: Mode = Mode == Mode.Arcade ? Mode.Versus : Mode.Arcade; break;
            case 1: Pick1 = (Pick1 + by + n) % n; break;
            case 2 when Mode == Mode.Arcade: Skill = (Skill)(((int)Skill + by + 3) % 3); break;
            case 2: Pick2 = (Pick2 + by + n) % n; break;
        }
    }

    public static double SkillOf(Skill s, int bout) => Math.Min(1, s switch { Skill.Easy => 0.15, Skill.Normal => 0.42, _ => 0.7 } + 0.09 * (bout - 1));

    /// <summary>A fighter in dark colours: the last opponent of the ladder.</summary>
    public static Character Shadow(Character c) => c with
    {
        Name = "SHADOW", From = "THE RING", Skin = 0xFF5A5060, Hair = 0xFF0A0A10, Gi = 0xFF1C1C24, Belt = 0xFF8A30D0, Trim = 0xFFB050FF,
        Toughness = c.Toughness * 1.15,
    };

    public void StartGame()
    {
        Fighter1 = Roster.All[Pick1];
        Score = 0;
        Champion = false;
        NewEntryIndex = -1;
        State = GameState.Playing;
        StateTime = 0;
        if (Mode == Mode.Arcade)
        {
            var others = Roster.All.Where(c => c != Fighter1).OrderBy(_ => _random.Next()).ToList();
            Ladder = [.. others, Shadow(Fighter1)];
            Bout = 1;
            _cpu1 = null;
            BeginBout(Ladder[0]);
        }
        else
        {
            Ladder = [];
            Bout = 1;
            _cpu1 = _cpu2 = null;
            BeginBout(Roster.All[Pick2]);
        }
        Play(Sfx.MusicStop);
    }

    private void StartAttract()
    {
        var picks = Roster.All.OrderBy(_ => _random.Next()).ToList();
        Fighter1 = picks[0];
        Mode = Mode.Arcade;
        State = GameState.Attract;
        StateTime = 0;
        Bout = 1;
        Score = 0;
        _cpu1 = new Cpu(0.6, _random);
        BeginBout(picks[1]);
        _cpu2!.Skill = 0.6;
    }

    private void BeginBout(Character opponent)
    {
        Fighter2 = opponent;
        if (Mode == Mode.Arcade || State == GameState.Attract) _cpu2 = new Cpu(SkillOf(Skill, Bout), _random);
        Wins1 = Wins2 = 0;
        Round = 0;
        BoutWinner = -1;
        _lastRoundWinner = -1;
        Fight = new Fight(Fighter1, Fighter2);
        SetPhase(Phase.Versus);
    }

    private void NextRound()
    {
        Round++;
        int m1 = Fight?.P1.Meter ?? 0, m2 = Fight?.P2.Meter ?? 0;
        Fight = new Fight(Fighter1, Fighter2);
        // The super meter carries from round to round.
        Fight.P1.Meter = Round > 1 ? m1 : 0;
        Fight.P2.Meter = Round > 1 ? m2 : 0;
        RoundWinner = -1;
        Perfect = false;
        Fight.Call(Round == MaxRounds ? "FINAL ROUND" : $"ROUND {Round}", 80);
        Play(Sfx.Round);
        SetPhase(Phase.Intro);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(Controls c1, Controls c2)
    {
        PhaseTime += Step;
        var fight = Fight!;
        switch (Phase)
        {
            case Phase.Versus:
                if (PhaseTime > VersusSeconds) NextRound();
                break;
            case Phase.Intro:
                fight.Tick(default, default, clock: false);
                if (PhaseTime > IntroSeconds - 0.8 && fight.Callout != "FIGHT!" && PhaseTime < IntroSeconds - 0.7)
                {
                    fight.Call("FIGHT!", 50);
                    Play(Sfx.Bell);
                }
                if (PhaseTime > IntroSeconds) { SetPhase(Phase.Fight); Play(Sfx.Music); }
                break;
            case Phase.Fight:
            {
                var a = _cpu1?.Next(fight, fight.P1) ?? c1;
                var b = _cpu2?.Next(fight, fight.P2) ?? c2;
                int before = fight.P2.Health;
                fight.Tick(a, b);
                // Points for the damage player 1 does to the computer.
                if (State == GameState.Playing && Mode == Mode.Arcade) Score += Math.Max(0, before - fight.P2.Health) * 5;
                if (fight.Over) EndRound();
                break;
            }
            case Phase.RoundOver:
                fight.Tick(default, default, clock: false);
                if (PhaseTime > 1.6 && RoundWinner >= 0)
                {
                    var w = RoundWinner == 0 ? fight.P1 : fight.P2;
                    if (w.Free) { w.State = FighterState.Win; w.Frame = 0; }
                }
                if (PhaseTime > RoundOverSeconds) AfterRound();
                break;
            case Phase.BoutOver:
                fight.Tick(default, default, clock: false);
                if (PhaseTime > BoutOverSeconds) AfterBout();
                break;
        }

        foreach (var s in fight.Heard) Play(s);
        fight.Heard.Clear();
    }

    private void EndRound()
    {
        var fight = Fight!;
        RoundWinner = fight.Winner == fight.P1 ? 0 : fight.Winner == fight.P2 ? 1 : -1;
        if (RoundWinner == 0) Wins1++;
        else if (RoundWinner == 1) Wins2++;
        if (RoundWinner >= 0) _lastRoundWinner = RoundWinner;
        var w = RoundWinner == 0 ? fight.P1 : RoundWinner == 1 ? fight.P2 : null;
        Perfect = w is { Health: Fighter.MaxHealth };
        if (State == GameState.Playing && Mode == Mode.Arcade && RoundWinner == 0)
            Score += RoundPoints + fight.P1.Health + fight.Timer / 60 * 20 + (Perfect ? PerfectPoints : 0);
        Play(Sfx.MusicStop);
        if (Perfect) Play(Sfx.Perfect);
        SetPhase(Phase.RoundOver);
    }

    private void AfterRound()
    {
        bool decided = Wins1 >= RoundsToWin || Wins2 >= RoundsToWin || Round >= MaxRounds;
        if (!decided) { NextRound(); return; }
        BoutWinner = Wins1 != Wins2 ? (Wins1 > Wins2 ? 0 : 1) : _lastRoundWinner >= 0 ? _lastRoundWinner : 1;
        if (State == GameState.Playing && Mode == Mode.Arcade && BoutWinner == 0) Score += BoutPoints * Bout;
        Play(State == GameState.Attract || Mode == Mode.Versus || BoutWinner == 0 ? Sfx.Victory : Sfx.Defeat);
        SetPhase(Phase.BoutOver);
    }

    private void AfterBout()
    {
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        if (Mode == Mode.Versus) { EndGame(); return; }
        if (BoutWinner != 0) { EndGame(); return; }
        if (Bout >= Ladder.Count) { Champion = true; EndGame(); return; }
        Bout++;
        BeginBout(Ladder[Bout - 1]);
    }

    private void EndGame()
    {
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void AfterGameOver()
    {
        if (Mode == Mode.Arcade && HighScores.Qualifies(Score))
        {
            State = GameState.EnterInitials;
            StateTime = 0;
            Initials = new InitialsEntry(HighScores.LastInitials);
        }
        else ShowHighScores(-1);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Play(Sfx.MusicStop);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Play(Sfx.MusicStop);
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c1 = default, Controls c2 = default)
    {
        for (int i = 0; i < steps; i++) Tick(c1, c2);
    }

    /// <summary>Straight into a fight between these two, past the select screen and the cards.</summary>
    public void Go(Character p1, Character p2, Mode mode = Mode.Versus, Skill skill = Skill.Normal)
    {
        Mode = mode;
        Skill = skill;
        Pick1 = Roster.All.ToList().IndexOf(p1) is var i and >= 0 ? i : 0;
        Pick2 = Roster.All.ToList().IndexOf(p2) is var j and >= 0 ? j : 0;
        StartGame();
        if (mode == Mode.Versus) Fighter2 = p2;
        else
        {
            Ladder = [p2, .. Ladder.Where(c => c != p2)];
            BeginBout(p2);
        }
        Fight = new Fight(Fighter1, Fighter2);
        NextRound();
        SetPhase(Phase.Fight);
        Fight.Call("", 1);
    }

    /// <summary>Lets the computer play player 1 too (for the demonstration and for tests).</summary>
    public void ComputerPlaysOne(double skill) => _cpu1 = new Cpu(skill, _random);
}
