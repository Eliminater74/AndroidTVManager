using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class PackageSafetyGateTests
{
    [Fact]
    public async Task Incomplete_inventory_blocks_destructive_mutations()
    {
        var gate = CreateGate(packages: [], error: "Required inventory evidence unavailable.");
        var decision = await gate.EvaluateAsync(new("tv-1", "com.example.app", PackageMutationKind.Disable));
        decision.Allowed.Should().BeFalse();
        decision.BlockReason.Should().Contain("Required inventory evidence unavailable");
    }

    [Fact]
    public async Task Automotive_or_non_owner_user_inventory_errors_block_disable()
    {
        var gate = CreateGate(
            [Package("com.example.app")],
            "Android Automotive inventory is read-only for debloat. Vehicle package dependencies need a dedicated reviewed profile.");
        (await gate.EvaluateAsync(new("tv-1", "com.example.app", PackageMutationKind.Disable)))
            .Allowed.Should().BeFalse();
        (await gate.EvaluateAsync(new("tv-1", "com.example.app", PackageMutationKind.Enable)))
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Active_launcher_is_safety_locked_against_disable_but_can_be_enabled()
    {
        var gate = CreateGate([Package("com.google.android.tvlauncher", isLauncher: true)]);
        var disable = await gate.EvaluateAsync(new("tv-1", "com.google.android.tvlauncher", PackageMutationKind.Disable));
        disable.Allowed.Should().BeFalse();
        disable.BlockReason.Should().Contain("protected");
        (await gate.EvaluateAsync(new("tv-1", "com.google.android.tvlauncher", PackageMutationKind.Enable)))
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Build_fingerprint_mismatch_blocks_destructive_mutations()
    {
        var gate = CreateGate([Package("com.example.app")], fingerprint: "live/fingerprint");
        var decision = await gate.EvaluateAsync(new(
            "tv-1",
            "com.example.app",
            PackageMutationKind.Disable,
            ExpectedBuildFingerprint: "old/fingerprint"));
        decision.Allowed.Should().BeFalse();
        decision.BlockReason.Should().Contain("build changed");
    }

    [Fact]
    public async Task Unlocked_optional_package_can_be_disabled()
    {
        var gate = CreateGate([Package("com.purefusion.iptv")]);
        var decision = await gate.EvaluateAsync(new("tv-1", "com.purefusion.iptv", PackageMutationKind.Disable));
        decision.Allowed.Should().BeTrue();
    }

    private static PackageSafetyGate CreateGate(
        IReadOnlyList<PackageInventoryEntry> packages,
        string? error = null,
        string? fingerprint = "fingerprint")
        => new(
            new FixedInventory(packages, error),
            new FixedInspection(fingerprint),
            new PackageClassifier(),
            new PackageReferenceCatalog());

    private static PackageInventoryEntry Package(string name, bool isLauncher = false)
        => new(
            name, null, null, null, "0", false, false, true, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "14", "fingerprint",
            IsActiveLauncher: isLauncher);

    private sealed class FixedInventory : IPackageInventoryService
    {
        private readonly IReadOnlyList<PackageInventoryEntry> _packages;
        private readonly string? _error;

        public FixedInventory(IReadOnlyList<PackageInventoryEntry> packages, string? error)
        {
            _packages = packages;
            _error = error;
        }

        public Task<PackageInventoryResult> GetInventoryAsync(string serial, CancellationToken cancellationToken = default)
            => Task.FromResult(new PackageInventoryResult(serial, DateTimeOffset.UtcNow, _packages, [], _error));

        public Task<PackageInventoryEntry?> GetDetailsAsync(string serial, string packageName, CancellationToken cancellationToken = default)
            => Task.FromResult(_packages.FirstOrDefault(package =>
                package.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class FixedInspection : IDeviceInspectionService
    {
        private readonly string? _fingerprint;

        public FixedInspection(string? fingerprint) => _fingerprint = fingerprint;

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
                BuildFingerprint = _fingerprint
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
}
