using Avalonia;
using Avalonia.Media;
using AVASteelRing.Engine;

namespace AVASteelRing.Views;

public sealed partial class GameCanvas
{
    private const double W = 1024, H = 768;
    private double _shown1 = Fighter.MaxHealth, _shown2 = Fighter.MaxHealth;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.Select: DrawSelect(dc); break;
            case GameState.HighScores: DrawStage(dc, 4); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawStage(dc, g.Stage); DrawInitials(dc); break;
            case GameState.GameOver: DrawFight(dc); DrawGameOver(dc); break;
            default:
                DrawFight(dc);
                if (g.Phase == Phase.Versus) DrawVersus(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, W, H));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract && (int)(_clock * 2) % 2 == 0)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 700), 26, Yellow, center: true);
    }

    private static void Card(DrawingContext dc, Rect r)
    {
        dc.FillRectangle(Brush(0xE8100A0C), r, 6);
        dc.DrawRectangle(null, Pen(0xFFB8A070, 3), r, 6, 6);
        dc.DrawRectangle(null, Pen(0xFF5A4630, 2), r.Deflate(6), 4, 4);
    }

    // ───────────────────────────── the fight ─────────────────────────────

    private void DrawFight(DrawingContext dc)
    {
        var g = Engine;
        DrawStage(dc, g.Stage);
        if (g.Fight is not { } fight) return;
        // The one who is attacking is drawn in front.
        var first = fight.P1.State == FighterState.Attack && fight.P2.State != FighterState.Attack ? fight.P2 : fight.P1;
        var front = fight.Other(first);
        bool superFreeze = fight.Freeze > 0 && !fight.Over;
        if (superFreeze) dc.FillRectangle(Brush(0x90000010), new Rect(0, 0, W, H));
        foreach (var f in new[] { first, front })
        {
            if (f.State == FighterState.Attack && f.Move is { Cost: > 0 }) DrawAura(dc, f);
            DrawFighter(dc, f, f.State == FighterState.HitStun && f.Frame < 3 ? 0.6 : 0);
        }
        foreach (var p in fight.Projectiles) DrawProjectile(dc, p);
        foreach (var sp in fight.Sparks) DrawSpark(dc, sp, fight.Frames);
        DrawHud(dc, fight);
        if (fight.Callout is { Length: > 0 } call)
        {
            bool combo = call.EndsWith("HITS", StringComparison.Ordinal);
            if (combo) Text(dc, call, new Point(fight.P1.ComboTaken > fight.P2.ComboTaken ? 860 : 164, 200), 46, Orange, center: true);
            else
            {
                double size = call.Length > 12 ? 52 : 110;
                Text(dc, call, new Point(512, 300), size, call == "K.O." ? Red : Yellow, center: true);
            }
        }

        if (g.Phase == Phase.RoundOver && g.PhaseTime > 1.4)
        {
            string who = g.RoundWinner switch { 0 => fight.P1.C.Name + " WINS", 1 => fight.P2.C.Name + " WINS", _ => "DRAW" };
            Text(dc, who, new Point(512, 390), 64, White, center: true);
            if (g.Perfect) Text(dc, "PERFECT", new Point(512, 450), 58, (int)(_clock * 6) % 2 == 0 ? Yellow : Orange, center: true);
        }
        if (g.Phase == Phase.BoutOver) DrawBoutOver(dc, fight);
    }

    private void DrawAura(DrawingContext dc, Fighter f)
    {
        double pulse = 0.5 + 0.5 * Math.Sin(_clock * 30);
        for (int i = 3; i >= 1; i--)
            dc.DrawEllipse(Brush(Alpha(f.C.Trim, 0.12 + 0.06 * pulse)), null, new Point(f.X, f.Y - 80 * f.S), (50 + i * 16) * f.S, (90 + i * 16) * f.S);
    }

    private void DrawHud(DrawingContext dc, Fight fight)
    {
        var g = Engine;
        _shown1 = fight.P1.Health > _shown1 ? fight.P1.Health : Math.Max(fight.P1.Health, _shown1 - 6);
        _shown2 = fight.P2.Health > _shown2 ? fight.P2.Health : Math.Max(fight.P2.Health, _shown2 - 6);
        Bar(dc, 40, fight.P1.Health, _shown1, false);
        Bar(dc, 572, fight.P2.Health, _shown2, true);
        Text(dc, fight.P1.C.Name, new Point(44, 92), 34, White);
        Text(dc, fight.P2.C.Name, new Point(980, 92), 34, White, right: true);
        for (int i = 0; i < GameEngine.RoundsToWin; i++)
        {
            Lamp(dc, new Point(440 - i * 26, 92), i < g.Wins1);
            Lamp(dc, new Point(584 + i * 26, 92), i < g.Wins2);
        }
        // The clock.
        dc.FillRectangle(Brush(0xE0100A0C), new Rect(474, 30, 76, 62), 6);
        dc.DrawRectangle(null, Pen(0xFFB8A070, 2), new Rect(474, 30, 76, 62), 6, 6);
        int secs = (fight.Timer + 59) / 60;
        Text(dc, $"{secs:00}", new Point(512, 60), 56, secs <= 10 && (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
        if (g.Mode == Mode.Arcade && g.State == GameState.Playing)
            Text(dc, $"SCORE {g.Score:N0}", new Point(44, 16), 24, Dim);
        if (g.Mode == Mode.Arcade && g.State == GameState.Playing)
            Text(dc, $"BOUT {g.Bout} OF {g.Ladder.Count}", new Point(980, 16), 24, Dim, right: true);

        Meter(dc, 40, fight.P1.Meter, false);
        Meter(dc, 724, fight.P2.Meter, true);
    }

    private static void Lamp(DrawingContext dc, Point at, bool lit)
    {
        dc.DrawEllipse(Brush(lit ? 0xFFFFD040 : 0xFF2A2420), Pen(0xFF0A0A0A, 2), at, 9, 9);
        if (lit) dc.DrawEllipse(Brush(0xFFFFF4C0), null, new Point(at.X - 2, at.Y - 2), 3, 3);
    }

    /// <summary>A health bar, 412 wide: it drains toward the middle, a red strip showing what was lost just now.</summary>
    private static void Bar(DrawingContext dc, double x, int health, double shown, bool right)
    {
        const double w = 412, h = 28, y = 42;
        dc.FillRectangle(Brush(0xFF0A0A0A), new Rect(x - 3, y - 3, w + 6, h + 6), 4);
        dc.FillRectangle(Brush(0xFF3A1010), new Rect(x, y, w, h));
        double k = health / (double)Fighter.MaxHealth, ks = shown / Fighter.MaxHealth;
        double lw = w * ks, hw = w * k;
        dc.FillRectangle(Brush(0xFFE83020), new Rect(right ? x : x + w - lw, y, lw, h));
        uint col = k > 0.5 ? 0xFFFFD84A : k > 0.25 ? 0xFFFF9A30 : 0xFFFF5A3A;
        dc.FillRectangle(Brush(col), new Rect(right ? x : x + w - hw, y, hw, h));
        dc.FillRectangle(Brush(0x50FFFFFF), new Rect(right ? x : x + w - hw, y + 3, hw, 6));
        dc.DrawRectangle(null, Pen(0xFFB8A070, 2), new Rect(x, y, w, h));
    }

    private void Meter(DrawingContext dc, double x, int meter, bool right)
    {
        const double w = 260, h = 16, y = 736;
        dc.FillRectangle(Brush(0xFF0A0A0A), new Rect(x - 2, y - 2, w + 4, h + 4), 3);
        dc.FillRectangle(Brush(0xFF14182A), new Rect(x, y, w, h));
        double mw = w * meter / Fighter.MaxMeter;
        bool full = meter >= Fighter.MaxMeter;
        uint col = full ? ((int)(_clock * 8) % 2 == 0 ? 0xFF6AF0FF : 0xFFFFFFFF) : 0xFF3A8AE8;
        dc.FillRectangle(Brush(col), new Rect(right ? x + w - mw : x, y, mw, h));
        Text(dc, full ? "SUPER!" : "SUPER", new Point(right ? x - 10 : x + w + 10, y + 7), 24, full ? Cyan : Dim, right: right);
    }

    private void DrawVersus(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, W, H));
        // Two slanting panels meeting in the middle.
        Poly(dc, Shade(g.Fighter1.Gi, 0.45), null, 0, P(0, 180), P(560, 180), P(470, 560), P(0, 560));
        Poly(dc, Shade(g.Fighter2.Gi, 0.45), null, 0, P(560, 180), P(1024, 180), P(1024, 560), P(470, 560));
        dc.DrawLine(Pen(Yellow, 6), P(560, 180), P(470, 560));
        Text(dc, g.Fighter1.Name, new Point(240, 260), 84, White, center: true);
        Text(dc, g.Fighter1.From, new Point(240, 330), 30, Dim, center: true);
        Text(dc, g.Fighter2.Name, new Point(790, 420), 84, White, center: true);
        Text(dc, g.Fighter2.From, new Point(790, 490), 30, Dim, center: true);
        Text(dc, "VS", new Point(515, 370), 120, Red, center: true);
        if (g.Mode == Mode.Arcade && g.State == GameState.Playing)
            Text(dc, g.Bout == g.Ladder.Count ? "THE LAST BOUT" : $"BOUT {g.Bout} OF {g.Ladder.Count}", new Point(512, 130), 44, Yellow, center: true);
        Text(dc, g.Fighter2.Style, new Point(790, 530), 22, Dim, center: true);
    }

    private void DrawBoutOver(DrawingContext dc, Fight fight)
    {
        var g = Engine;
        Card(dc, new Rect(232, 170, 560, 220));
        var w = g.BoutWinner == 0 ? fight.P1 : fight.P2;
        Text(dc, $"{w.C.Name} WINS THE BOUT", new Point(512, 220), 50, Yellow, center: true);
        Text(dc, $"ROUNDS {g.Wins1} - {g.Wins2}", new Point(512, 280), 34, White, center: true);
        if (g.Mode == Mode.Arcade && g.State == GameState.Playing)
            Text(dc, g.BoutWinner == 0 ? $"BOUT BONUS {GameEngine.BoutPoints * g.Bout:N0}" : "YOU ARE BEATEN", new Point(512, 336), 30, g.BoutWinner == 0 ? Cyan : Red, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(212, 170, 600, 360));
        if (g.Mode == Mode.Versus)
        {
            var w = g.BoutWinner == 0 ? g.Fighter1 : g.Fighter2;
            Text(dc, $"PLAYER {g.BoutWinner + 1} WINS", new Point(512, 240), 64, Yellow, center: true);
            Text(dc, $"WITH {w.Name}", new Point(512, 310), 40, White, center: true);
        }
        else
        {
            Text(dc, g.Champion ? "CHAMPION!" : "GAME OVER", new Point(512, 240), 80, g.Champion && (int)(_clock * 4) % 2 == 0 ? White : Yellow, center: true);
            Text(dc, g.Champion ? "THE STEEL RING IS YOURS" : $"BEATEN IN BOUT {g.Bout}", new Point(512, 310), 34, White, center: true);
            Text(dc, $"SCORE {g.Score:N0}", new Point(512, 380), 52, Cyan, center: true);
        }
        if (g.StateTime > 1.5) Text(dc, "PRESS ENTER", new Point(512, 480), 28, White, center: true);
    }

    // ───────────────────────────── the stages ─────────────────────────────

    /// <summary>Five places to fight: a night street, a sunset beach, a mountain temple, sea cliffs, and the steel ring.</summary>
    private void DrawStage(DrawingContext dc, int stage)
    {
        switch (stage)
        {
            case 0: Street(dc); break;
            case 1: Beach(dc); break;
            case 2: Temple(dc); break;
            case 3: Cliffs(dc); break;
            default: Ring(dc); break;
        }
    }

    private static void Sky(DrawingContext dc, uint top, uint low, double to = 640)
    {
        int n = 32;
        for (int i = 0; i < n; i++)
            dc.FillRectangle(Brush(Mix(top, low, i / (n - 1.0))), new Rect(0, i * to / n, W, to / n + 1));
    }

    private void Crowd(DrawingContext dc, double y, uint colour, int salt, int count = 34)
    {
        for (int i = 0; i < count; i++)
        {
            uint h = Hash(i, salt, 11);
            double x = i * (W / count) + h % 18, bob = Math.Abs(Math.Sin(_clock * (3 + h % 3) + i)) * 5, hh = 46 + h % 24;
            dc.FillRectangle(Brush(colour), new Rect(x - 11, y - hh + bob, 22, hh + 10), 8);
            dc.DrawEllipse(Brush(colour), null, new Point(x, y - hh - 8 + bob), 10, 11);
            if (h % 5 == 0) dc.DrawLine(Pen(colour, 6), new Point(x + 8, y - hh + bob), new Point(x + 16, y - hh - 26 + bob * 2));
        }
    }

    private void Street(DrawingContext dc)
    {
        Sky(dc, 0xFF060818, 0xFF241838);
        // Buildings, their windows lit at random.
        for (int b = 0; b < 9; b++)
        {
            uint h = Hash(b, 0, 5);
            double x = b * 118 - 20, top = 150 + h % 180, w = 100 + h % 30;
            dc.FillRectangle(Brush(0xFF10101E), new Rect(x, top, w, 640 - top));
            for (double wy = top + 16; wy < 520; wy += 26)
                for (double wx = x + 10; wx < x + w - 14; wx += 20)
                    if (Hash((int)wx, (int)wy, 9) % 3 == 0) dc.FillRectangle(Brush(0xFFE8C870), new Rect(wx, wy, 10, 14));
        }
        // Neon signs, buzzing.
        (double X, double Y, uint C, string T)[] signs = [(110, 300, 0xFFFF3A8A, "RAMEN"), (440, 250, 0xFF3AE8FF, "GAMES"), (770, 320, 0xFFFFD040, "OPEN 24")];
        foreach (var (x, y, c, t) in signs)
        {
            bool on = Hash((int)(_clock * 8), (int)x, 1) % 13 != 0;
            dc.DrawRectangle(Brush(0xE0101018), Pen(on ? c : Shade(c, 0.3), 3), new Rect(x, y, 150, 50), 6, 6);
            Text(dc, t, new Point(x + 75, y + 25), 38, on ? c : Shade(c, 0.35), center: true, shadow: false);
        }
        Crowd(dc, 560, 0xFF181424, 1, 22);
        // Wet road: a dark floor that mirrors the lights.
        dc.FillRectangle(Brush(0xFF141420), new Rect(0, 600, W, 168));
        foreach (var (x, _, c, _) in signs) dc.FillRectangle(Brush(Alpha(c, 0.16)), new Rect(x + 20, 650, 110, 90));
        for (int i = 0; i < 12; i++) dc.FillRectangle(Brush(0x30FFFFFF), new Rect(i * 96 + 20, 700, 50, 4));
        dc.DrawLine(Pen(0xFF2A2A3A, 3), new Point(0, 600), new Point(W, 600));
    }

    private void Beach(DrawingContext dc)
    {
        Sky(dc, 0xFF3A1A5A, 0xFFFF9A4A, 470);
        dc.DrawEllipse(Brush(0xFFFFD27A), null, new Point(620, 430), 70, 70);
        // The sea, with the sun's path on it.
        for (int i = 0; i < 10; i++) dc.FillRectangle(Brush(Mix(0xFF8A3A5A, 0xFF2A1A4A, i / 9.0)), new Rect(0, 470 + i * 13, W, 14));
        for (int i = 0; i < 9; i++)
        {
            double w = 120 - i * 10 + 12 * Math.Sin(_clock * 2 + i);
            dc.FillRectangle(Brush(0x70FFD27A), new Rect(620 - w / 2, 476 + i * 13, w, 4));
        }
        // Palms.
        foreach (var (x, lean) in new[] { (90.0, 30.0), (940.0, -40.0) })
        {
            var top = new Point(x + lean, 300);
            dc.DrawLine(Pen(0xFF1A0A18, 18), new Point(x, 600), top);
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3 + 0.1 * Math.Sin(_clock * 1.5 + k);
                dc.DrawLine(Pen(0xFF1A0A18, 12), top, new Point(top.X + Math.Cos(a) * 110, top.Y + Math.Sin(a) * 40 + 30));
            }
        }
        Crowd(dc, 600, 0xFF2A1428, 2, 26);
        dc.FillRectangle(Brush(0xFFD8A870), new Rect(0, 600, W, 168));
        for (int i = 0; i < 90; i++)
        {
            uint h = Hash(i, 2, 3);
            dc.FillRectangle(Brush(0x40804020), new Rect(h % 1024, 610 + h / 1024 % 150, 4, 2));
        }
    }

    private void Temple(DrawingContext dc)
    {
        Sky(dc, 0xFFB8D0D8, 0xFFF0E8D8, 640);
        // Misty peaks, the far ones paler.
        for (int layer = 0; layer < 3; layer++)
        {
            var pts = new List<Point> { new(0, 640) };
            for (int x = 0; x <= 1024; x += 32)
                pts.Add(new Point(x, 300 + layer * 70 - 90 * Math.Abs(Math.Sin(x * 0.006 + layer * 2.1)) - 30 * Math.Sin(x * 0.02 + layer)));
            pts.Add(new Point(1024, 640));
            Poly(dc, Mix(0xFF7A9A9A, 0xFF2A4A4A, layer / 2.0), null, 0, [.. pts]);
        }
        // The temple gate: red pillars and a curved roof.
        foreach (double x in new[] { 180.0, 844.0 })
            dc.FillRectangle(Brush(0xFFB02A20), new Rect(x - 16, 300, 32, 320));
        Poly(dc, 0xFF2A2020, null, 0, P(110, 300), P(914, 300), P(960, 270), P(512, 252), P(64, 270));
        dc.FillRectangle(Brush(0xFFB02A20), new Rect(150, 330, 724, 18));
        // Lanterns swinging.
        for (int i = 0; i < 5; i++)
        {
            double x = 260 + i * 126 + 6 * Math.Sin(_clock * 1.6 + i);
            dc.DrawLine(Pen(0xFF2A2020, 2), new Point(260 + i * 126, 348), new Point(x, 382));
            dc.DrawEllipse(Brush(0xFFE84A2A), Pen(0xFF2A2020, 2), new Point(x, 398), 13, 17);
        }
        Crowd(dc, 610, 0xFF3A3A40, 3, 24);
        dc.FillRectangle(Brush(0xFF9A9088), new Rect(0, 600, W, 168));
        for (int r = 0; r < 4; r++)
            for (int i = 0; i < 12; i++)
                dc.DrawRectangle(null, Pen(0xFF7A7068, 2), new Rect(i * 96 - (r % 2) * 48, 600 + r * 42, 96, 42));
    }

    private void Cliffs(DrawingContext dc)
    {
        Sky(dc, 0xFF5A6A7A, 0xFFB8C0C0, 460);
        for (int i = 0; i < 7; i++)
        {
            uint h = Hash(i, 3, 7);
            double x = ((h % 1300) + _clock * 12) % 1300 - 160, y = 60 + h / 9 % 200;
            for (int k = 0; k < 5; k++)
                dc.DrawEllipse(Brush(0x90E8ECEC), null, new Point(x + k * 30, y + Math.Sin(k * 1.9) * 9), 38 - Math.Abs(k - 2) * 7, 20);
        }
        for (int i = 0; i < 12; i++) dc.FillRectangle(Brush(Mix(0xFF4A6A7A, 0xFF1A3040, i / 11.0)), new Rect(0, 460 + i * 12, W, 13));
        for (int i = 0; i < 8; i++)
        {
            double x = (i * 140 + _clock * 30) % 1100 - 40;
            dc.DrawLine(Pen(0x80FFFFFF, 3), new Point(x, 500 + i % 3 * 20), new Point(x + 40, 500 + i % 3 * 20));
        }
        // A far headland with a lighthouse.
        Poly(dc, 0xFF2A3A30, null, 0, P(700, 470), P(760, 420), P(900, 410), P(1024, 430), P(1024, 470));
        dc.FillRectangle(Brush(0xFFE8E8E0), new Rect(840, 360, 16, 52));
        if ((int)(_clock * 1.5) % 2 == 0) dc.DrawEllipse(Brush(0xA0FFF0A0), null, new Point(848, 360), 14, 8);
        // The grass of the cliff top, and stones.
        Poly(dc, 0xFF3A5A2A, null, 0, P(0, 580), P(1024, 590), P(1024, 768), P(0, 768));
        dc.FillRectangle(Brush(0xFF4A6A30), new Rect(0, 600, W, 168));
        for (int i = 0; i < 6; i++) dc.DrawEllipse(Brush(0xFF8A8A80), Pen(0xFF4A4A44, 2), new Point(80 + i * 180, 690 + (i % 2) * 30), 26, 12);
        Crowd(dc, 600, 0xFF26302A, 4, 18);
        // A fine drizzle.
        for (int i = 0; i < 70; i++)
        {
            uint h = Hash(i, 3, 2);
            double x = (h % 1024 + _clock * 60) % 1024, y = (h / 1024 % 768 + _clock * 520) % 768;
            dc.DrawLine(Pen(0x50E0E8F0, 1.5), new Point(x, y), new Point(x - 4, y + 14));
        }
    }

    private void Ring(DrawingContext dc)
    {
        Sky(dc, 0xFF020204, 0xFF141018);
        // Spotlights swinging over the crowd.
        for (int i = 0; i < 3; i++)
        {
            double x = 512 + 380 * Math.Sin(_clock * 0.5 + i * 2.1);
            Poly(dc, 0x18FFF0D0, null, 0, P(200 + i * 312, 0), P(232 + i * 312, 0), P(x + 90, 640), P(x - 90, 640));
        }
        Crowd(dc, 430, 0xFF100C14, 5, 40);
        Crowd(dc, 500, 0xFF18121C, 6, 36);
        // The ring: its apron, posts and steel ropes.
        dc.FillRectangle(Brush(0xFF2A2A34), new Rect(0, 600, W, 168));
        dc.FillRectangle(Brush(0xFF3A3A48), new Rect(0, 600, W, 14));
        foreach (double x in new[] { 16.0, 1008.0 }) dc.FillRectangle(Brush(0xFF8A8A9A), new Rect(x - 9, 420, 18, 200));
        for (int r = 0; r < 3; r++)
        {
            double y = 450 + r * 50;
            dc.DrawLine(Pen(0xFF101014, 7), new Point(16, y), new Point(1008, y));
            dc.DrawLine(Pen(0xFFB8B8C8, 4), new Point(16, y), new Point(1008, y));
        }
        Text(dc, "STEEL RING", new Point(512, 690), 70, 0xFF4A4A5A, center: true, shadow: false);
    }

    // ───────────────────────────── the title and the choosing ─────────────────────────────

    private readonly Fighter _demoA = new() { C = Roster.Kai, Side = 0, X = 380, Facing = 1 };
    private readonly Fighter _demoB = new() { C = Roster.Orla, Side = 1, X = 650, Facing = -1 };

    /// <summary>Two fighters sparring in a loop: a jab and a block, then the other way round.</summary>
    private void Spar()
    {
        int t = (int)(_clock * 60) % 240;
        var jab = Roster.Kai.Moves.First(m => m.Name == "HIGH KICK");
        var spark = Roster.Orla.Moves.First(m => m.Name == "STRAIGHT");
        void Set(Fighter f, FighterState s, Move? m, int frame) { f.State = s; f.Move = m; f.Frame = frame; }
        if (t < 60) { Set(_demoA, FighterState.Attack, jab, t / 2); Set(_demoB, t > 12 ? FighterState.BlockStun : FighterState.Idle, null, t); }
        else if (t < 120) { Set(_demoA, FighterState.Idle, null, t); Set(_demoB, FighterState.Idle, null, t); }
        else if (t < 180) { Set(_demoB, FighterState.Attack, spark, (t - 120) / 2); Set(_demoA, t > 135 ? FighterState.HitStun : FighterState.Idle, null, t); _demoA.Stun = 8; }
        else { Set(_demoA, FighterState.Idle, null, t); Set(_demoB, FighterState.Idle, null, t); }
    }

    private void DrawTitle(DrawingContext dc)
    {
        DrawStage(dc, 4);
        Spar();
        DrawFighter(dc, _demoA);
        DrawFighter(dc, _demoB);
        dc.FillRectangle(Brush(0x60000000), new Rect(0, 0, W, H));
        Text(dc, "A V A", new Point(512, 70), 40, Dim, center: true);
        Text(dc, "STEEL RING", new Point(512, 150), 140, Yellow, center: true);
        Text(dc, "A ONE-ON-ONE FIGHTING GAME", new Point(512, 236), 34, White, center: true);
        Text(dc, "FOUR FIGHTERS, EACH WITH THEIR OWN SPECIAL MOVES AND A SUPER", new Point(512, 280), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(512, 350), 46, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 676, W, 92));
        Text(dc, "PLAYER 1:  W A S D MOVE   U I PUNCH   J K KICK", new Point(512, 696), 22, 0xFFE0D0B0, center: true);
        Text(dc, "PLAYER 2:  ARROWS MOVE   NUMPAD 4 5 PUNCH   NUMPAD 1 2 KICK", new Point(512, 722), 22, 0xFFE0D0B0, center: true);
        Text(dc, "HOLD BACK TO BLOCK (DOWN-BACK FOR LOW ATTACKS)   P: PAUSE", new Point(512, 748), 20, Dim, center: true);
    }

    private readonly Fighter[] _portraits = Roster.All.Select((c, i) => new Fighter { C = c, Side = i, Facing = 1 }).ToArray();

    private static string Specials(Character c) => string.Join("   ", c.Moves.Where(m => m.Special).Select(m => $"{m.Name}: {MotionText(m)}"));

    public static string MotionText(Move m) => (m.Motion switch
    {
        Motion.QuarterForward => "D DF F",
        Motion.QuarterBack => "D DB B",
        Motion.DragonPunch => "F D DF",
        _ => "D DF F D DF F",
    }) + " + " + ((m.Button & Buttons.AnyPunch) != 0 ? "P" : "K");

    private void DrawSelect(DrawingContext dc)
    {
        var g = Engine;
        DrawStage(dc, 4);
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, W, H));
        Text(dc, "CHOOSE YOUR FIGHTER", new Point(512, 50), 56, Yellow, center: true);
        for (int i = 0; i < Roster.All.Count; i++)
        {
            var c = Roster.All[i];
            var r = new Rect(48 + i * 240, 90, 208, 300);
            bool one = i == g.Pick1, two = g.Mode == Mode.Versus && i == g.Pick2;
            dc.FillRectangle(Brush(Shade(c.Gi, one || two ? 0.5 : 0.25)), r, 8);
            if (one) dc.DrawRectangle(null, Pen(0xFF4AB8FF, 5), r, 8, 8);
            if (two) dc.DrawRectangle(null, Pen(0xFFFF5A4A, 5), r.Deflate(one ? 7 : 0), 8, 8);
            var f = _portraits[i];
            f.X = r.Center.X;
            f.Y = r.Bottom - 70;
            f.State = one || two ? FighterState.Idle : FighterState.Idle;
            DrawFighter(dc, f);
            Text(dc, c.Name, new Point(r.Center.X, r.Bottom - 46), 40, White, center: true);
            Text(dc, c.From, new Point(r.Center.X, r.Bottom - 18), 24, Dim, center: true);
            if (one) Text(dc, "1P", new Point(r.Left + 22, r.Top + 22), 30, 0xFF4AB8FF, center: true);
            if (two) Text(dc, "2P", new Point(r.Right - 22, r.Top + 22), 30, 0xFFFF5A4A, center: true);
        }
        var picked = Roster.All[g.SelectRow == 2 && g.Mode == Mode.Versus ? g.Pick2 : g.Pick1];
        Text(dc, picked.Style, new Point(512, 414), 26, Cyan, center: true);
        Text(dc, Specials(picked), new Point(512, 444), 18, White, center: true);

        var rows = new List<(string Label, string Value)>
        {
            ("MODE", g.Mode == Mode.Arcade ? "ONE PLAYER" : "TWO PLAYERS"),
            ("PLAYER 1", Roster.All[g.Pick1].Name),
            g.Mode == Mode.Arcade ? ("COMPUTER", g.Skill.ToString().ToUpperInvariant()) : ("PLAYER 2", Roster.All[g.Pick2].Name),
            ("", "FIGHT!"),
        };
        Card(dc, new Rect(232, 470, 560, 250));
        for (int r = 0; r < rows.Count; r++)
        {
            double y = 506 + r * 52;
            bool on = r == g.SelectRow;
            if (on) dc.FillRectangle(Brush(0xFF3A2A20), new Rect(262, y - 22, 500, 44), 6);
            if (rows[r].Label.Length > 0)
            {
                Text(dc, rows[r].Label, new Point(290, y), 34, White);
                Text(dc, (on ? "< " : "") + rows[r].Value + (on ? " >" : ""), new Point(736, y), 34, on ? Yellow : White, right: true);
            }
            else Text(dc, rows[r].Value, new Point(512, y), 44, on && (int)(_clock * 4) % 2 == 0 ? White : Yellow, center: true);
        }
        Text(dc, "UP/DOWN: CHOOSE   LEFT/RIGHT: CHANGE   ENTER: FIGHT", new Point(512, 746), 22, Dim, center: true);
    }

    // ───────────────────────────── scores ─────────────────────────────

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(182, 70, 660, 640));
        Text(dc, "HIGH SCORES", new Point(512, 124), 64, Yellow, center: true);
        Text(dc, "RANK NAME     SCORE   BOUT", new Point(512, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9:N0}   {e.Stage,4}", new Point(512, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(512, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, W, H));
        Card(dc, new Rect(182, 140, 660, 460));
        Text(dc, "A NEW HIGH SCORE!", new Point(512, 200), 64, Yellow, center: true);
        Text(dc, $"{g.Score:N0}", new Point(512, 270), 52, Cyan, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(512, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(452 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(430 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(512, 560), 26, White, center: true);
    }
}
