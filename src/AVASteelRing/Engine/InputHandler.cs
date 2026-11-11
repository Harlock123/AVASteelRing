using Avalonia.Input;

namespace AVASteelRing.Engine;

/// <summary>
/// Keyboard and two gamepads boiled down to each player's controls: the stick (up is +1, so up means up) and the four
/// buttons. In a one-player game both sets of keys and both pads work for player 1.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad1 = new(0), _pad2 = new(1);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad1.Update();
        _pad2.Update();
    }

    /// <summary>W A S D to move; U and I punch (light, heavy), J and K kick (light, heavy).</summary>
    public Controls KeysOne => Make(Any(Key.A), Any(Key.D), Any(Key.W), Any(Key.S),
        Any(Key.U), Any(Key.I), Any(Key.J), Any(Key.K));

    /// <summary>The arrows to move; NumPad 4 and 5 punch, 1 and 2 kick (or Insert, Home, Delete, End).</summary>
    public Controls KeysTwo => Make(Any(Key.Left), Any(Key.Right), Any(Key.Up), Any(Key.Down),
        Any(Key.NumPad4, Key.Insert), Any(Key.NumPad5, Key.Home), Any(Key.NumPad1, Key.Delete), Any(Key.NumPad2, Key.End));

    /// <summary>A gamepad: the stick or D-pad to move, X and Y punch, A and B kick.</summary>
    public static Controls Pad(ControllerManager p) => Make(
        p.DPadLeft || p.LeftStickX < -0.45f, p.DPadRight || p.LeftStickX > 0.45f,
        p.DPadUp || p.LeftStickY < -0.5f, p.DPadDown || p.LeftStickY > 0.5f,
        p.ButtonX, p.ButtonY, p.ButtonA, p.ButtonB);

    private static Controls Make(bool left, bool right, bool up, bool down, bool lp, bool hp, bool lk, bool hk) =>
        new((right ? 1 : 0) - (left ? 1 : 0), (up ? 1 : 0) - (down ? 1 : 0),
            (lp ? Buttons.LightPunch : 0) | (hp ? Buttons.HeavyPunch : 0) | (lk ? Buttons.LightKick : 0) | (hk ? Buttons.HeavyKick : 0));

    /// <summary>Two sets of controls together: the stick of whichever is pushed, all the buttons held.</summary>
    public static Controls Merge(params Controls[] all) => new(
        Math.Clamp(all.Sum(c => c.X), -1, 1), Math.Clamp(all.Sum(c => c.Y), -1, 1),
        all.Aggregate(Buttons.None, (b, c) => b | c.Held));

    public Controls One(bool alone) => alone ? Merge(KeysOne, KeysTwo, Pad(_pad1), Pad(_pad2)) : Merge(KeysOne, Pad(_pad1));
    public Controls Two => Merge(KeysTwo, Pad(_pad2));

    /// <summary>Enter, Space or 1.</summary>
    public bool Start => Any(Key.Enter, Key.Space, Key.D1) || _pad1.ButtonStart || _pad2.ButtonStart;

    public bool PadUp => _pad1.DPadUp || _pad1.LeftStickY < -0.5f;
    public bool PadDown => _pad1.DPadDown || _pad1.LeftStickY > 0.5f;
    public bool PadLeft => _pad1.DPadLeft || _pad1.LeftStickX < -0.5f;
    public bool PadRight => _pad1.DPadRight || _pad1.LeftStickX > 0.5f;
    public bool PadAccept => _pad1.ButtonA || _pad1.ButtonB;
    public bool PadDone => _pad1.ButtonStart;
    public bool PadPause => _pad1.ButtonStart || _pad2.ButtonStart;
    public bool PadBack => _pad1.ButtonBack || _pad2.ButtonBack;

    public void Dispose()
    {
        _pad1.Dispose();
        _pad2.Dispose();
    }
}
