using System.Windows;
using KeyTrainer.Core.Input;

namespace KeyTrainer;

/// <summary>One line in the debug list: either key caps ("Win" "+" "R") or typed text.</summary>
public sealed class TokenRow
{
    public TokenRow(InputToken token, DateTime time)
    {
        Time = time.ToString("HH:mm:ss");
        if (token.Kind == TokenKind.Text)
        {
            Text = token.Value;
            Parts = [];
        }
        else
        {
            Text = "";
            var parts = new List<KeyPart>();
            foreach (var name in Combo.Parse(token.Value).Names())
            {
                if (parts.Count > 0) parts.Add(new KeyPart("+", true));
                parts.Add(new KeyPart(name, false));
            }
            Parts = parts;
        }
    }

    public string Time { get; }

    public string Text { get; }

    public IReadOnlyList<KeyPart> Parts { get; }

    public Visibility TextVisibility => Parts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ComboVisibility => Parts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
}

public sealed record KeyPart(string Label, bool IsSeparator);
