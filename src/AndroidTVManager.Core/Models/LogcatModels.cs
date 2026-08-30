namespace AndroidTVManager.Core.Models;

public sealed record LogcatOptions(
    string? PackageFilter = null,
    string? TagFilter = null,
    string? SeverityFilter = null,
    int MaxLines = 25000);
