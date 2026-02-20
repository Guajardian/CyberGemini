using System.Collections.Generic;

namespace CyberGemini.Models;

public sealed class ScanResult
{
    public IReadOnlyList<DuplicateFileItem> DuplicateFiles { get; init; } = new List<DuplicateFileItem>();
    public IReadOnlyList<NameDuplicateItem> NameDuplicates { get; init; } = new List<NameDuplicateItem>();
}