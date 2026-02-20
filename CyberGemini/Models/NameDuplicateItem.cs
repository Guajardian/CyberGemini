using CyberGemini.Utilities;

namespace CyberGemini.Models;

public sealed class NameDuplicateItem : ObservableObject
{
    private string _fileName = string.Empty;
    private string _fullPath = string.Empty;
    private string _newFileName = string.Empty;

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public string FullPath
    {
        get => _fullPath;
        set => SetProperty(ref _fullPath, value);
    }

    public string NewFileName
    {
        get => _newFileName;
        set => SetProperty(ref _newFileName, value);
    }
}