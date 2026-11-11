namespace AVASteelRing.Engine;

/// <summary>One frame of a fighter's controls: the stick (screen left −1, right +1; up +1, down −1) and the buttons held.</summary>
public readonly record struct Controls(int X = 0, int Y = 0, Buttons Held = Buttons.None);

public enum FighterState { Idle, Walk, Crouch, Air, Attack, HitStun, BlockStun, Knockdown, GetUp, Ko, Win }

/// <summary>Sounds the game asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Swish, SwishHeavy, HitLight, HitHeavy, Block, Fireball, Special, Super, Land, Knockdown, Ko, Bell, Round, Select, Perfect,
    TitleTune, Victory, Defeat, Music, MusicStop,
}

public sealed class Fighter
{
    public const double Ground = 640;
    public const int MaxHealth = 1000, MaxMeter = 100;

    public required Character C;
    public required int Side;
    public double X, Y = Ground, Vx, Vy;
    /// <summary>+1 facing right, −1 facing left.</summary>
    public int Facing = 1;
    public FighterState State;
    public Move? Move;
    /// <summary>Frames into the current move or state.</summary>
    public int Frame;
    public int Stun;
    public int Health = MaxHealth;
    public int Meter;
    /// <summary>How many hits of the current move have landed.</summary>
    public int Landed;
    /// <summary>The hits taken in a row without getting free, for the combo counter.</summary>
    public int ComboTaken;
    public double Slide;
    public Buttons Last;
    /// <summary>The stick's last 40 frames, as numpad directions relative to facing (6 forward, 2 down, 4 back).</summary>
    public readonly Queue<int> History = new();
    public int Rounds;

    /// <summary>How big this fighter is drawn and how far their boxes reach.</summary>
    public double S => C.Scale * 1.3;
    public bool Grounded => Y >= Ground - 0.01;
    public bool Down => State is FighterState.Knockdown or FighterState.GetUp or FighterState.Ko;
    public bool Free => State is FighterState.Idle or FighterState.Walk or FighterState.Crouch;

    /// <summary>The box that can be hit, in screen space: none while down or getting up.</summary>
    public (double L, double T, double R, double B)? Hurtbox
    {
        get
        {
            if (Down || State == FighterState.Win) return null;
            bool crouch = State == FighterState.Crouch || State == FighterState.BlockStun && _crouchBlock ||
                          State == FighterState.Attack && Move?.Stance == Stance.Crouching;
            double h = (crouch ? 98 : State == FighterState.Air ? 130 : 150) * S, w = 24 * S;
            double top = Y - h, bottom = State == FighterState.Air ? Y - 30 * S : Y;
            return (X - w, top, X + w, bottom);
        }
    }

    internal bool _crouchBlock;

    /// <summary>The attack's box while it's out, in screen space.</summary>
    public (double L, double T, double R, double B)? Hitbox
    {
        get
        {
            if (State != FighterState.Attack || Move is not { Projectile: false } m) return null;
            if (Frame < m.Startup || Frame >= m.Startup + m.Active) return null;
            // Several hits are spread over the active frames.
            int every = Math.Max(1, m.Active / m.Hits);
            int due = Math.Min(m.Hits, (Frame - m.Startup) / every + 1);
            if (Landed >= due) return null;
            double x0 = X + Facing * m.BoxX * S, x1 = X + Facing * (m.BoxX + m.BoxW) * S;
            return (Math.Min(x0, x1), Y + m.BoxY * S, Math.Max(x0, x1), Y + (m.BoxY + m.BoxH) * S);
        }
    }

    public int Dir(Controls c)
    {
        int fx = c.X * Facing;
        return c.Y > 0 ? (fx > 0 ? 9 : fx < 0 ? 7 : 8) : c.Y < 0 ? (fx > 0 ? 3 : fx < 0 ? 1 : 2) : (fx > 0 ? 6 : fx < 0 ? 4 : 5);
    }

    public bool HoldingBack(Controls c) => c.X * Facing < 0;
}

public sealed class Projectile
{
    public required Fighter Owner;
    public required Move Move;
    public double X, Y, Vx;
    public int HitsLeft, Cooldown, Age;
    public bool Gone;
}

/// <summary>A hit's flash, for show.</summary>
public sealed record Spark(double X, double Y, bool Heavy, bool Blocked, int Born);

