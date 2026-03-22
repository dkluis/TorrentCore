namespace TorrentCore.Avalonia.Models;

public sealed class NavigationSection(string key, string title, string shortDescription, string description)
{
    public string Key              { get; } = key;
    public string Title            { get; } = title;
    public string ShortDescription { get; } = shortDescription;
    public string Description      { get; } = description;
}
