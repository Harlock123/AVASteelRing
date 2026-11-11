namespace AVASteelRing.Engine;

/// <summary>The four buttons: light and heavy punch, light and heavy kick.</summary>
[Flags]
public enum Buttons { None = 0, LightPunch = 1, HeavyPunch = 2, LightKick = 4, HeavyKick = 8, AnyPunch = LightPunch | HeavyPunch, AnyKick = LightKick | HeavyKick }

/// <summary>A special move's stick motion, read off the last half-second of input, relative to the way you face.</summary>
public enum Motion { None, QuarterForward, QuarterBack, DragonPunch, DoubleQuarterForward }

public enum Stance { Standing, Crouching, Air }

/// <summary>Where a move must be blocked: mid is blocked standing or crouching, low only crouching, high (an overhead) only standing.</summary>
public enum Height { Mid, Low, Overhead }

/// <summary>How a move looks: the drawing poses the body from this and how far through the move it is.</summary>
public enum Look { Jab, Straight, Uppercut, LowKick, HighKick, Sweep, LowPunch, AirPunch, AirKick, Fireball, RisingFist, SpinKick, Charge, Super }

/// <summary>
/// One attack: what starts it, its timing in frames (startup, active, recovery), where it hits (a box relative to the
/// fighter's feet, facing right), what it does to someone it hits or who blocks it, and how it looks.
/// </summary>
public sealed record Move(
    string Name,
    Stance Stance,
    Buttons Button,
    Motion Motion,
    int Startup,
    int Active,
    int Recovery,
    int Damage,
    int HitStun,
    int BlockStun,
    double BoxX,
    double BoxY,
    double BoxW,
    double BoxH,
    Look Look,
    Height Height = Height.Mid,
    bool Knockdown = false,
    double Push = 6,
    bool Projectile = false,
    int Cost = 0,
    int Hits = 1,
    double Lunge = 0,
    double Rise = 0)
{
    public int Total => Startup + Active + Recovery;
    public bool Special => Motion != Motion.None;
}

/// <summary>A fighter's build and colours, and the moves they have.</summary>
public sealed record Character(
    string Name,
    string From,
    string Style,
    double Walk,
    double Jump,
    double Scale,
    double Toughness,
    uint Skin,
    uint Hair,
    uint Gi,
    uint Belt,
    uint Trim,
    IReadOnlyList<Move> Moves);

/// <summary>The roster. The normal moves are shared, with each fighter's build changing their reach; the specials are their own.</summary>
public static class Roster
{
    private static List<Move> Normals(double reach, int power) =>
    [
        new("JAB", Stance.Standing, Buttons.LightPunch, Motion.None, 4, 3, 7, 30 * power / 10, 14, 10, 30, -112, 48 * reach, 22, Look.Jab, Push: 5),
        new("STRAIGHT", Stance.Standing, Buttons.HeavyPunch, Motion.None, 8, 4, 16, 80 * power / 10, 20, 15, 28, -116, 62 * reach, 26, Look.Straight, Push: 9),
        new("FRONT KICK", Stance.Standing, Buttons.LightKick, Motion.None, 6, 4, 10, 40 * power / 10, 16, 11, 30, -78, 56 * reach, 26, Look.LowKick, Push: 7),
        new("HIGH KICK", Stance.Standing, Buttons.HeavyKick, Motion.None, 10, 5, 18, 95 * power / 10, 22, 16, 30, -140, 68 * reach, 34, Look.HighKick, Push: 11),
        new("LOW JAB", Stance.Crouching, Buttons.LightPunch, Motion.None, 4, 3, 7, 25 * power / 10, 13, 9, 26, -72, 46 * reach, 20, Look.LowPunch, Push: 5),
        new("UPPERCUT", Stance.Crouching, Buttons.HeavyPunch, Motion.None, 7, 5, 18, 85 * power / 10, 22, 14, 18, -150, 40 * reach, 70, Look.Uppercut, Push: 8),
        new("SHIN KICK", Stance.Crouching, Buttons.LightKick, Motion.None, 5, 3, 9, 30 * power / 10, 13, 9, 28, -22, 56 * reach, 20, Look.LowKick, Height.Low, Push: 5),
        new("SWEEP", Stance.Crouching, Buttons.HeavyKick, Motion.None, 9, 5, 22, 85 * power / 10, 30, 14, 24, -24, 74 * reach, 22, Look.Sweep, Height.Low, Knockdown: true, Push: 8),
        new("AIR PUNCH", Stance.Air, Buttons.AnyPunch, Motion.None, 5, 8, 6, 60 * power / 10, 18, 12, 24, -96, 48 * reach, 40, Look.AirPunch, Height.Overhead, Push: 6),
        new("FLYING KICK", Stance.Air, Buttons.AnyKick, Motion.None, 6, 10, 6, 80 * power / 10, 20, 14, 22, -60, 62 * reach, 40, Look.AirKick, Height.Overhead, Push: 8),
    ];