/// <summary>
/// A round between two fighters, a frame at a time (60 a second): the moves, special motions read from the stick's
/// history, hits and blocks, stun, knockdowns, combos, projectiles, the meter, the round timer and the KO.
/// </summary>
public sealed class Fight
{
    public const double Left = 50, Right = 974;
    public const int RoundFrames = 60 * 60;

    public Fighter P1 { get; }
    public Fighter P2 { get; }
    public List<Projectile> Projectiles { get; } = [];
    public List<Spark> Sparks { get; } = [];
    public List<Sfx> Heard { get; } = [];
    public int Timer = RoundFrames;
    public int Frames;
    /// <summary>Frames of slow-motion left after a KO.</summary>
    public int Freeze;
    public bool Over { get; private set; }
    public Fighter? Winner { get; private set; }
    public string? Callout { get; private set; }
    public int CalloutFrames;

    public Fight(Character c1, Character c2)
    {
        P1 = new Fighter { C = c1, Side = 0, X = 360, Facing = 1 };
        P2 = new Fighter { C = c2, Side = 1, X = 664, Facing = -1 };
    }

    public Fighter Other(Fighter f) => f == P1 ? P2 : P1;

    public void Tick(Controls c1, Controls c2, bool clock = true)
    {
        Frames++;
        if (CalloutFrames > 0 && --CalloutFrames == 0) Callout = null;
        if (Freeze > 0)
        {
            Freeze--;
            if (Freeze % 3 != 0) return;
        }

        Step(P1, c1);
        Step(P2, c2);
        Separate();
        Projectiles.ForEach(MoveProjectile);
        Projectiles.RemoveAll(p => p.Gone);
        Collide(P1, P2, c2);
        Collide(P2, P1, c1);
        Sparks.RemoveAll(s => Frames - s.Born > 18);
        if (clock && !Over && Timer > 0 && --Timer == 0) TimeUp();
    }

    // ───────────────────────────── a fighter's frame ─────────────────────────────

    private void Step(Fighter f, Controls c)
    {
        var o = Other(f);
        int dir = f.Dir(c);
        f.History.Enqueue(dir);
        if (f.History.Count > 40) f.History.Dequeue();
        var pressed = c.Held & ~f.Last;
        f.Last = c.Held;
        f.Frame++;

        switch (f.State)
        {
            case FighterState.Ko:
            case FighterState.Win:
                Physics(f);
                return;
            case FighterState.Knockdown:
                Physics(f);
                if (f.Grounded && f.Frame > 50) Set(f, FighterState.GetUp);
                return;
            case FighterState.GetUp:
                if (f.Frame > 22) Set(f, FighterState.Idle);
                return;
            case FighterState.HitStun:
            case FighterState.BlockStun:
                Physics(f);
                if (f.Grounded)
                {
                    f.X += f.Slide;
                    f.Slide *= 0.82;
                }
                if (--f.Stun <= 0 && f.Grounded)
                {
                    Set(f, c.Y < 0 ? FighterState.Crouch : FighterState.Idle);
                    o.ComboTaken = 0;
                    f.ComboTaken = 0;
                }
                return;
            case FighterState.Attack:
                Attack(f, c, pressed, o);
                return;
            case FighterState.Air:
                Physics(f);
                if (f.Grounded) { Set(f, FighterState.Idle); Heard.Add(Sfx.Land); return; }
                if (pressed != Buttons.None) StartMove(f, f.C.Moves.First(m => m.Stance == Stance.Air && (m.Button & pressed) != 0));
                return;
        }

        // Free on the ground: face the other fighter, then move, crouch, jump or attack.
        f.Facing = o.X >= f.X ? 1 : -1;
        if (pressed != Buttons.None && TryStart(f, pressed, o)) return;
        if (c.Y > 0)
        {
            f.Vy = -f.C.Jump * 1.15;
            f.Vx = c.X * f.C.Walk * 1.4;
            f.Y -= 1;
            Set(f, FighterState.Air);
            return;
        }

        if (c.Y < 0) { Set(f, FighterState.Crouch, keepFrame: f.State == FighterState.Crouch); return; }
        if (c.X != 0)
        {
            // Backing off is slower than coming in.
            double speed = c.X * f.Facing > 0 ? f.C.Walk : f.C.Walk * 0.75;
            f.X += c.X * speed * 1.2;
            Set(f, FighterState.Walk, keepFrame: f.State == FighterState.Walk);
        }
        else Set(f, FighterState.Idle, keepFrame: f.State == FighterState.Idle);
    }

