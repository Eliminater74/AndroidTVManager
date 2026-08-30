namespace AndroidTVManager.Core.Models;

public enum DisplayCaptureLabel
{
    Unlabeled,
    Good,
    Bad
}

public sealed record DisplayDiagnosticSnapshot(
    string Serial,
    string? FriendlyDeviceName,
    DateTimeOffset CapturedUtc,
    DisplayCaptureLabel Label,
    DisplayInfo Display,
    HdmiInfo Hdmi,
    string? HdcpState,
    string? CecPhysicalAddress,
    string? CecLogicalAddress,
    IReadOnlyList<string> SurfaceFlingerModes,
    IReadOnlyList<string> VendorProperties,
    IReadOnlyList<InspectionCommandEvidence> Evidence);

public sealed record DisplayDiagnosticChange(
    string Name,
    string? PreviousValue,
    string? CurrentValue);

public sealed record DisplayDiagnosticComparison(
    DateTimeOffset PreviousCapturedUtc,
    DateTimeOffset CurrentCapturedUtc,
    IReadOnlyList<DisplayDiagnosticChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}
