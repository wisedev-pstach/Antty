namespace Antty.UI;

public enum MenuChoice
{
    TalkToAssistant,
    SearchDocuments,
    SwitchProvider,
    ReloadDocuments,
    Settings,
    Exit
}

public static class MenuChoiceExtensions
{
    private static readonly Dictionary<string, MenuChoice> _choiceMap = new()
    {
        ["💬 Talk to Assistant"] = MenuChoice.TalkToAssistant,
        ["🔍 Search Documents"] = MenuChoice.SearchDocuments,
        ["🔧 Switch Local/Cloud"] = MenuChoice.SwitchProvider,
        ["📚 Reload/Change Documents"] = MenuChoice.ReloadDocuments,
        ["⚙️  Settings"] = MenuChoice.Settings,
        ["❌ Exit"] = MenuChoice.Exit
    };

    public static MenuChoice Parse(string displayText) =>
        _choiceMap.TryGetValue(displayText, out var choice)
            ? choice
            : throw new ArgumentException($"Unknown menu choice: {displayText}");

    public static string[] GetDisplayChoices() => _choiceMap.Keys.ToArray();
}