    private static void Set(Fighter f, FighterState s, bool keepFrame = false)
    {
        if (f.State != s || !keepFrame) f.Frame = 0;
        f.State = s;
        if (s != FighterState.Attack) f.Move = null;
    }

    private static void Physics(Fighter f)
    {
        if (f.Grounded && f.Vy >= 0) { f.Vy = 0; f.Y = Fighter.Ground; return; }
        f.Vy += 0.85;
        f.X += f.Vx;
        f.Y = Math.Min(Fighter.Ground, f.Y + f.Vy);
    }

    /// <summary>The motion the stick has drawn in its recent history, if any.</summary>
    public static Motion ReadMotion(Fighter f)
    {
        var h = f.History.ToArray();
        if (h.Length == 0) return Motion.None;
        // The motion's last direction must be in the last few frames, so an old stray direction doesn't count.
        bool Seq(int window, params int[] dirs)
        {
            int k = dirs.Length - 1;
            int end = -1;
            for (int i = h.Length - 1; i >= Math.Max(0, h.Length - window) && k >= 0; i--)
                if (h[i] == dirs[k])
                {
                    if (k == dirs.Length - 1) end = i;
                    k--;
                }
            return k < 0 && end >= h.Length - 4;
        }

        int last = h[^1];
        if (Seq(36, 2, 3, 6, 2, 3, 6) && last is 6 or 3) return Motion.DoubleQuarterForward;
        // Forward, down, down-forward ends on down-forward; a quarter circle ends on forward.
        if (Seq(18, 6, 2, 3) && last is 3 or 2) return Motion.DragonPunch;
        if (Seq(16, 2, 3, 6) && last is 6 or 3) return Motion.QuarterForward;
        if (Seq(16, 2, 1, 4) && last is 4 or 1) return Motion.QuarterBack;
        return Motion.None;
    }

    private bool TryStart(Fighter f, Buttons pressed, Fighter o)
    {
        var motion = ReadMotion(f);
        if (motion != Motion.None)
        {
            var special = f.C.Moves.FirstOrDefault(m => m.Motion == motion && (m.Button & pressed) != 0 && m.Cost <= f.Meter)
                       ?? (motion == Motion.DoubleQuarterForward ? f.C.Moves.FirstOrDefault(m => m.Motion == Motion.QuarterForward && (m.Button & pressed) != 0) : null);
            if (special != null && !(special.Projectile && Projectiles.Any(p => p.Owner == f)))
            {
                StartMove(f, special);
                return true;
            }
        }

        var stance = f.State == FighterState.Crouch || f.History.LastOrDefault() is 1 or 2 or 3 ? Stance.Crouching : Stance.Standing;
        // Heavy beats light if both are pressed together.
        foreach (var b in new[] { Buttons.HeavyPunch, Buttons.HeavyKick, Buttons.LightPunch, Buttons.LightKick })
        {
            if ((pressed & b) == 0) continue;
            var move = f.C.Moves.FirstOrDefault(m => m.Stance == stance && !m.Special && m.Button == b);
            if (move != null) { StartMove(f, move); return true; }
        }
        _ = o;
        return false;
    }

    private void StartMove(Fighter f, Move m)
    {
        f.Move = m;
        f.State = FighterState.Attack;
        f.Frame = 0;
        f.Landed = 0;
        if (m.Cost > 0)
        {
            f.Meter -= m.Cost;
            Heard.Add(Sfx.Super);
            Freeze = Math.Max(Freeze, 18);
            Call($"{f.C.Name}: {m.Name}!", 70);
        }
        else Heard.Add(m.Special ? Sfx.Special : m.Damage >= 70 ? Sfx.SwishHeavy : Sfx.Swish);
        if (m.Rise > 0) { f.Vy = -m.Rise; f.Y -= 1; }
    }

