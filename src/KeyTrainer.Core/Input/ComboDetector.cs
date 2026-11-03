using System.Text;

namespace KeyTrainer.Core.Input;

/// <summary>
/// Turns a raw key event stream into tokens: key combinations in normal form ("Win+R")
/// and typed strings ("text:services.msc"). Holds input only in memory.
/// </summary>
public sealed class ComboDetector
{
    readonly HashSet<int> _down = [];
    readonly StringBuilder _text = new();
    bool _winAlone;

    /// <summary>
    /// German and most European layouts send AltGr as LCtrl + RAlt. When true, the right Alt key
    /// counts as AltGr: it neither forms "Alt+..." combos nor lets its fake Ctrl through.
    /// </summary>
    public bool RightAltIsAltGr { get; init; } = true;

    public event Action<InputToken>? TokenDetected;

    public bool HasPendingText => _text.Length > 0;

    public bool IsDown(int vk) => _down.Contains(MapGeneric(vk));

    public void Process(KeyEvent e)
    {
        int vk = MapGeneric(e.Vk);
        if (!e.IsDown)
        {
            OnKeyUp(vk);
            return;
        }

        bool repeat = !_down.Add(vk);

        if (IsModifier(vk))
        {
            if (!repeat && IsWin(vk)) _winAlone = true;
            return;
        }

        _winAlone = false;
        var mods = CurrentMods();

        if (IsTextInput(vk, e.Text, mods))
        {
            _text.Append(e.Text);
            return;
        }

        // Plain backspace while typing edits the pending text instead of being a combo.
        if (vk == Vk.Back && mods == Mods.None && _text.Length > 0)
        {
            _text.Length--;
            return;
        }

        if (repeat) return;

        Flush();
        Emit(InputToken.FromCombo(new Combo(mods, KeyNames.Name(vk))));
    }

    /// <summary>Emits pending typed text as a token (call on idle timeout or end of exercise).</summary>
    public void Flush()
    {
        if (_text.Length == 0) return;
        var text = _text.ToString();
        _text.Clear();
        Emit(InputToken.FromText(text));
    }

    /// <summary>Forgets all state, e.g. after the session was locked and key-ups were lost.</summary>
    public void Reset()
    {
        _down.Clear();
        _text.Clear();
        _winAlone = false;
    }

    void OnKeyUp(int vk)
    {
        _down.Remove(vk);
        if (IsWin(vk) && _winAlone)
        {
            _winAlone = false;
            Flush();
            Emit(InputToken.FromCombo(new Combo(Mods.Win, "")));
        }
    }

    bool IsTextInput(int vk, string? text, Mods mods)
    {
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0])) return false;
        if ((mods & (Mods.Win | Mods.Ctrl | Mods.Alt)) != 0) return false;
        // A space on its own is the "Leertaste" key (e.g. toggling a checkbox); inside text it is text.
        if (vk == Vk.Space && _text.Length == 0) return false;
        return true;
    }

    Mods CurrentMods()
    {
        bool altGr = RightAltIsAltGr && _down.Contains(Vk.RMenu);
        var mods = Mods.None;
        if (_down.Contains(Vk.LWin) || _down.Contains(Vk.RWin)) mods |= Mods.Win;
        if (!altGr && (_down.Contains(Vk.LControl) || _down.Contains(Vk.RControl))) mods |= Mods.Ctrl;
        if (_down.Contains(Vk.LMenu) || (!RightAltIsAltGr && _down.Contains(Vk.RMenu))) mods |= Mods.Alt;
        if (_down.Contains(Vk.LShift) || _down.Contains(Vk.RShift)) mods |= Mods.Shift;
        return mods;
    }

    void Emit(InputToken token) => TokenDetected?.Invoke(token);

    static int MapGeneric(int vk) => vk switch
    {
        Vk.Shift => Vk.LShift,
        Vk.Control => Vk.LControl,
        Vk.Menu => Vk.LMenu,
        _ => vk,
    };

    static bool IsWin(int vk) => vk is Vk.LWin or Vk.RWin;

    public static bool IsModifier(int vk) =>
        vk is Vk.LShift or Vk.RShift or Vk.LControl or Vk.RControl or Vk.LMenu or Vk.RMenu or Vk.LWin or Vk.RWin;
}
