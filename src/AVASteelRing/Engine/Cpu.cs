namespace AVASteelRing.Engine;

/// <summary>
/// The computer at the controls of one fighter. It sees what a person sees (where the other fighter is, what they're
/// doing and how far into it), waits a person's reaction time before it can answer an attack, and puts in its
/// specials as stick motions, frame by frame, the same way the keys do. <see cref="Skill"/> runs from 0 to 1: it
/// shortens the reaction time and makes good choices likelier.
/// </summary>
public sealed class Cpu(double skill, Random random)
{
    private readonly Queue<Controls> _plan = new();
    private Controls _hold;
    private int _holdFrames;
    private bool _blocking, _airDone;

    public double Skill { get; set; } = Math.Clamp(skill, 0, 1);

    /// <summary>Frames an attack must have been showing before the computer can react to it.</summary>
    public int Reaction => (int)Math.Round(14 - 10 * Skill);

    public Controls Next(Fight fight, Fighter me)
    {
        var o = fight.Other(me);
        int f = me.Facing;
        switch (me.State)
        {
            case FighterState.HitStun or FighterState.Knockdown or FighterState.GetUp or FighterState.Ko or FighterState.Win:
                _plan.Clear();
                _holdFrames = 0;
                _blocking = false;
                return default;
            case FighterState.BlockStun:
                return _hold;
            case FighterState.Air:
                if (me.Frame < 3) _airDone = false;
                // A jump-in: kick on the way down, near enough to land it.
                if (!_airDone && me.Vy > -2 && Math.Abs(o.X - me.X) < 150 && random.NextDouble() < 0.35 + 0.5 * Skill)
                {
                    _airDone = true;
                    return new Controls(0, 0, Buttons.HeavyKick);
                }
                return default;
            case FighterState.Attack:
                return _plan.Count > 0 ? _plan.Dequeue() : default;
        }

        if (_plan.Count > 0) return _plan.Dequeue();

        // Blocking: keep holding back until the danger is past.
        if (Threat(fight, me, o) is { } threat)
        {
            if (_blocking || random.NextDouble() < 0.15 + 0.75 * Skill)
            {
                _blocking = true;
                // A projectile far enough away can be jumped instead.
                if (threat.Projectile && Math.Abs(threat.X - me.X) is > 130 and < 220 && random.NextDouble() < 0.3 * Skill)
                {
                    _blocking = false;
                    return new Controls(f, 1);
                }
                _hold = new Controls(-f, threat.Low ? -1 : 0);
                return _hold;
            }
        }
        else _blocking = false;

        if (_holdFrames > 0)
        {
            _holdFrames--;
            return _hold;
        }

        double dx = (o.X - me.X) * f;
        var c = me.C;

        // Anti-air: a fighter dropping in gets the rising special, or an uppercut.
        if (o.State is FighterState.Air or FighterState.Attack && !o.Grounded && dx < 190 && o.Vy > -3 && random.NextDouble() < 0.2 + 0.6 * Skill)
        {
            if (Find(c, Motion.DragonPunch) is { Rise: > 0 } dp) { Plan(me, Motion.DragonPunch, dp.Button); return _plan.Dequeue(); }
            return Press(Buttons.HeavyPunch, crouch: true);
        }

        // Down: walk in to stand over them, ready.
        if (o.Down)
        {
            if (dx > 120) return Walk(f, 6);
            return Wait();
        }

        // A full meter in range is the super.
        if (me.Meter >= Fighter.MaxMeter && Find(c, Motion.DoubleQuarterForward) is { } super &&
            (super.Projectile ? dx < 600 : dx < 200) && random.NextDouble() < 0.04 + 0.08 * Skill)
        {
            Plan(me, Motion.DoubleQuarterForward, super.Button);
            return _plan.Dequeue();
        }

        var projectile = c.Moves.FirstOrDefault(m => m.Projectile && m.Cost == 0);
        if (dx > 300)
        {
            if (projectile != null && !fight.Projectiles.Any(p => p.Owner == me) && random.NextDouble() < 0.04 + 0.06 * Skill)
            {
                Plan(me, projectile.Motion, projectile.Button);
                return _plan.Dequeue();
            }
            if (random.NextDouble() < 0.012) return Jump(f);
            return Walk(f, 8 + random.Next(16));
        }

        if (dx > 150)
        {
            double roll = random.NextDouble();
            // The lunging specials close the gap.
            var lunge = c.Moves.FirstOrDefault(m => m.Special && m.Cost == 0 && m.Lunge >= 6);
            if (lunge != null && dx < 280 && roll < 0.025 + 0.03 * Skill) { Plan(me, lunge.Motion, lunge.Button); return _plan.Dequeue(); }
            if (projectile != null && !fight.Projectiles.Any(p => p.Owner == me) && roll < 0.05) { Plan(me, projectile.Motion, projectile.Button); return _plan.Dequeue(); }
            if (roll < 0.07 && dx < 260) return Jump(f);
            if (roll < 0.1) return Walk(-f, 6 + random.Next(10));
            return Walk(f, 4 + random.Next(10));
        }

        // Close in: an attack, a combo, or a step back and wait.
        double pick = random.NextDouble();
        if (pick < 0.12) return Walk(-f, 6 + random.Next(8));
        if (pick < 0.25) return Wait();
        if (pick < 0.25 + 0.3 * Skill && Find(c, Motion.QuarterForward) is { } follow && !follow.Projectile | !fight.Projectiles.Any(p => p.Owner == me))
        {
            // A low kick, cancelled into a special as it lands.
            _plan.Enqueue(new Controls(0, -1, Buttons.LightKick));
            for (int i = 0; i < 3; i++) _plan.Enqueue(new Controls(0, -1));
            Stick(f, Engine.Motion.QuarterForward, follow.Button, 2);
            return _plan.Dequeue();
        }
        if (pick < 0.62)
        {
            var b = random.Next(4) switch { 0 => Buttons.LightPunch, 1 => Buttons.LightKick, 2 => Buttons.HeavyPunch, _ => Buttons.HeavyKick };
            return Press(b, crouch: random.NextDouble() < 0.4);
        }
        if (pick < 0.75 && Find(c, Motion.DragonPunch) is { } rising) { Plan(me, Motion.DragonPunch, rising.Button); return _plan.Dequeue(); }
        if (pick < 0.85 && Find(c, Motion.QuarterBack) is { } back) { Plan(me, Motion.QuarterBack, back.Button); return _plan.Dequeue(); }
        return Press(Buttons.HeavyKick, crouch: true);
    }

