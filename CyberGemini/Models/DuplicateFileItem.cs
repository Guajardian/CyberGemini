using CyberGemini.Utilities;

namespace CyberGemini.Models;

public sealed class DuplicateFileItem : ObservableObject
{
    private bool _isSelected;
    private bool _isFolderEssential;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// True when this file is the only copy of its content (hash) within its parent folder.
    /// Deleting it would leave that folder without this file.
    /// </summary>
    public bool IsFolderEssential
    {
        get => _isFolderEssential;
        set => SetProperty(ref _isFolderEssential, value);
    }

    public int GroupId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Hash { get; init; } = string.Empty;
}