    private void Attack(Fighter f, Controls c, Buttons pressed, Fighter o)
    {
        var m = f.Move!;
        // Lunges carry the fighter forward through the start and the active frames.
        if (m.Lunge > 0 && f.Frame <= m.Startup + m.Active) f.X += f.Facing * m.Lunge;
        if (m.Stance == Stance.Air || m.Rise > 0 || !f.Grounded)
        {
            Physics(f);
            if (f.Grounded && f.Frame > 2)
            {
                if (m.Rise > 0 && f.Frame < m.Total) { f.Vy = 0; }
                else if (m.Stance == Stance.Air) { Set(f, FighterState.Idle); Heard.Add(Sfx.Land); return; }
            }
        }

        if (m.Projectile && f.Frame == m.Startup)
        {
            Projectiles.Add(new Projectile
            {
                Owner = f, Move = m, X = f.X + f.Facing * m.BoxX * f.S, Y = f.Y + m.BoxY * f.S,
                Vx = f.Facing * (m.Cost > 0 ? 9.5 : 6.5), HitsLeft = m.Hits,
            });
            Heard.Add(Sfx.Fireball);
        }

        // A normal that has connected (or been blocked) can be cut short by a special: that's a combo.
        if (!m.Special && f.Landed > 0 && f.Frame <= m.Startup + m.Active + 8 && pressed != Buttons.None)
        {
            var motion = ReadMotion(f);
            var special = f.C.Moves.FirstOrDefault(s => s.Motion == motion && motion != Motion.None && (s.Button & pressed) != 0 && s.Cost <= f.Meter);
            if (special != null && !(special.Projectile && Projectiles.Any(p => p.Owner == f)))
            {
                StartMove(f, special);
                return;
            }
        }

        if (f.Frame >= m.Total && f.Grounded) Set(f, c.Y < 0 ? FighterState.Crouch : FighterState.Idle);
        _ = o;
    }

    /// <summary>Two fighters can't stand in one place: they push each other apart, and the walls stop both.</summary>
    private void Separate()
    {
        foreach (var f in new[] { P1, P2 }) f.X = Math.Clamp(f.X, Left, Right);
        if (P1.Down || P2.Down) return;
        double min = 30 * (P1.S + P2.S);
        double dx = P2.X - P1.X;
        if (Math.Abs(dx) >= min || Math.Abs(P1.Y - P2.Y) > 110) return;
        double push = (min - Math.Abs(dx)) / 2 * Math.Sign(dx == 0 ? (P1.Side == 0 ? 1 : -1) : dx);
        P1.X -= push;
        P2.X += push;
        foreach (var f in new[] { P1, P2 }) f.X = Math.Clamp(f.X, Left, Right);
        // In a corner the one against the wall stays, and the other is pushed out.
        if (Math.Abs(P2.X - P1.X) < min)
        {
            if (P1.X <= Left + 0.1 || P2.X >= Right - 0.1) { if (P1.X <= Left + 0.1) P2.X = P1.X + min; else P1.X = P2.X - min; }
            else if (P2.X <= Left + 0.1) P1.X = P2.X + min;
            else if (P1.X >= Right - 0.1) P2.X = P1.X - min;
        }
    }

    private void MoveProjectile(Projectile p)
    {
        p.Age++;
        p.X += p.Vx;
        if (p.Cooldown > 0) p.Cooldown--;
        if (p.X < 0 || p.X > 1024) p.Gone = true;
        // Two projectiles meeting cancel out.
        foreach (var q in Projectiles)
            if (q != p && q.Owner != p.Owner && !q.Gone && Math.Abs(q.X - p.X) < 30 && Math.Abs(q.Y - p.Y) < 40)
            {
                p.Gone = q.Gone = true;
                Sparks.Add(new Spark((p.X + q.X) / 2, p.Y, true, true, Frames));
                Heard.Add(Sfx.Block);
            }
    }

    private void Collide(Fighter a, Fighter d, Controls dc)
    {
        if (a.Hitbox is { } hb && d.Hurtbox is { } hu && Overlap(hb, hu))
        {
            a.Landed++;
            Land(a, d, a.Move!, dc, (hb.L + hb.R) / 2, (Math.Max(hb.T, hu.T) + Math.Min(hb.B, hu.B)) / 2);
        }

        foreach (var p in Projectiles)
        {
            if (p.Owner != a || p.Gone || p.Cooldown > 0 || d.Hurtbox is not { } box) continue;
            double r = 20 * (p.Move.Cost > 0 ? 1.8 : 1);
            if (p.X + r < box.L || p.X - r > box.R || p.Y + r < box.T || p.Y - r > box.B) continue;
            Land(a, d, p.Move, dc, p.X, p.Y, p.HitsLeft > 1);
            if (--p.HitsLeft <= 0) p.Gone = true;
            else p.Cooldown = 5;
        }
    }

