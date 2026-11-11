using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASteelRing.Engine;

namespace AVASteelRing.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASteelRing/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Shade(uint c, double k)
    {
        byte r = (byte)Math.Clamp(((c >> 16) & 0xFF) * k, 0, 255), g = (byte)Math.Clamp(((c >> 8) & 0xFF) * k, 0, 255), b = (byte)Math.Clamp((c & 0xFF) * k, 0, 255);
        return 0xFF000000 | (uint)r << 16 | (uint)g << 8 | b;
    }

    private static uint Alpha(uint c, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (c & 0xFFFFFF);

    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int s) => (uint)(((a >> s) & 0xFF) + ((((b >> s) & 0xFF) - (double)((a >> s) & 0xFF)) * t));
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    // ───────────────────────────── the fighters ─────────────────────────────

    /// <summary>
    /// A body's key points, in its own space: feet at the origin, facing right, y down, before scaling. Elbows and knees
    /// are found from these by <see cref="Ik"/>, so the limbs keep their length whatever the pose.
    /// </summary>
    private readonly record struct Pose(Point Hip, Point Chest, Point Head, Point FHand, Point BHand, Point FFoot, Point BFoot)
    {
        public static Pose Lerp(Pose a, Pose b, double t)
        {
            static Point L(Point p, Point q, double t) => new(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
            return new Pose(L(a.Hip, b.Hip, t), L(a.Chest, b.Chest, t), L(a.Head, b.Head, t), L(a.FHand, b.FHand, t),
                L(a.BHand, b.BHand, t), L(a.FFoot, b.FFoot, t), L(a.BFoot, b.BFoot, t));
        }

        /// <summary>The whole body turned about the hip (negative tips the head back).</summary>
        public Pose Rotate(double a)
        {
            var o = Hip;
            Point R(Point p)
            {
                double x = p.X - o.X, y = p.Y - o.Y, c = Math.Cos(a), s = Math.Sin(a);
                return new Point(o.X + x * c - y * s, o.Y + x * s + y * c);
            }
            return new Pose(Hip, R(Chest), R(Head), R(FHand), R(BHand), R(FFoot), R(BFoot));
        }
    }

    private static Point P(double x, double y) => new(x, y);

    private static Pose Stand(double bob) => new(P(0, -78 + bob), P(6, -128 + bob), P(10, -146 + bob), P(36, -124 + bob), P(20, -114 + bob), P(22, 0), P(-22, 0));
    private static readonly Pose Crouch = new(P(0, -44), P(10, -90), P(16, -106), P(36, -94), P(22, -86), P(28, 0), P(-24, 0));
    private static readonly Pose Lying = new(P(0, -12), P(-50, -14), P(-70, -16), P(-36, -4), P(-62, -6), P(46, -6), P(40, -2));

    private static Pose Airborne(Fighter f) => f.Vy < 0
        ? new Pose(P(0, -88), P(4, -134), P(8, -152), P(30, -126), P(16, -116), P(16, -44), P(-10, -52))
        : new Pose(P(0, -84), P(6, -130), P(10, -148), P(32, -118), P(18, -110), P(18, -24), P(-14, -30));

    private static Pose Walk(int frame, double toward)
    {
        double ph = frame * 0.22 * Math.Sign(toward == 0 ? 1 : toward), s = Math.Sin(ph), c = Math.Cos(ph);
        var p = Stand(Math.Abs(s) * 2);
        return p with { FFoot = P(22 + 12 * s, -Math.Max(0, c) * 10), BFoot = P(-22 - 12 * s, -Math.Max(0, -c) * 10) };
    }

    /// <summary>How far an attack is thrown out: up through the startup, held while it's live, back during recovery.</summary>
    private static double Extension(Move m, int frame) =>
        frame < m.Startup ? (double)frame / Math.Max(1, m.Startup)
        : frame < m.Startup + m.Active ? 1
        : Math.Max(0, 1 - (double)(frame - m.Startup - m.Active) / Math.Max(1, m.Recovery));

    private static Pose Struck(Pose b, Move m, int frame)
    {
        var t = P(m.BoxX + m.BoxW * 0.75, m.BoxY + m.BoxH / 2);
        Point Add(Point p, double x, double y) => new(p.X + x, p.Y + y);
        return m.Look switch
        {
            Look.Jab or Look.LowPunch or Look.AirPunch => b with { FHand = t, Chest = Add(b.Chest, 8, 2), Head = Add(b.Head, 6, 2) },
            Look.Straight => b with { BHand = t, Chest = Add(b.Chest, 14, 3), Head = Add(b.Head, 14, 3), FHand = P(14, b.Chest.Y + 10), BFoot = Add(b.BFoot, -8, 0) },
            Look.Uppercut => b with { Hip = P(2, -72), Chest = P(12, -130), Head = P(14, -148), BHand = P(m.BoxX + m.BoxW / 2, m.BoxY + 12), FHand = P(22, -112), FFoot = P(22, 0), BFoot = P(-20, 0) },
            Look.LowKick => b with { FFoot = t, Chest = Add(b.Chest, -8, 0), Head = Add(b.Head, -10, 0) },
            Look.HighKick => b with { FFoot = t, Hip = Add(b.Hip, -4, -2), Chest = P(-14, -120), Head = P(-22, -134), BFoot = P(-8, 0) },
            Look.SpinKick => b with { FFoot = t, Hip = Add(b.Hip, -2, -4), Chest = P(-16, -122), Head = P(-24, -136), BFoot = P(-6, 0), FHand = P(-20, -110), BHand = P(-30, -120) },
            Look.Sweep => b with { Hip = P(-4, -34), Chest = P(-26, -74), Head = P(-34, -90), FFoot = t, BHand = P(-30, -6), FHand = P(-6, -46), BFoot = P(-30, 0) },
            Look.AirKick => b with { FFoot = t, BFoot = P(-14, -56), Chest = Add(b.Chest, -6, 4), FHand = P(-6, -110) },
            Look.Fireball or Look.Super => b with { Hip = P(0, -72), Chest = P(12, -120), Head = P(16, -138), FHand = P(m.BoxX - 4, m.BoxY), BHand = P(m.BoxX - 10, m.BoxY + 10), FFoot = P(36, 0), BFoot = P(-32, 0) },
            Look.RisingFist => b with { Hip = P(2, -86), Chest = P(8, -140), Head = P(0, -156), FHand = P(18, -204), BHand = P(10, -112), FFoot = P(18, -42), BFoot = P(-10, 0) },
            Look.Charge => b with { Hip = P(0, -74), Chest = P(26, -116), Head = P(38, -128), FHand = P(46, -110), BHand = P(30, -102), FFoot = P(30, 0), BFoot = P(-36, 0) },
            _ => b,
        };
    }

    private static Pose PoseOf(Fighter f, double clock, out bool mirror)
    {
        mirror = false;
        int fr = f.Frame;
        switch (f.State)
        {
            case FighterState.Walk: return Walk(fr, 1);
            case FighterState.Crouch: return Crouch;
            case FighterState.Air: return Airborne(f);
            case FighterState.HitStun:
            {
                double k = Math.Min(1, f.Stun / 8.0);
                var b = Stand(0);
                return Pose.Lerp(b, b with { Chest = P(-8, -124), Head = P(-16, -138), FHand = P(10, -100), BHand = P(-22, -108) }, k);
            }
            case FighterState.BlockStun:
                return f._crouchBlock ? Crouch with { FHand = P(30, -106), BHand = P(26, -96), Chest = P(4, -90) }
                                      : Stand(0) with { FHand = P(24, -138), BHand = P(22, -122), Chest = P(0, -128), Head = P(2, -146) };
            case FighterState.Knockdown or FighterState.Ko:
                if (!f.Grounded) return Stand(0).Rotate(-Math.Min(1.4, fr * 0.07));
                return Lying;
            case FighterState.GetUp:
            {
                double k = fr / 22.0;
                return k < 0.5 ? Pose.Lerp(Lying, Crouch, k * 2) : Pose.Lerp(Crouch, Stand(0), (k - 0.5) * 2);
            }
            case FighterState.Win:
            {
                var b = Stand(0);
                return Pose.Lerp(b, b with { BHand = P(6, -200), FHand = P(28, -96), Head = P(8, -150) }, Math.Min(1, fr / 10.0));
            }
            case FighterState.Attack when f.Move is { } m:
            {
                var b = m.Stance switch { Stance.Crouching => Crouch, Stance.Air => Airborne(f), _ => Stand(0) };
                if (m.Look == Look.SpinKick && fr >= m.Startup && fr < m.Startup + m.Active) mirror = (fr - m.Startup) / 6 % 2 == 1;
                return Pose.Lerp(b, Struck(b, m, fr), Extension(m, fr));
            }
            default: return Stand(Math.Sin(clock * 5 + f.Side) * 2);
        }
    }

    /// <summary>Two-bone reach: where the joint goes for an end at <paramref name="end"/>, bending toward the preferred side.</summary>
    private static (Point Joint, Point End) Ik(Point root, Point end, double l1, double l2, bool bendDown)
    {
        double dx = end.X - root.X, dy = end.Y - root.Y, d = Math.Sqrt(dx * dx + dy * dy);
        double reach = l1 + l2 - 0.01;
        if (d > reach) { end = new Point(root.X + dx / d * reach, root.Y + dy / d * reach); d = reach; }
        d = Math.Max(d, 1);
        double a = (l1 * l1 - l2 * l2 + d * d) / (2 * d), h = Math.Sqrt(Math.Max(0, l1 * l1 - a * a));
        double ux = (end.X - root.X) / d, uy = (end.Y - root.Y) / d;
        var mid = new Point(root.X + ux * a, root.Y + uy * a);
        var j1 = new Point(mid.X - uy * h, mid.Y + ux * h);
        var j2 = new Point(mid.X + uy * h, mid.Y - ux * h);
        // Elbows bend down and back; knees bend forward.
        var joint = bendDown ? (j1.Y + -j1.X * 0.3 > j2.Y + -j2.X * 0.3 ? j1 : j2) : (j1.X > j2.X ? j1 : j2);
        return (joint, end);
    }

    private void DrawFighter(DrawingContext dc, Fighter f, double flash = 0)
    {
        var c = f.C;
        var pose = PoseOf(f, _clock, out bool mirror);
        double s = f.S;
        int face = mirror ? -f.Facing : f.Facing;
        Point W(Point p) => new(f.X + face * s * p.X, f.Y + s * p.Y);

        // The shadow on the floor.
        double lift = Math.Max(0, Fighter.Ground - f.Y);
        dc.DrawEllipse(Brush(Alpha(0xFF000000, 0.35 * Math.Max(0.3, 1 - lift / 250))), null, new Point(f.X, Fighter.Ground + 4), 46 * s * Math.Max(0.5, 1 - lift / 400), 9 * s);

        uint gi = c.Gi, skin = c.Skin, trim = c.Trim;
        if (flash > 0) { gi = Mix(gi, 0xFFFFFFFF, flash); skin = Mix(skin, 0xFFFFFFFF, flash); }
        uint ink = 0xFF0A0A12;

        void Limb(Point a, Point b, uint colour, double w)
        {
            dc.DrawLine(Pen(ink, (w + 4) * s), W(a), W(b));
            dc.DrawLine(Pen(colour, w * s), W(a), W(b));
        }

        void Arm(Point hand, bool front)
        {
            var (elbow, end) = Ik(pose.Chest, hand, 32, 32, bendDown: true);
            double k = front ? 1 : 0.72;
            Limb(pose.Chest, elbow, Shade(gi, k), 13);
            Limb(elbow, end, Shade(skin, k), 11);
            dc.DrawEllipse(Brush(Shade(trim, k)), Pen(ink, 2 * s), W(end), 7.5 * s, 7.5 * s);
        }

        void Leg(Point foot, bool front)
        {
            var (knee, end) = Ik(pose.Hip, foot, 41, 41, bendDown: false);
            double k = front ? 1 : 0.72;
            Limb(pose.Hip, knee, Shade(gi, k), 17);
            Limb(knee, end, Shade(gi, k * 0.92), 14);
            // The foot points the way the shin is going, turned forward.
            double fx = end.X - knee.X, fy = end.Y - knee.Y, fl = Math.Max(1, Math.Sqrt(fx * fx + fy * fy));
            var toe = new Point(end.X + 12 - fx / fl * 2, end.Y + fy / fl * 2);
            Limb(end, toe, Shade(skin, k * 0.9), 8);
        }

        Arm(pose.BHand, false);
        Leg(pose.BFoot, false);

        // The body: hips to shoulders, the belt round the waist.
        Limb(pose.Hip, pose.Chest, gi, 30);
        var waist = new Point(pose.Hip.X + (pose.Chest.X - pose.Hip.X) * 0.18, pose.Hip.Y + (pose.Chest.Y - pose.Hip.Y) * 0.18);
        // The open front of the jacket, in the trim colour, then the belt.
        dc.DrawLine(Pen(Alpha(trim, 0.85), 3 * s), W(new Point(pose.Chest.X + 6, pose.Chest.Y + 2)), W(new Point(waist.X + 6, waist.Y - 4)));
        dc.DrawLine(Pen(c.Belt, 8 * s), W(new Point(waist.X - 13, waist.Y)), W(new Point(waist.X + 13, waist.Y)));
        Leg(pose.FFoot, true);
        DrawHead(dc, c, pose, W, s, skin, face);
        Arm(pose.FHand, true);
    }

    private static void DrawHead(DrawingContext dc, Character c, Pose pose, Func<Point, Point> w, double s, uint skin, int face)
    {
        var head = w(pose.Head);
        var back = w(new Point(pose.Head.X - 8, pose.Head.Y - 2));
        uint ink = 0xFF0A0A12;
        // Neck.
        dc.DrawLine(Pen(ink, 12 * s), w(pose.Chest), head);
        dc.DrawLine(Pen(skin, 8 * s), w(pose.Chest), head);
        string style = c.Style;
        if (style.StartsWith("LONG", StringComparison.Ordinal))
        {
            // A long tail of hair down the back.
            dc.DrawLine(Pen(ink, 13 * s), back, w(new Point(pose.Head.X - 22, pose.Head.Y + 26)));
            dc.DrawLine(Pen(c.Hair, 9 * s), back, w(new Point(pose.Head.X - 22, pose.Head.Y + 26)));
        }
        dc.DrawEllipse(Brush(skin), Pen(ink, 2.5 * s), head, 14 * s, 15 * s);
        // Hair over the top and back.
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(w(new Point(pose.Head.X + 12, pose.Head.Y - 6)), true);
            ctx.ArcTo(w(new Point(pose.Head.X - 12, pose.Head.Y + 6)), new Size(14 * s, 15 * s), 0, false, face > 0 ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
            ctx.LineTo(w(new Point(pose.Head.X - 4, pose.Head.Y - 4)));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(Brush(c.Hair), null, g);
        if (style.StartsWith("QUICK", StringComparison.Ordinal))
            dc.DrawEllipse(Brush(c.Hair), Pen(ink, 2 * s), w(new Point(pose.Head.X - 4, pose.Head.Y - 18)), 6 * s, 6 * s);
        if (style.StartsWith("BALANCED", StringComparison.Ordinal))
        {
            // A headband, its ends flying behind.
            dc.DrawLine(Pen(c.Trim, 5 * s), w(new Point(pose.Head.X - 13, pose.Head.Y - 5)), w(new Point(pose.Head.X + 13, pose.Head.Y - 7)));
            dc.DrawLine(Pen(c.Trim, 4 * s), w(new Point(pose.Head.X - 13, pose.Head.Y - 5)), w(new Point(pose.Head.X - 30, pose.Head.Y + 2)));
            dc.DrawLine(Pen(c.Trim, 4 * s), w(new Point(pose.Head.X - 13, pose.Head.Y - 5)), w(new Point(pose.Head.X - 28, pose.Head.Y + 10)));
        }
        // An eye, facing forward.
        dc.DrawEllipse(Brush(ink), null, w(new Point(pose.Head.X + 7, pose.Head.Y - 1)), 2 * s, 2.4 * s);
    }

    private void DrawProjectile(DrawingContext dc, Projectile p)
    {
        bool big = p.Move.Cost > 0;
        double r = big ? 34 : 20, pulse = 1 + 0.12 * Math.Sin(p.Age * 0.7);
        uint col = p.Owner.C.Trim;
        int dir = Math.Sign(p.Vx);
        // A tail of fading copies behind it.
        for (int i = 5; i >= 1; i--)
            dc.DrawEllipse(Brush(Alpha(col, 0.12 * (6 - i))), null, new Point(p.X - dir * i * r * 0.5, p.Y), r * (1 - i * 0.1), r * (1 - i * 0.12));
        dc.DrawEllipse(Brush(Alpha(col, 0.45)), null, new Point(p.X, p.Y), r * 1.35 * pulse, r * 1.2 * pulse);
        dc.DrawEllipse(Brush(Mix(col, 0xFFFFFFFF, 0.5)), null, new Point(p.X, p.Y), r * pulse, r * 0.85 * pulse);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(p.X + dir * r * 0.2, p.Y), r * 0.45, r * 0.4);
    }

    private void DrawSpark(DrawingContext dc, Spark sp, int now)
    {
        double age = (now - sp.Born) / 18.0, r = (sp.Heavy ? 46 : 30) * (0.4 + age);
        uint col = sp.Blocked ? 0xFF8AD8FF : sp.Heavy ? 0xFFFFB030 : 0xFFFFF0A0;
        double a = 1 - age;
        for (int i = 0; i < 8; i++)
        {
            double ang = i * Math.PI / 4 + sp.Born, len = r * (i % 2 == 0 ? 1 : 0.6);
            dc.DrawLine(Pen(Alpha(col, a), sp.Heavy ? 5 : 3), new Point(sp.X + Math.Cos(ang) * r * 0.25, sp.Y + Math.Sin(ang) * r * 0.25),
                new Point(sp.X + Math.Cos(ang) * len, sp.Y + Math.Sin(ang) * len));
        }
        dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, a)), null, new Point(sp.X, sp.Y), r * 0.3, r * 0.3);
    }
}
