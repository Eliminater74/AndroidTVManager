using System.Collections.ObjectModel;
using AndroidTVManager.App.Services;
using AndroidTVManager.App.ViewModels;
using AndroidTVManager.Core.Abstractions;
using AndroidTVManager.Core.Models;
using FluentAssertions;

namespace AndroidTVManager.Tests;

public sealed class DebloatPageViewModelTests
{
    [Fact]
    public void No_device_explains_connect_and_disables_one_click_actions()
    {
        var vm = CreateVm();

        vm.HasAuthorizedTarget.Should().BeFalse();
        vm.SafeActionLabel.Should().Be("Connect a device");
        vm.RecommendedActionLabel.Should().Be("Connect a device");
        vm.DeepActionLabel.Should().Be("Connect a device");
        vm.CanRunSafe.Should().BeFalse();
        vm.CanRunRecommended.Should().BeFalse();
        vm.CanRunDeep.Should().BeFalse();
        vm.CanRestoreLast.Should().BeFalse();
        vm.Status.Should().Contain("Connect a device");
    }

    [Fact]
    public async Task Zero_candidate_profile_disables_run_and_explains_nothing_to_clean()
    {
        var cleanup = new FakeCleanup { Overview = SampleOverview(recommendedActions: 0) };
        var vm = CreateVm(cleanup);
        vm.SelectedDevice = Shield();
        await vm.RefreshOverviewCommand.ExecuteAsync(null);

        vm.RecommendedProfile!.HasActions.Should().BeFalse();
        vm.CanRunRecommended.Should().BeFalse();
        vm.RecommendedActionLabel.Should().Be("Nothing to clean");
        vm.DetectedProfileText.Should().Be("NVIDIA Shield TV — darcy");
    }

    [Fact]
    public async Task Already_clean_profile_shows_clean_instead_of_a_no_op_run_button()
    {
        var cleanup = new FakeCleanup { Overview = SampleOverview(recommendedActions: 0, alreadyClean: true) };
        var vm = CreateVm(cleanup);
        vm.SelectedDevice = Shield();
        await vm.RefreshOverviewCommand.ExecuteAsync(null);

        vm.RecommendedProfile!.IsAlreadyClean.Should().BeTrue();
        vm.CanRunRecommended.Should().BeFalse();
        vm.RecommendedActionLabel.Should().Be("Clean");
        vm.RecommendedProfile.StatusText.Should().Contain("already matches");
    }

    [Fact]
    public async Task Actions_available_enable_recommended_cleanup()
    {
        var cleanup = new FakeCleanup { Overview = SampleOverview(recommendedActions: 2) };
        var vm = CreateVm(cleanup);
        vm.SelectedDevice = Shield();
        await vm.RefreshOverviewCommand.ExecuteAsync(null);

        vm.CanRunRecommended.Should().BeTrue();
        vm.RecommendedActionLabel.Should().Be("Run Recommended Cleanup");
        vm.RecommendedProfile!.ActionCount.Should().Be(2);
        vm.ScanSummary.Should().Contain("packages scanned");
    }

    [Fact]
    public async Task Run_recommended_builds_the_plan_without_create_preview()
    {
        var cleanup = new FakeCleanup { Overview = SampleOverview(recommendedActions: 1) };
        var execution = new FakeExecution();
        var planner = new ThrowingPlanner();
        var confirmation = new FakeConfirmation();
        var vm = CreateVm(cleanup, execution, planner, confirmation);
        vm.SelectedDevice = Shield();

        await vm.RunCleanupCommand.ExecuteAsync(DebloatCleanupKind.Recommended);

        planner.Called.Should().BeFalse();
        cleanup.CreateOverviewCalls.Should().BeGreaterThanOrEqualTo(2);
        confirmation.LastTitle.Should().Be("Recommended Cleanup");
        confirmation.LastConfirmLabel.Should().Be("Run Cleanup");
        execution.LastPlan.Should().NotBeNull();
        execution.LastPlan!.Serial.Should().Be("tv-1");
        execution.LastPlan.Items.Should().Contain(item => item.Selected && item.Package.PackageName == "com.nvidia.stats");
        vm.LastResult.Should().NotBeNull();
        vm.CanRestoreLast.Should().BeTrue();
        vm.LastResult!.Summary.Should().Contain("completed");
        vm.HasLastResult.Should().BeTrue();
    }

    [Fact]
    public async Task Device_state_change_before_execution_rebuilds_and_does_not_mutate()
    {
        var first = SampleOverview(recommendedActions: 1, fingerprint: "fp-1");
        var second = SampleOverview(recommendedActions: 1, fingerprint: "fp-2");
        var cleanup = new FakeCleanup { Overview = first };
        var execution = new FakeExecution();
        var vm = CreateVm(cleanup, execution);
        vm.SelectedDevice = Shield();
        await vm.RefreshOverviewCommand.ExecuteAsync(null);
        cleanup.Overviews = new Queue<DebloatCleanupOverview>([first, second]);

        await vm.RunCleanupCommand.ExecuteAsync(DebloatCleanupKind.Recommended);

        execution.LastPlan.Should().BeNull();
        vm.Status.Should().Contain("device state changed");
        vm.Overview!.BuildFingerprint.Should().Be("fp-2");
    }