    private static bool Overlap((double L, double T, double R, double B) a, (double L, double T, double R, double B) b) =>
        a.L < b.R && b.L < a.R && a.T < b.B && b.T < a.B;

    /// <summary>A hit arrives: blocked if the defender holds back (crouching for a low, standing for an overhead), or it lands.</summary>
    private void Land(Fighter a, Fighter d, Move m, Controls dc, double x, double y, bool more = false)
    {
        int share = Math.Max(1, m.Hits);
        bool canBlock = d.Free || d.State == FighterState.BlockStun;
        bool crouching = dc.Y < 0;
        bool blocked = canBlock && d.Grounded && d.HoldingBack(dc) &&
                       (m.Height == Height.Mid || m.Height == Height.Low && crouching || m.Height == Height.Overhead && !crouching);
        if (blocked)
        {
            d.State = FighterState.BlockStun;
            d.Frame = 0;
            d.Stun = m.BlockStun;
            d._crouchBlock = crouching;
            d.Slide = -d.Facing * m.Push * 0.8;
            // Specials chip a little through a block.
            if (m.Special) Hurt(d, Math.Max(1, m.Damage / share / 8), a);
            a.Meter = Math.Min(Fighter.MaxMeter, a.Meter + 2);
            Sparks.Add(new Spark(x, y, m.Damage > 60, true, Frames));
            Heard.Add(Sfx.Block);
            return;
        }

        // Each hit in a row is worth a bit less, so a combo can't do everything.
        double scale = Math.Max(0.35, 1 - 0.12 * d.ComboTaken);
        int damage = (int)Math.Round(m.Damage / (double)share * scale / d.C.Toughness);
        d.ComboTaken++;
        if (d.ComboTaken >= 2) Call($"{d.ComboTaken} HITS", 50);
        Hurt(d, damage, a);
        a.Meter = Math.Min(Fighter.MaxMeter, a.Meter + damage / 9 + 1);
        d.Meter = Math.Min(Fighter.MaxMeter, d.Meter + damage / 18);
        Sparks.Add(new Spark(x, y, m.Damage > 60, false, Frames));
        Heard.Add(m.Damage / share >= 50 ? Sfx.HitHeavy : Sfx.HitLight);
        if (d.State == FighterState.Ko) return;

        bool down = (m.Knockdown && !more) || !d.Grounded;
        if (down)
        {
            d.State = FighterState.Knockdown;
            d.Frame = 0;
            d.Move = null;
            d.Vy = -9;
            d.Vx = -d.Facing * 3.5;
            d.Y -= 1;
            Heard.Add(Sfx.Knockdown);
        }
        else
        {
            d.State = FighterState.HitStun;
            d.Frame = 0;
            d.Move = null;
            d.Stun = m.HitStun;
            d.Slide = -d.Facing * m.Push;
        }
    }

    private void Hurt(Fighter d, int damage, Fighter by)
    {
        d.Health = Math.Max(0, d.Health - damage);
        if (d.Health > 0 || Over) return;
        d.State = FighterState.Ko;
        d.Frame = 0;
        d.Move = null;
        d.Vy = -11;
        d.Vx = -d.Facing * 4;
        d.Y -= 1;
        Over = true;
        Winner = by;
        Freeze = 90;
        Heard.Add(Sfx.Ko);
        Call("K.O.", 150);
    }

    private void TimeUp()
    {
        Over = true;
        Winner = P1.Health == P2.Health ? null : P1.Health > P2.Health ? P1 : P2;
        Call("TIME", 150);
        Heard.Add(Sfx.Bell);
    }

    public void Call(string text, int frames)
    {
        Callout = text;
        CalloutFrames = frames;
    }

    /// <summary>For tests: put a fighter at a place, free to act.</summary>
    public static void Place(Fighter f, double x)
    {
        f.X = x;
        f.Y = Fighter.Ground;
        f.Vx = f.Vy = 0;
        f.State = FighterState.Idle;
        f.Move = null;
        f.Frame = 0;
    }
}
