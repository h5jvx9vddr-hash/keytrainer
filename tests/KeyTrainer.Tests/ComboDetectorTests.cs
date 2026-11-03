using KeyTrainer.Core.Input;

namespace KeyTrainer.Tests;

public class ComboDetectorTests
{
    readonly ComboDetector _detector = new();
    readonly List<string> _tokens = [];

    public ComboDetectorTests()
    {
        _detector.TokenDetected += t => _tokens.Add(t.ToString());
    }

    void Down(int vk, string? text = null) => _detector.Process(new KeyEvent(vk, true, text));

    void Up(int vk) => _detector.Process(new KeyEvent(vk, false));

    void Tap(int vk, string? text = null)
    {
        Down(vk, text);
        Up(vk);
    }

    void Type(string text)
    {
        foreach (var c in text)
        {
            int vk = char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : Vk.OemPeriod;
            if (char.IsUpper(c))
            {
                Down(Vk.LShift);
                Tap(vk, c.ToString());
                Up(Vk.LShift);
            }
            else
            {
                Tap(vk, c.ToString());
            }
        }
    }

    [Fact]
    public void WinR()
    {
        Down(Vk.LWin);
        Tap('R', "r");
        Up(Vk.LWin);
        Assert.Equal(["Win+R"], _tokens);
    }

    [Fact]
    public void CtrlShiftT()
    {
        Down(Vk.LControl);
        Down(Vk.LShift);
        Tap('T', "\x14");
        Up(Vk.LShift);
        Up(Vk.LControl);
        Assert.Equal(["Strg+Shift+T"], _tokens);
    }

    [Fact]
    public void AltTab_HeldAltWithTwoTabs_EmitsTwice()
    {
        Down(Vk.LMenu);
        Tap(Vk.Tab, "\t");
        Tap(Vk.Tab, "\t");
        Up(Vk.LMenu);
        Assert.Equal(["Alt+Tab", "Alt+Tab"], _tokens);
    }

    [Fact]
    public void WinShiftS()
    {
        Down(Vk.LWin);
        Down(Vk.LShift);
        Tap('S', "S");
        Up(Vk.LShift);
        Up(Vk.LWin);
        Assert.Equal(["Win+Shift+S"], _tokens);
    }

    [Fact]
    public void GenericModifierCodes_AreMapped()
    {
        Down(Vk.Control);
        Tap('L', "\x0C");
        Up(Vk.Control);
        Assert.Equal(["Strg+L"], _tokens);
    }

    [Fact]
    public void WinAlone_EmitsWin()
    {
        Tap(Vk.LWin);
        Assert.Equal(["Win"], _tokens);
    }

    [Fact]
    public void TypedText_ThenEnter()
    {
        Down(Vk.LWin);
        Tap('R', "r");
        Up(Vk.LWin);
        Type("services.msc");
        Tap(Vk.Return, "\r");
        Assert.Equal(["Win+R", "text:services.msc", "Enter"], _tokens);
    }

    [Fact]
    public void ShiftedLetters_AreText()
    {
        Type("Dienste");
        _detector.Flush();
        Assert.Equal(["text:Dienste"], _tokens);
    }

    [Fact]
    public void SpecialKeys_AreNotText()
    {
        Tap(Vk.Escape, "\x1B");
        Tap(Vk.Tab, "\t");
        Tap(Vk.F1 + 1);
        Tap(Vk.Left);
        Assert.Equal(["Esc", "Tab", "F2", "Pfeil links"], _tokens);
    }

    [Fact]
    public void Backspace_EditsPendingText()
    {
        Type("rechnx");
        Tap(Vk.Back, "\b");
        Type("er");
        Tap(Vk.Return, "\r");
        Assert.Equal(["text:rechner", "Enter"], _tokens);
    }

    [Fact]
    public void Backspace_WithoutText_IsKey()
    {
        Tap(Vk.Back, "\b");
        Assert.Equal(["Rücktaste"], _tokens);
    }

    [Fact]
    public void CtrlBackspace_IsCombo_AndFlushesText()
    {
        Type("abc");
        Down(Vk.LControl);
        Tap(Vk.Back, "\x7F");
        Up(Vk.LControl);
        Assert.Equal(["text:abc", "Strg+Rücktaste"], _tokens);
    }

    [Fact]
    public void Space_AloneIsKey_InsideTextIsText()
    {
        Tap(Vk.Space, " ");
        Type("a");
        Tap(Vk.Space, " ");
        Type("b");
        _detector.Flush();
        Assert.Equal(["Leertaste", "text:a b"], _tokens);
    }

    [Fact]
    public void AltGr_ProducesText_NotCtrlAltCombo()
    {
        // AltGr arrives as a fake LCtrl followed by RAlt.
        Down(Vk.LControl);
        Down(Vk.RMenu);
        Tap('Q', "@");
        Up(Vk.RMenu);
        Up(Vk.LControl);
        _detector.Flush();
        Assert.Equal(["text:@"], _tokens);
    }

    [Fact]
    public void LeftAlt_FormsAltCombos()
    {
        Down(Vk.LMenu);
        Tap(Vk.F1 + 3);
        Up(Vk.LMenu);
        Assert.Equal(["Alt+F4"], _tokens);
    }

    [Fact]
    public void ShiftWithNavigationKey_IsCombo()
    {
        Down(Vk.LShift);
        Tap(Vk.End);
        Up(Vk.LShift);
        Assert.Equal(["Shift+Ende"], _tokens);
    }

    [Fact]
    public void AutoRepeat_OfCombo_EmitsOnce_ButRepeatsText()
    {
        Down(Vk.LWin);
        Down(Vk.Left);
        Down(Vk.Left);
        Up(Vk.Left);
        Up(Vk.LWin);
        Down('A', "a");
        Down('A', "a");
        Up('A');
        _detector.Flush();
        Assert.Equal(["Win+Pfeil links", "text:aa"], _tokens);
    }

    [Fact]
    public void Reset_ClearsStuckModifiers()
    {
        Down(Vk.LWin);
        Down('L', "l");
        _detector.Reset();
        Tap('X', "x");
        _detector.Flush();
        Assert.Equal(["Win+L", "text:x"], _tokens);
    }

    [Fact]
    public void AltShiftPlus_MatchesNormalizedForm()
    {
        Down(Vk.LMenu);
        Down(Vk.LShift);
        Tap(Vk.OemPlus, "*");
        Up(Vk.LShift);
        Up(Vk.LMenu);
        Assert.Equal([Combo.Normalize("Alt + Shift + +")], _tokens);
    }
}
