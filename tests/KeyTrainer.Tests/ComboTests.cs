using KeyTrainer.Core.Input;

namespace KeyTrainer.Tests;

public class ComboTests
{
    [Theory]
    [InlineData("Strg+Shift+T", "Strg+Shift+T")]
    [InlineData("shift + ctrl + t", "Strg+Shift+T")]
    [InlineData("Strg + Win + D", "Win+Strg+D")]
    [InlineData("Win+R", "Win+R")]
    [InlineData("win+shift+s", "Win+Shift+S")]
    [InlineData("Alt+Tab", "Alt+Tab")]
    [InlineData("Alt + Shift + +", "Alt+Shift+Plus")]
    [InlineData("Alt+Shift++", "Alt+Shift+Plus")]
    [InlineData("Win + Pfeil links", "Win+Pfeil links")]
    [InlineData("Win+Left", "Win+Pfeil links")]
    [InlineData("Strg+Backspace", "Strg+Rücktaste")]
    [InlineData("Shift+End", "Shift+Ende")]
    [InlineData("Strg+Shift+Esc", "Strg+Shift+Esc")]
    [InlineData("Strg+Shift+Enter", "Strg+Shift+Enter")]
    [InlineData("Shift+F10", "Shift+F10")]
    [InlineData("Alt+F4", "Alt+F4")]
    [InlineData("Strg+3", "Strg+3")]
    [InlineData("Win", "Win")]
    [InlineData("enter", "Enter")]
    [InlineData("Num +", "Num Plus")]
    [InlineData("Strg+Num Plus", "Strg+Num Plus")]
    public void Normalize_AcceptsAliasesAndAnyOrder(string input, string expected)
    {
        Assert.Equal(expected, Combo.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Strg+")]
    [InlineData("Strg+Foo")]
    [InlineData("Strg+A+B")]
    public void Parse_RejectsInvalid(string input)
    {
        Assert.Throws<FormatException>(() => Combo.Parse(input));
    }

    [Fact]
    public void KeyNames_CoversLettersDigitsFunctionKeys()
    {
        Assert.Equal("A", KeyNames.Name(0x41));
        Assert.Equal("7", KeyNames.Name(0x37));
        Assert.Equal("F10", KeyNames.Name(Vk.F1 + 9));
        Assert.Equal("Num 5", KeyNames.Name(Vk.NumPad0 + 5));
        Assert.Equal("Plus", KeyNames.Name(Vk.OemPlus));
    }
}

public class ComboNamesTests
{
    [Theory]
    [InlineData("Win+Strg+D", new[] { "Win", "Strg", "D" })]
    [InlineData("Strg+Num Plus", new[] { "Strg", "Num Plus" })]
    [InlineData("Win", new[] { "Win" })]
    public void Names_SplitsIntoKeyCaps(string combo, string[] expected)
    {
        Assert.Equal(expected, Combo.Parse(combo).Names());
    }
}
