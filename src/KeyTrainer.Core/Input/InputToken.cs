namespace KeyTrainer.Core.Input;

/// <summary>A single key event as delivered by the platform layer.</summary>
/// <param name="Vk">Virtual-key code; left/right modifiers should use the specific codes (LShift, RMenu, ...).</param>
/// <param name="IsDown">True for key down (including auto-repeat), false for key up.</param>
/// <param name="Text">Characters the key produces with the current layout and modifiers, if any.</param>
public readonly record struct KeyEvent(int Vk, bool IsDown, string? Text = null);

public enum TokenKind
{
    Combo,
    Text,
}

/// <summary>What the detector emits: a key combination ("Win+R") or a typed string ("text:services.msc").</summary>
public readonly record struct InputToken(TokenKind Kind, string Value)
{
    public const string TextPrefix = "text:";

    public static InputToken FromCombo(Combo combo) => new(TokenKind.Combo, combo.ToString());

    public static InputToken FromText(string text) => new(TokenKind.Text, text);

    public override string ToString() => Kind == TokenKind.Text ? TextPrefix + Value : Value;
}
