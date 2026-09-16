using AndroidTVManager.Core.Models;
using AndroidTVManager.Infrastructure.Packages;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DeploymentCompatibilityTests
{
    [Fact]
    public void Required_abi_without_live_evidence_is_unknown_required_evidence()
    {
        var compatibility = CreateService().CheckCompatibility(
            Profile(abi: "arm64-v8a"),
            Device());

        compatibility.State.Should().Be(DeploymentCompatibilityState.UnknownRequiredEvidence);
        compatibility.Reasons.Should().Contain(reason => reason.Contains("ABI"));
    }

    [Fact]
    public void Required_abi_is_compatible_when_the_live_list_contains_it()
    {
        var compatibility = CreateService().CheckCompatibility(
            Profile(abi: "arm64-v8a"),
            Device(),
            Inspection(abis: ["armeabi-v7a", "arm64-v8a"], features: ["android.software.leanback"]));

        compatibility.State.Should().Be(DeploymentCompatibilityState.Compatible);
    }

    [Fact]
    public void Required_abi_mismatch_is_incompatible()
    {
        var compatibility = CreateService().CheckCompatibility(
            Profile(abi: "arm64-v8a"),
            Device(),
            Inspection(abis: ["x86_64"], features: ["android.software.leanback"]));

        compatibility.State.Should().Be(DeploymentCompatibilityState.Incompatible);
        compatibility.Reasons.Should().Contain(reason => reason.Contains("does not provide"));
    }

    [Fact]
    public void Required_android_tv_without_feature_evidence_is_unknown_required_evidence()
    {
        var compatibility = CreateService().CheckCompatibility(
            Profile(requiresAndroidTv: true),
            Device());

        compatibility.State.Should().Be(DeploymentCompatibilityState.UnknownRequiredEvidence);
    }

    [Fact]
    public void Required_android_tv_with_leanback_is_compatible()
    {
        var compatibility = CreateService().CheckCompatibility(
            Profile(requiresAndroidTv: true),
            Device(),
            Inspection(abis: ["arm64-v8a"], features: ["android.software.leanback"]));

        compatibility.State.Should().Be(DeploymentCompatibilityState.Compatible);
    }

    [Fact]
    public async Task Deploy_fails_closed_when_mandatory_evidence_is_missing()
    {
        var service = CreateService();
        await FluentActions.Awaiting(() => service.DeployAsync(
                Profile(abi: "arm64-v8a"),
                Device(state: DeviceState.Device)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ABI*");
    }

    private static DeploymentProfileService CreateService()
        => new(null!, null!, null!, null!, null!);

    private static DeploymentProfile Profile(string? abi = null, bool? requiresAndroidTv = null)
        => new(
            1,
            "Test",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            abi,
            requiresAndroidTv,
            null,
            null,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            []);

    private static AndroidDevice Device(DeviceState state = DeviceState.Device)
        => new()
        {
            Serial = "tv-1",
            State = state,
            ConnectionType = ConnectionType.Network,
            ApiLevel = 34
        };

    private static DeviceInspectionResult Inspection(IReadOnlyList<string> abis, IReadOnlyList<string> features)
    {
        InspectionSection<T> Section<T>(string name, T? value = default)
            => new(name, InspectionSectionState.Completed, value, []);
        var cpu = new CpuInfo("arm64", abis.FirstOrDefault(), abis, 4, null, null, null, null, null, null, null, null);
        var device = Device();
        return new(
            device.Serial,
            DateTimeOffset.UtcNow,
            Section("Overview", device),
            Section("CPU", cpu),
            Section<MemoryInfo>("Memory"),
            Section<GraphicsInfo>("Graphics"),
            Section<DisplayInfo>("Display"),
            Section<StorageInfo>("Storage"),
            Section<SecurityInfo>("Security"),
            Section<BootInfo>("Boot"),
            Section<GsiInfo>("Gsi"),
            Section<NetworkInfo>("Network"),
            Section<RuntimeInfo>("Runtime"),
            Section<IReadOnlyList<string>>("Features", features),
            Section<PackageSummaryInfo>("Packages"),
            Section<ServiceSummaryInfo>("Services"),
            Section<DeveloperVerificationInfo>("Developer Verification"),
            new Dictionary<string, string>(),
            [],
            []);
    }
}
