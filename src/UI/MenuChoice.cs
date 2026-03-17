namespace Antty.UI;

public enum MenuChoice
{
    TalkToAssistant,
    SearchDocuments,
    SwitchProvider,
    ReloadDocuments,
    Settings,
    Update,
    Exit
}

public static class MenuChoiceExtensions
{
    private static readonly Dictionary<string, MenuChoice> _baseChoiceMap = new()
    {
        ["💬 Talk to Assistant"] = MenuChoice.TalkToAssistant,
        ["🔍 Search Documents"] = MenuChoice.SearchDocuments,
        ["🔧 Switch Local/Cloud"] = MenuChoice.SwitchProvider,
        ["📚 Reload/Change Documents"] = MenuChoice.ReloadDocuments,
        ["⚙️  Settings"] = MenuChoice.Settings,
        ["❌ Exit"] = MenuChoice.Exit
    };

    public static MenuChoice Parse(string displayText)
    {
        if (_baseChoiceMap.TryGetValue(displayText, out var choice))
            return choice;

        // Dynamic update entry — any text starting with this prefix maps to Update
        if (displayText.StartsWith("🔄 Update Available"))
            return MenuChoice.Update;

        throw new ArgumentException($"Unknown menu choice: {displayText}");
    }

    public static string[] GetDisplayChoices(string? updateVersion = null)
    {
        var choices = new List<string>(_baseChoiceMap.Keys);

        if (updateVersion is not null)
        {
            // Insert update option before Exit
            var exitIndex = choices.IndexOf("❌ Exit");
            choices.Insert(exitIndex, $"🔄 Update Available ({updateVersion})");
        }

        return choices.ToArray();
    }
}
