namespace CyberGemini.Models;

public sealed class AppSettings
{
    public int MaxDegreeOfParallelism { get; set; } = 4;
    public string HashAlgorithm { get; set; } = "SHA256";
    public bool SkipHiddenFiles { get; set; }
    public bool SkipSystemFiles { get; set; }
    public string ExcludedExtensions { get; set; } = string.Empty;
    public double MinFileSizeMb { get; set; }
    public double MaxFileSizeMb { get; set; }
    public bool FolderSafetyEnabled { get; set; } = true;
}