    [Fact]
    public async Task Partial_failure_is_reported_and_restore_stays_available_when_undo_exists()
    {
        var cleanup = new FakeCleanup { Overview = SampleOverview(recommendedActions: 2) };
        var execution = new FakeExecution
        {
            Result = new ScriptExecutionResult(9, "Partial", 1, 1, true)
        };
        var vm = CreateVm(cleanup, execution);
        vm.SelectedDevice = Shield();

        await vm.RunCleanupCommand.ExecuteAsync(DebloatCleanupKind.Recommended);

        vm.LastResult!.Failed.Should().Be(1);
        vm.LastResult.Succeeded.Should().Be(1);
        vm.LastResult.Summary.Should().Contain("partially completed");
        vm.CanRestoreLast.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_is_locked_to_the_original_device()
    {
        var cleanup = new FakeCleanup { Overview = SampleOverview(recommendedActions: 1) };
        var execution = new FakeExecution();
        var vm = CreateVm(cleanup, execution);
        vm.SelectedDevice = Shield();
        await vm.RunCleanupCommand.ExecuteAsync(DebloatCleanupKind.Recommended);

        vm.SelectedDevice = new AndroidDevice
        {
            Serial = "other",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network
        };
        await vm.RestoreLastCommand.ExecuteAsync(null);

        execution.UndoSerial.Should().BeNull();
        vm.Status.Should().Contain("locked to the original device");
    }

    [Fact]
    public async Task Restore_refreshes_profile_counts()
    {
        var dirty = SampleOverview(recommendedActions: 1);
        var clean = SampleOverview(recommendedActions: 0, alreadyClean: true);
        var cleanup = new FakeCleanup { Overview = dirty };
        var execution = new FakeExecution();
        var vm = CreateVm(cleanup, execution);
        vm.SelectedDevice = Shield();
        await vm.RunCleanupCommand.ExecuteAsync(DebloatCleanupKind.Recommended);
        cleanup.Overview = clean;

        await vm.RestoreLastCommand.ExecuteAsync(null);

        execution.UndoSerial.Should().Be("tv-1");
        vm.RecommendedProfile!.IsAlreadyClean.Should().BeTrue();
        vm.CanRestoreLast.Should().BeFalse();
        vm.Status.Should().Contain("Restore");
    }

    [Fact]
    public void Custom_mode_is_opt_in_and_keeps_create_preview()
    {
        var vm = CreateVm();
        vm.IsCustomMode.Should().BeFalse();
        vm.OpenCustomCommand.Execute(null!);
        vm.IsCustomMode.Should().BeTrue();
        vm.CloseCustomCommand.Execute(null!);
        vm.IsCustomMode.Should().BeFalse();
    }

    private static DebloatPageViewModel CreateVm(
        FakeCleanup? cleanup = null,
        FakeExecution? execution = null,
        IDebloatPlanner? planner = null,
        FakeConfirmation? confirmation = null)
        => new(
            planner ?? new ThrowingPlanner(),
            cleanup ?? new FakeCleanup { Overview = SampleOverview() },
            execution ?? new FakeExecution(),
            confirmation ?? new FakeConfirmation(),
            new NoIcons(),
            new MemorySettings(),
            new EmptyCatalog(),
            []);

    private static AndroidDevice Shield()
        => new()
        {
            Serial = "tv-1",
            Manufacturer = "NVIDIA",
            Brand = "NVIDIA",
            Model = "SHIELD Android TV",
            Product = "darcy",
            DeviceName = "darcy",
            AndroidVersion = "11",
            State = DeviceState.Device,
            ConnectionType = ConnectionType.Network,
            BuildFingerprint = "fp"
        };

    private static DebloatCleanupOverview SampleOverview(
        int recommendedActions = 0,
        bool alreadyClean = false,
        string fingerprint = "fp")
    {
        var package = new PackageInventoryEntry(
            "com.nvidia.stats", null, null, null, "0", true, false, true, true, false, [],
            DateTimeOffset.UtcNow, "tv-1", "11", fingerprint);
        var assessment = new PackageAssessment(
            package.PackageName,
            PackageRiskLevel.Caution,
            PackageConfidence.High,
            "Telemetry",
            "Optional telemetry/statistics component",
            "Disable",
            ["NVIDIA Shield reviewed package profile"],
            [new PackageImpact("Telemetry", "Optional telemetry/statistics component")],
            false,
            "test");
        var selected = recommendedActions > 0;
        var item = new DebloatPlanItem(package, assessment, DebloatAction.Disable, selected, selected ? null : "Not included");
        var recommended = new DebloatCleanupProfileState(
            DebloatCleanupKind.Recommended,
            "recommended",
            "Recommended Cleanup",
            "Reviewed cleanup for NVIDIA Shield TV — darcy.",
            DebloatPreset.Medium,
            "Fully reversible",
            [],
            recommendedActions,
            alreadyClean ? 2 : 0,
            0,
            recommendedActions,
            0,
            recommendedActions > 0,
            alreadyClean,
            alreadyClean
                ? "Device already matches this cleanup profile."
                : recommendedActions > 0
                    ? $"{recommendedActions} action(s) available · 0 already cleaned"
                    : "Nothing left to clean.",
            recommendedActions > 0 ? "Run Recommended Cleanup" : alreadyClean ? "Clean" : "Nothing to clean");
        var safe = recommended with
        {
            Kind = DebloatCleanupKind.Safe,
            Id = "safe",
            DisplayName = "Safe Cleanup",
            Description = "No Safe Cleanup actions are available for this device.",
            PlannerPreset = DebloatPreset.Simple,
            ActionCount = 0,
            DisableCount = 0,
            HasActions = false,
            IsAlreadyClean = false,
            StatusText = "No Safe Cleanup actions are available for this device.",
            ActionLabel = "Nothing to clean"
        };
        var deep = recommended with
        {
            Kind = DebloatCleanupKind.Deep,
            Id = "deep",
            DisplayName = "Deep Cleanup",
            PlannerPreset = DebloatPreset.Aggressive,
            AdditionalActionCount = 0,
            ActionLabel = recommendedActions > 0 ? "Review / Run Deep Cleanup" : "Nothing to clean"
        };
        var source = new DebloatPlan("tv-1", fingerprint, DateTimeOffset.UtcNow, DebloatPreset.Aggressive, null, [item], []);
        return new(
            "tv-1",
            fingerprint,
            "NVIDIA SHIELD Android TV",
            "NVIDIA Shield TV — darcy",
            "Uses the reviewed NVIDIA Shield TV package profile.",
            true,
            12,
            0,
            ["Launcher", "Casting", "Play services"],
            [safe, recommended, deep],
            [],
            source,
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeCleanup : IDebloatCleanupService
    {
        public DebloatCleanupOverview Overview { get; set; } = null!;
        public Queue<DebloatCleanupOverview> Overviews { get; set; } = [];
        public int CreateOverviewCalls { get; private set; }

        public Task<DebloatCleanupOverview> CreateOverviewAsync(
            string serial,
            AndroidDevice? targetDevice = null,
            CancellationToken cancellationToken = default)
        {
            CreateOverviewCalls++;
            if (Overviews.Count > 0)
                Overview = Overviews.Dequeue();
            return Task.FromResult(Overview);
        }

        public DebloatPlan CreateProfilePlan(DebloatCleanupOverview overview, DebloatCleanupKind kind)
            => overview.SourcePlan with
            {
                Preset = kind switch
                {
                    DebloatCleanupKind.Safe => DebloatPreset.Simple,
                    DebloatCleanupKind.Deep => DebloatPreset.Aggressive,
                    _ => DebloatPreset.Medium
                },
                Items = overview.SourcePlan.Items.Select(item => DebloatPresetSelection.Apply(item, DebloatPreset.Medium)).ToArray()
            };
    }

    private sealed class FakeExecution : IDebloatExecutionService
    {
        public DebloatPlan? LastPlan { get; private set; }
        public string? UndoSerial { get; private set; }
        public ScriptExecutionResult Result { get; set; } = new(7, "Completed", 1, 0, true);

        public Task<ScriptExecutionResult> ExecuteAsync(DebloatPlan plan, CancellationToken cancellationToken = default)
        {
            LastPlan = plan;
            return Task.FromResult(Result);
        }

        public Task<ScriptUndoResult> RestoreAsync(long executionId, string serial, CancellationToken cancellationToken = default)
        {
            UndoSerial = serial;
            return Task.FromResult(new ScriptUndoResult(executionId, "Completed", 1, 0));
        }
    }

    private sealed class ThrowingPlanner : IDebloatPlanner
    {
        public bool Called { get; private set; }

        public Task<DebloatPlan> CreatePlanAsync(
            string serial,
            DebloatPreset preset,
            AndroidDevice? targetDevice = null,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("Create preview should not be required for one-click cleanup.");
        }
    }

    private sealed class FakeConfirmation : IConfirmationService
    {
        public string? LastTitle { get; private set; }
        public string? LastConfirmLabel { get; private set; }

        public bool Confirm(string title, string message, string confirmLabel = "Continue")
        {
            LastTitle = title;
            LastConfirmLabel = confirmLabel;
            return true;
        }
    }

    private sealed class NoIcons : IPackageIconService
    {
        public Task<string?> GetIconPathAsync(
            string serial, PackageInventoryEntry package, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class MemorySettings : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyCatalog : IPackageReferenceCatalog
    {
        public IReadOnlyList<PackageReferenceProfileMatch> GetProfiles() => [];

        public Task<PackageReferenceAnalysis> AnalyzeAsync(
            AndroidDevice device,
            IReadOnlyList<PackageInventoryEntry> packages,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PackageReferenceAnalysis(
                device.Serial,
                DateTimeOffset.UtcNow,
                [],
                new PackageReferenceSummary(0, [], 0, 0, [])));
    }
}
