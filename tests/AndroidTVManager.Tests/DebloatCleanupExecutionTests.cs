using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Core.Scripts;
using AndroidTVManager.Infrastructure.Packages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DebloatCleanupExecutionTests
{
    [Fact]
    public async Task Fingerprint_mismatch_aborts_before_mutation()
    {
        var scripts = new RecordingScripts();
        var execution = CreateExecution(
            [Package("com.example.optional")],
            scripts,
            liveFingerprint: "new-fp");

        await FluentActions.Awaiting(() => execution.ExecuteAsync(Plan("old-fp", enabled: true)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*device state changed since this cleanup was prepared*");
        scripts.Executed.Should().BeFalse();
    }

    [Fact]
    public async Task Package_state_change_aborts_and_does_not_mutate()
    {
        var scripts = new RecordingScripts();
        var execution = CreateExecution(
            [Package("com.example.optional", isEnabled: false)],
            scripts,
            liveFingerprint: "fp");

        await FluentActions.Awaiting(() => execution.ExecuteAsync(Plan("fp", enabled: true)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*device state changed since this cleanup was prepared*");
        scripts.Executed.Should().BeFalse();
    }

    [Fact]
    public async Task Successful_cleanup_is_locked_to_the_plan_serial_and_can_restore()
    {
        var scripts = new RecordingScripts();
        var execution = CreateExecution(
            [Package("com.example.optional")],
            scripts,
            liveFingerprint: "fp");

        var result = await execution.ExecuteAsync(Plan("fp", enabled: true));
        result.SuccessfulActions.Should().Be(1);
        result.FailedActions.Should().Be(0);
        result.CanUndo.Should().BeTrue();
        scripts.LastTargetSerial.Should().Be("tv-1");
        scripts.LastScriptName.Should().Be("Recommended Cleanup");

        var restore = await execution.RestoreAsync(result.ExecutionId, "tv-1");
        restore.RestoredActions.Should().Be(1);
        restore.FailedActions.Should().Be(0);
        scripts.LastUndoSerial.Should().Be("tv-1");
    }

    [Fact]
    public async Task Restore_targets_the_original_serial()
    {
        var scripts = new RecordingScripts();
        var execution = CreateExecution([Package("com.example.optional")], scripts, "fp");
        var result = await execution.ExecuteAsync(Plan("fp", enabled: true));

        await execution.RestoreAsync(result.ExecutionId, "other-device");
        scripts.LastUndoSerial.Should().Be("other-device");
    }

    private static DebloatExecutionService CreateExecution(
        IReadOnlyList<PackageInventoryEntry> packages,
        RecordingScripts scripts,
        string liveFingerprint)
        => new(
            new FixedInventory(packages),
            new PackageClassifier(),
            new PackageReferenceCatalog(),
            scripts,
            new FixedInspection(liveFingerprint));

    private static DebloatPlan Plan(string fingerprint, bool enabled)
        => new(
            "tv-1",
            fingerprint,
            DateTimeOffset.UtcNow,
            DebloatPreset.Medium,
            null,
            [
                new DebloatPlanItem(
                    Package("com.example.optional", isEnabled: enabled),
                    new PackageAssessment(
                        "com.example.optional",
                        PackageRiskLevel.Caution,
                        PackageConfidence.High,
                        "Optional",
                        "Optional test package.",
                        "Disable",
                        [],
                        [],
                        false,
                        "test"),
                    DebloatAction.Disable,
                    true,
                    null)
            ],
            []);

    private static PackageInventoryEntry Package(string name, bool isEnabled = true)
        => new(name, null, null, null, "0", false, false, isEnabled, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "14", "fp");

    private sealed class FixedInventory(IReadOnlyList<PackageInventoryEntry> packages) : IPackageInventoryService
    {
        public Task<PackageInventoryResult> GetInventoryAsync(string serial, CancellationToken cancellationToken = default)
            => Task.FromResult(new PackageInventoryResult(serial, DateTimeOffset.UtcNow, packages, [], null));

        public Task<PackageInventoryEntry?> GetDetailsAsync(
            string serial, string packageName, CancellationToken cancellationToken = default)
            => Task.FromResult(packages.FirstOrDefault(package =>
                package.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FixedInspection(string fingerprint) : IDeviceInspectionService
    {
        public Task<DeviceInspectionResult> InspectAsync(
            string serial,
            IProgress<DeviceInspectionProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool deepScan = false)
        {
            InspectionSection<T> Section<T>(string name, T? value = default)
                => new(name, InspectionSectionState.Completed, value, []);
            var device = new AndroidDevice
            {
                Serial = serial,
                State = DeviceState.Device,
                ConnectionType = ConnectionType.Usb,
                BuildFingerprint = fingerprint
            };
            return Task.FromResult(new DeviceInspectionResult(
                serial,
                DateTimeOffset.UtcNow,
                Section("Overview", device),
                Section<CpuInfo>("CPU"),
                Section<MemoryInfo>("Memory"),
                Section<GraphicsInfo>("Graphics"),
                Section<DisplayInfo>("Display"),
                Section<StorageInfo>("Storage"),
                Section<SecurityInfo>("Security"),
                Section<BootInfo>("Boot"),
                Section<GsiInfo>("Gsi"),
                Section<NetworkInfo>("Network"),
                Section<RuntimeInfo>("Runtime"),
                Section<IReadOnlyList<string>>("Features", []),
                Section<PackageSummaryInfo>("Packages"),
                Section<ServiceSummaryInfo>("Services"),
                Section<DeveloperVerificationInfo>("Developer Verification"),
                new Dictionary<string, string>(),
                [],
                []));
        }
    }

    private sealed class RecordingScripts : IScriptExecutionService
    {
        public bool Executed { get; private set; }
        public string? LastTargetSerial { get; private set; }
        public string? LastScriptName { get; private set; }
        public string? LastUndoSerial { get; private set; }

        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptDefinition script,
            AndroidDevice target,
            CancellationToken cancellationToken = default)
        {
            Executed = true;
            LastTargetSerial = target.Serial;
            LastScriptName = script.Name;
            return Task.FromResult(new ScriptExecutionResult(42, "Completed", script.Actions.Count, 0, true));
        }

        public Task<ScriptUndoResult> UndoAsync(
            long executionId,
            string serial,
            CancellationToken cancellationToken = default)
        {
            LastUndoSerial = serial;
            return Task.FromResult(new ScriptUndoResult(executionId, "Completed", 1, 0));
        }
    }
}