    public static readonly Character Kai = new("KAI", "OSAKA", "BALANCED: A FIREBALL AND A RISING FIST", 3.4, 15.5, 1.0, 1.0,
        0xFFE8B890, 0xFF1A1410, 0xFFF0F0F4, 0xFF181818, 0xFFD03030,
        [
            .. Normals(1, 10),
            new("TIGER FLAME", Stance.Standing, Buttons.AnyPunch, Motion.QuarterForward, 12, 2, 26, 90, 22, 16, 40, -112, 30, 30, Look.Fireball, Projectile: true, Push: 10),
            new("RISING FIST", Stance.Standing, Buttons.AnyPunch, Motion.DragonPunch, 3, 12, 30, 130, 30, 18, 10, -190, 44, 140, Look.RisingFist, Knockdown: true, Rise: 14, Lunge: 2),
            new("WHIRL KICK", Stance.Standing, Buttons.AnyKick, Motion.QuarterBack, 8, 18, 18, 110, 22, 16, 20, -110, 70, 30, Look.SpinKick, Knockdown: true, Hits: 3, Lunge: 4.5),
            new("BLAZING TIGER", Stance.Standing, Buttons.AnyPunch, Motion.DoubleQuarterForward, 6, 2, 30, 260, 34, 22, 40, -120, 50, 50, Look.Super, Projectile: true, Cost: 100, Hits: 5, Knockdown: true),
        ]);

    public static readonly Character Bruna = new("BRUNA", "SAO PAULO", "HEAVY: SHORT REACH, HARD HITS, A CHARGE", 2.6, 13.5, 1.14, 0.8,
        0xFF9A6A48, 0xFF3A2010, 0xFF2A3A6A, 0xFFE8C020, 0xFFE8C020,
        [
            .. Normals(1.05, 13),
            new("BULL RUSH", Stance.Standing, Buttons.AnyPunch, Motion.QuarterForward, 10, 20, 22, 120, 24, 18, 20, -120, 50, 90, Look.Charge, Knockdown: true, Lunge: 8, Push: 14),
            new("HAMMER", Stance.Standing, Buttons.AnyPunch, Motion.DragonPunch, 14, 6, 24, 140, 26, 20, 30, -170, 60, 60, Look.Uppercut, Height.Overhead, Knockdown: true, Push: 12),
            new("STOMP", Stance.Standing, Buttons.AnyKick, Motion.QuarterBack, 12, 6, 22, 100, 30, 18, 20, -30, 90, 30, Look.Sweep, Height.Low, Knockdown: true),
            new("AVALANCHE", Stance.Standing, Buttons.AnyPunch, Motion.DoubleQuarterForward, 8, 30, 26, 280, 34, 22, 20, -130, 60, 110, Look.Super, Cost: 100, Hits: 4, Knockdown: true, Lunge: 9),
        ]);

    public static readonly Character Zen = new("ZEN", "TAIPEI", "QUICK: THE LIGHTEST AND THE FASTEST, KICKS", 4.3, 17, 0.94, 1.15,
        0xFFF0C8A0, 0xFF101010, 0xFF2A8A4A, 0xFF101010, 0xFFF0E8D0,
        [
            .. Normals(0.95, 9),
            new("CRANE KICK", Stance.Standing, Buttons.AnyKick, Motion.QuarterForward, 6, 10, 18, 90, 22, 15, 30, -120, 70, 50, Look.HighKick, Lunge: 6, Push: 10),
            new("SKY KICK", Stance.Standing, Buttons.AnyKick, Motion.DragonPunch, 3, 14, 26, 115, 28, 16, 10, -200, 50, 160, Look.RisingFist, Knockdown: true, Rise: 16, Lunge: 2.5),
            new("SHADOW STEP", Stance.Standing, Buttons.AnyPunch, Motion.QuarterBack, 4, 12, 10, 70, 18, 12, 24, -110, 50, 40, Look.Straight, Lunge: 10),
            new("THOUSAND LEAVES", Stance.Standing, Buttons.AnyKick, Motion.DoubleQuarterForward, 5, 34, 22, 250, 30, 20, 30, -140, 80, 110, Look.SpinKick, Cost: 100, Hits: 6, Knockdown: true, Lunge: 5),
        ]);

    public static readonly Character Orla = new("ORLA", "GALWAY", "LONG REACH: KEEPS YOU OUT WITH SPARKS", 3.1, 15, 1.04, 0.95,
        0xFFF4D8C8, 0xFFB84A20, 0xFF6A2A7A, 0xFFE8E8F0, 0xFF40C8E8,
        [
            .. Normals(1.15, 9),
            new("SPARK", Stance.Standing, Buttons.AnyPunch, Motion.QuarterForward, 10, 2, 22, 70, 20, 14, 40, -112, 26, 26, Look.Fireball, Projectile: true, Push: 9),
            new("LOW SPARK", Stance.Standing, Buttons.AnyKick, Motion.QuarterForward, 12, 2, 24, 60, 20, 14, 40, -30, 26, 22, Look.Fireball, Height.Low, Projectile: true, Push: 8),
            new("ARC FLASH", Stance.Standing, Buttons.AnyPunch, Motion.DragonPunch, 4, 10, 30, 120, 28, 18, 20, -180, 60, 150, Look.RisingFist, Knockdown: true, Rise: 12),
            new("STORM WALL", Stance.Standing, Buttons.AnyPunch, Motion.DoubleQuarterForward, 6, 2, 30, 240, 30, 22, 40, -150, 60, 140, Look.Super, Projectile: true, Cost: 100, Hits: 5, Knockdown: true),
        ]);

    public static readonly IReadOnlyList<Character> All = [Kai, Bruna, Zen, Orla];
}