    private static Move? Find(Character c, Motion m) => c.Moves.FirstOrDefault(x => x.Motion == m);

    private (bool Projectile, bool Low, double X)? Threat(Fight fight, Fighter me, Fighter o)
    {
        if (o.State == FighterState.Attack && o.Move is { } m && o.Frame >= Reaction && o.Frame < m.Startup + m.Active + 2 && !m.Projectile)
        {
            double reach = (m.BoxX + m.BoxW) * o.S + m.Lunge * (m.Startup + m.Active) + 50;
            if (Math.Abs(o.X - me.X) < reach) return (false, m.Height == Height.Low, o.X);
        }

        foreach (var p in fight.Projectiles)
        {
            if (p.Owner == me || p.Age < Reaction) continue;
            double ahead = (p.X - me.X) * -Math.Sign(p.Vx);
            if (ahead is > 0 and < 320) return (true, p.Move.Height == Height.Low, p.X);
        }
        return null;
    }

    private Controls Walk(int dir, int frames)
    {
        _hold = new Controls(dir, 0);
        _holdFrames = frames;
        return _hold;
    }

    private Controls Wait()
    {
        _hold = default;
        _holdFrames = random.Next(4, 4 + (int)(24 - 18 * Skill));
        return _hold;
    }

    private Controls Jump(int dir)
    {
        _hold = default;
        _holdFrames = 0;
        return new Controls(dir, 1);
    }

    private Controls Press(Buttons b, bool crouch)
    {
        // One frame down first so the crouching version comes out.
        if (crouch) { _plan.Enqueue(new Controls(0, -1, b)); _plan.Enqueue(new Controls(0, -1)); return new Controls(0, -1); }
        _plan.Enqueue(default);
        return new Controls(0, 0, b);
    }

    private void Plan(Fighter me, Motion m, Buttons b)
    {
        // Pick one button out of a move's "any punch" or "any kick".
        Stick(me.Facing, m, b, random.Next(1, 3));
    }

    /// <summary>The stick motion for a special, two frames a direction (or <paramref name="step"/>), the button on the last.</summary>
    private void Stick(int f, Motion m, Buttons any, int step)
    {
        var b = (any & Buttons.HeavyPunch) != 0 ? Buttons.HeavyPunch : (any & Buttons.HeavyKick) != 0 ? Buttons.HeavyKick : any;
        (int X, int Y)[] dirs = m switch
        {
            Engine.Motion.QuarterForward => [(0, -1), (f, -1), (f, 0)],
            Engine.Motion.QuarterBack => [(0, -1), (-f, -1), (-f, 0)],
            Engine.Motion.DragonPunch => [(f, 0), (0, -1), (f, -1)],
            Engine.Motion.DoubleQuarterForward => [(0, -1), (f, -1), (f, 0), (0, -1), (f, -1), (f, 0)],
            _ => [(0, 0)],
        };
        for (int i = 0; i < dirs.Length; i++)
            for (int k = 0; k < step; k++)
                _plan.Enqueue(new Controls(dirs[i].X, dirs[i].Y, i == dirs.Length - 1 && k == step - 1 ? b : Buttons.None));
        _plan.Enqueue(new Controls(dirs[^1].X, dirs[^1].Y));
        _plan.Enqueue(default);
    }
}
