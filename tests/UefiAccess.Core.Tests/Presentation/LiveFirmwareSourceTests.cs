// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Tests.Safety;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// <see cref="LiveFirmwareSource"/> against fakes for the detector, the privilege manager, the
/// store and the identity inspector: the three precondition refusals in the CLI's order with the
/// CLI's sentences, the loaded result with variables, method, identity and warnings, the unit
/// check over a made-up machine ID, the failure paths (nothing enumerated, a throwing service, a
/// cancelled token) and the production composition. Nothing here touches WMI, the registry or a
/// native call.
/// </summary>
public sealed class LiveFirmwareSourceTests
{
    private const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;

    private static readonly FirmwareVariable BootOrder = new("BootOrder", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x01, 0x00, 0x00, 0x00 });
    private static readonly FirmwareVariable Timeout = new("Timeout", KnownGuids.EfiGlobalVariable, NvBsRt, new byte[] { 0x05, 0x00 });
    private static readonly FirmwareVariable SecureBoot = new("SecureBoot", KnownGuids.EfiGlobalVariable, VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess, new byte[] { 0x01 });

    // ---- description -------------------------------------------------------------------------

    [Fact]
    public void DescribesItselfAsTheLiveFirmwareAndNotOffline()
    {
        LiveFirmwareSource source = Source();

        Assert.Equal(Strings.LiveSourceName, source.Description);
        Assert.Equal("Live firmware", source.Description);
        Assert.False(source.IsOffline);
    }

    [Fact]
    public void ConstructorRejectsNullServices()
    {
        FakeDetector detector = new(FirmwareType.Uefi);
        FakePrivileges privileges = new(elevated: true, PrivilegeState.Enabled);
        InMemoryFirmwareVariableStore store = new(Array.Empty<FirmwareVariable>());
        FixedInspector identity = new(Identity());

        Assert.Throws<ArgumentNullException>(() => new LiveFirmwareSource(null!, privileges, store, identity));
        Assert.Throws<ArgumentNullException>(() => new LiveFirmwareSource(detector, null!, store, identity));
        Assert.Throws<ArgumentNullException>(() => new LiveFirmwareSource(detector, privileges, null!, identity));
        Assert.Throws<ArgumentNullException>(() => new LiveFirmwareSource(detector, privileges, store, null!));
    }

    // ---- refusals, in the CLI's order -------------------------------------------------------

    [Theory]
    [InlineData(FirmwareType.LegacyBios)]
    [InlineData(FirmwareType.Unknown)]
    public async Task NotUefi_IsRefusedWithTheCliSentence_BeforeAnythingElseRuns(FirmwareType firmwareType)
    {
        FakeDetector detector = new(firmwareType);
        FakePrivileges privileges = new(elevated: true, PrivilegeState.Enabled);
        CountingStore store = new(new InMemoryFirmwareVariableStore(new[] { BootOrder }));
        FixedInspector identity = new(Identity());

        SourceResult result = await new LiveFirmwareSource(detector, privileges, store, identity).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.NotUefiSentence, result.Message);
        Assert.Equal(firmwareType, result.FirmwareType);
        Assert.Null(result.Privilege);
        Assert.Empty(result.Variables);
        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Identity);
        Assert.Equal("Live firmware", result.SourceDescription);
        Assert.False(result.IsOffline);
        Assert.Equal(0, privileges.ElevationChecks);
        Assert.Equal(0, privileges.EnableCalls);
        Assert.Equal(0, store.EnumerateCalls);
        Assert.Equal(0, identity.InspectCalls);
    }

    [Fact]
    public async Task DetectorThatThrows_CountsAsUnknownAndIsRefusedAsNotUefi()
    {
        FakeDetector detector = new(FirmwareType.Uefi) { Throw = true };
        LiveFirmwareSource source = Source(detector: detector);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.NotUefiSentence, result.Message);
        Assert.Equal(FirmwareType.Unknown, result.FirmwareType);
    }

    [Fact]
    public async Task NotElevated_IsRefusedWithTheCliSentence_WithoutEnablingThePrivilege()
    {
        FakePrivileges privileges = new(elevated: false, PrivilegeState.Enabled);
        CountingStore store = new(new InMemoryFirmwareVariableStore(new[] { BootOrder }));
        FixedInspector identity = new(Identity());
        LiveFirmwareSource source = new(new FakeDetector(FirmwareType.Uefi), privileges, store, identity);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.NotElevatedSentence, result.Message);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.Equal(PrivilegeState.NotElevated, result.Privilege);
        Assert.Empty(result.Variables);
        Assert.Null(result.Identity);
        Assert.Equal(1, privileges.ElevationChecks);
        Assert.Equal(0, privileges.EnableCalls);
        Assert.Equal(0, store.EnumerateCalls);
        Assert.Equal(0, identity.InspectCalls);
    }

    [Fact]
    public async Task ElevationCheckThatThrows_CountsAsNotElevated()
    {
        FakePrivileges privileges = new(elevated: true, PrivilegeState.Enabled) { ThrowOnElevation = true };
        LiveFirmwareSource source = Source(privileges: privileges);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.NotElevatedSentence, result.Message);
        Assert.Equal(PrivilegeState.NotElevated, result.Privilege);
    }

    [Fact]
    public async Task PrivilegeAbsent_IsRefusedWithTheGuiSentence_WithoutEnumerating()
    {
        FakePrivileges privileges = new(elevated: true, PrivilegeState.ElevatedPrivilegeAbsent);
        CountingStore store = new(new InMemoryFirmwareVariableStore(new[] { BootOrder }));
        FixedInspector identity = new(Identity());
        LiveFirmwareSource source = new(new FakeDetector(FirmwareType.Uefi), privileges, store, identity);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.PrivilegeAbsentSentence, result.Message);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.Equal(PrivilegeState.ElevatedPrivilegeAbsent, result.Privilege);
        Assert.Empty(result.Variables);
        Assert.Empty(result.Warnings);
        Assert.Null(result.Identity);
        Assert.Equal(1, privileges.EnableCalls);
        Assert.Equal(0, store.EnumerateCalls);
        Assert.Equal(0, identity.InspectCalls);
    }

    [Fact]
    public async Task PrivilegeEnableThatThrows_IsRefusedAsAbsent_WithTheExceptionAsAWarning()
    {
        FakePrivileges privileges = new(elevated: true, PrivilegeState.Enabled) { ThrowOnEnable = true };
        LiveFirmwareSource source = Source(privileges: privileges);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.PrivilegeAbsentSentence, result.Message);
        Assert.Equal(PrivilegeState.ElevatedPrivilegeAbsent, result.Privilege);
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(LiveFirmwareSource.PrivilegeDetailPrefix, warning, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", warning, StringComparison.Ordinal);
        Assert.Contains("enable failed", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivilegeManagerThatReportsNotElevatedAfterAll_IsRefusedAsNotElevated()
    {
        // The elevation check said yes but the enable attempt says the token is not elevated:
        // the manager's verdict wins, with the CLI's not-elevated sentence.
        FakePrivileges privileges = new(elevated: true, PrivilegeState.NotElevated);
        LiveFirmwareSource source = Source(privileges: privileges);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.NotElevatedSentence, result.Message);
        Assert.Equal(PrivilegeState.NotElevated, result.Privilege);
    }

    [Fact]
    public async Task RefusalOrder_FirmwareTypeBeforeElevationBeforePrivilege()
    {
        // Everything wrong at once: the firmware type sentence wins.
        SourceResult all = await new LiveFirmwareSource(
            new FakeDetector(FirmwareType.LegacyBios),
            new FakePrivileges(elevated: false, PrivilegeState.ElevatedPrivilegeAbsent),
            new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>()),
            new FixedInspector(Identity())).LoadAsync(CancellationToken.None);
        Assert.Equal(Strings.NotUefiSentence, all.Message);

        // UEFI but neither elevated nor privileged: elevation wins.
        SourceResult twoWrong = await new LiveFirmwareSource(
            new FakeDetector(FirmwareType.Uefi),
            new FakePrivileges(elevated: false, PrivilegeState.ElevatedPrivilegeAbsent),
            new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>()),
            new FixedInspector(Identity())).LoadAsync(CancellationToken.None);
        Assert.Equal(Strings.NotElevatedSentence, twoWrong.Message);
    }

    // ---- loaded ------------------------------------------------------------------------------

    [Fact]
    public async Task AllPreconditionsHold_LoadsTheStoreWithMethodIdentityAndPrivilege()
    {
        InMemoryFirmwareVariableStore inner = new(new[] { Timeout, BootOrder, SecureBoot });
        CountingStore store = new(inner);
        FixedInspector identity = new(Identity());
        FakePrivileges privileges = new(elevated: true, PrivilegeState.Enabled);
        LiveFirmwareSource source = new(new FakeDetector(FirmwareType.Uefi), privileges, store, identity);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Null(result.Message);
        Assert.Equal("Live firmware", result.SourceDescription);
        Assert.False(result.IsOffline);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.Equal(PrivilegeState.Enabled, result.Privilege);
        Assert.Equal(EnumerationMethod.InMemory, result.Method);
        Assert.Equal(3, result.Variables.Count);
        Assert.Equal(new[] { "BootOrder", "SecureBoot", "Timeout" }, result.Variables.Select(variable => variable.Name));
        Assert.Same(BootOrder, result.Variables[0]);
        Assert.Empty(result.Warnings);
        Assert.Same(identity.Result, result.Identity);
        Assert.Equal(1, privileges.EnableCalls);
        Assert.Equal(1, store.EnumerateCalls);
        Assert.Equal(1, identity.InspectCalls);
    }

    [Fact]
    public async Task EmptyStoreWithoutError_LoadsZeroVariables()
    {
        LiveFirmwareSource source = Source(store: new InMemoryFirmwareVariableStore(Array.Empty<FirmwareVariable>()));

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Empty(result.Variables);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task IdentityErrors_BecomeWarnings_AndTheLoadStillSucceeds()
    {
        MachineIdentityResult identity = Identity() with { Errors = new[] { "Win32_BIOS: query timed out", " ", "Win32_Processor: access denied" } };
        LiveFirmwareSource source = Source(identity: new FixedInspector(identity));

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(
            new[]
            {
                LiveFirmwareSource.IdentityWarningPrefix + "Win32_BIOS: query timed out",
                LiveFirmwareSource.IdentityWarningPrefix + "Win32_Processor: access denied",
            },
            result.Warnings);
        Assert.Same(identity, result.Identity);
    }

    [Fact]
    public async Task IdentityInspectorThatThrows_LeavesIdentityNullWithAWarning_AndTheLoadStillSucceeds()
    {
        FixedInspector identity = new(Identity()) { Throw = true };
        LiveFirmwareSource source = Source(identity: identity);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Null(result.Identity);
        string warning = Assert.Single(result.Warnings);
        Assert.StartsWith(LiveFirmwareSource.IdentityWarningPrefix, warning, StringComparison.Ordinal);
        Assert.Contains("inspect failed", warning, StringComparison.Ordinal);
        Assert.Single(result.Variables);
    }

    [Fact]
    public async Task PartialEnumeration_LoadsTheVariablesWithTheErrorAndWarningsAsWarnings()
    {
        FixedResultStore store = new(new EnumerationResult(
            new[] { BootOrder, Timeout },
            EnumerationMethod.KnownNamesFallback,
            2,
            "NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D)",
            new[] { "known-names fallback in use: vendor variables are not listed", "  ", "Boot0002: ERROR_ENVVAR_NOT_FOUND (203)" }));
        LiveFirmwareSource source = Source(store: store);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(EnumerationMethod.KnownNamesFallback, result.Method);
        Assert.Equal(2, result.Variables.Count);
        Assert.Equal(
            new[]
            {
                LiveFirmwareSource.EnumerationIncompletePrefix + "NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D)",
                "known-names fallback in use: vendor variables are not listed",
                "Boot0002: ERROR_ENVVAR_NOT_FOUND (203)",
            },
            result.Warnings);
    }

    // ---- failed ------------------------------------------------------------------------------

    [Fact]
    public async Task EnumerationThatProducesNothingWithAnError_Fails_WithTheCliWording()
    {
        FixedResultStore store = new(new EnumerationResult(
            Array.Empty<FirmwareVariable>(),
            EnumerationMethod.None,
            0,
            "NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D)",
            Array.Empty<string>()));
        FixedInspector identity = new(Identity());
        LiveFirmwareSource source = Source(store: store, identity: identity);

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.Equal(
            "No variables could be enumerated: NtEnumerateSystemEnvironmentValuesEx failed: STATUS_INVALID_PARAMETER (0xC000000D)",
            result.Message);
        Assert.Equal(FirmwareType.Uefi, result.FirmwareType);
        Assert.Equal(PrivilegeState.Enabled, result.Privilege);
        Assert.Empty(result.Variables);
        Assert.Equal(EnumerationMethod.None, result.Method);
        Assert.Same(identity.Result, result.Identity);
    }

    [Fact]
    public async Task StoreThatThrows_Fails_WithTheExceptionInTheMessage()
    {
        LiveFirmwareSource source = Source(store: new ThrowingStore());

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.Equal("Enumerate threw InvalidOperationException: native seam exploded", result.Message);
        Assert.Equal(PrivilegeState.Enabled, result.Privilege);
        Assert.Empty(result.Variables);
    }

    [Fact]
    public async Task StoreThatReturnsNull_Fails_WithoutThrowing()
    {
        LiveFirmwareSource source = Source(store: new FixedResultStore(null));

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.Equal(LiveFirmwareSource.EnumerationFailedPrefix + "Enumerate returned no result", result.Message);
    }

    [Fact]
    public async Task CancelledToken_Fails_WithoutRunningAnyStep()
    {
        FakeDetector detector = new(FirmwareType.Uefi);
        CountingStore store = new(new InMemoryFirmwareVariableStore(new[] { BootOrder }));
        LiveFirmwareSource source = Source(detector: detector, store: store);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task<SourceResult> task = source.LoadAsync(cancellation.Token);
        SourceResult result = await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(SourceStatus.Failed, result.Status);
        Assert.Equal(LiveFirmwareSource.CancelledMessage, result.Message);
        Assert.Equal(0, detector.DetectCalls);
        Assert.Equal(0, store.EnumerateCalls);
    }

    // ---- composition -------------------------------------------------------------------------

    [Fact]
    public void CreateWindows_ComposesTheProductionServicesWithoutTouchingThem()
    {
        LiveFirmwareSource source = LiveFirmwareSource.CreateWindows(null);

        Assert.Equal("Live firmware", source.Description);
        Assert.False(source.IsOffline);

        // The production unit check, compared with the constant through the Windows read; not run here.
        EnabledUnit unit = Assert.IsType<EnabledUnit>(source.Unit);
        Assert.Equal(PlatformPolicy.EnabledUnitMachineIdSha256, unit.ExpectedSha256);
        Assert.Equal(typeof(PlatformPolicy).GetMethod(nameof(PlatformPolicy.ReadWindowsMachineId)), unit.MachineIdSource.Method);
    }

    // ---- the unit check (decided 25 September 2026) ------------------------------------------

    [Theory]
    [InlineData(SafetyFixtures.TestUnitMachineId, true)]
    [InlineData(SafetyFixtures.OtherUnitMachineId, false)]
    [InlineData(null, false)]
    [InlineData("not a guid", false)]
    public async Task ALoadedResult_SaysWhetherTheUnitCheckConfirmedTheTestUnit(string? machineId, bool confirmed)
    {
        LiveFirmwareSource source = Source(unit: SafetyFixtures.Unit(machineId));

        SourceResult result = await source.LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Equal(confirmed, result.UnitConfirmed);
    }

    [Fact]
    public async Task ASourceWithoutAUnitCheck_NeverConfirmsTheUnit_AndReadsNoMachineId()
    {
        SourceResult result = await Source().LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Loaded, result.Status);
        Assert.Null(Source().Unit);
        Assert.False(result.UnitConfirmed);
    }

    [NonWindowsFact]
    public async Task CreateWindows_OnANonWindowsHost_IsRefusedAsNotUefi()
    {
        // Off Windows the real detector answers Unknown before any native or WMI call is made.
        SourceResult result = await LiveFirmwareSource.CreateWindows(null).LoadAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Refused, result.Status);
        Assert.Equal(Strings.NotUefiSentence, result.Message);
        Assert.Equal(FirmwareType.Unknown, result.FirmwareType);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static LiveFirmwareSource Source(
        IFirmwareTypeDetector? detector = null,
        IPrivilegeManager? privileges = null,
        IFirmwareVariableStore? store = null,
        IPlatformInspector<MachineIdentityResult>? identity = null,
        EnabledUnit? unit = null)
    {
        return new LiveFirmwareSource(
            detector ?? new FakeDetector(FirmwareType.Uefi),
            privileges ?? new FakePrivileges(elevated: true, PrivilegeState.Enabled),
            store ?? new InMemoryFirmwareVariableStore(new[] { BootOrder }),
            identity ?? new FixedInspector(Identity()),
            unit: unit);
    }

    internal static MachineIdentityResult Identity()
    {
        return new MachineIdentityResult(
            "Dell Inc.",
            "OptiPlex 7020",
            "OptiPlex 7020",
            "A18",
            "2019-04-05",
            "ABC1234",
            "0N4YC8",
            "Intel(R) Core(TM) i5-4590 CPU @ 3.30GHz",
            "Microsoft Windows 11 Pro",
            "10.0.22631",
            "22631",
            true,
            "Expected model confirmed: 2014 Dell OptiPlex 7020 (model, BIOS version and CPU generation all match).",
            Array.Empty<string>());
    }

    // ---- fakes -------------------------------------------------------------------------------

    private sealed class FakeDetector : IFirmwareTypeDetector
    {
        private readonly FirmwareType _type;

        public FakeDetector(FirmwareType type)
        {
            _type = type;
        }

        public bool Throw { get; init; }

        public int DetectCalls { get; private set; }

        public FirmwareType Detect()
        {
            DetectCalls++;
            if (Throw)
            {
                throw new InvalidOperationException("detect failed");
            }

            return _type;
        }
    }

    private sealed class FakePrivileges : IPrivilegeManager
    {
        private readonly bool _elevated;
        private readonly PrivilegeState _state;

        public FakePrivileges(bool elevated, PrivilegeState state)
        {
            _elevated = elevated;
            _state = state;
        }

        public bool ThrowOnElevation { get; init; }

        public bool ThrowOnEnable { get; init; }

        public int ElevationChecks { get; private set; }

        public int EnableCalls { get; private set; }

        public bool IsElevated
        {
            get
            {
                ElevationChecks++;
                if (ThrowOnElevation)
                {
                    throw new InvalidOperationException("elevation check failed");
                }

                return _elevated;
            }
        }

        public PrivilegeState EnableSystemEnvironmentPrivilege()
        {
            EnableCalls++;
            if (ThrowOnEnable)
            {
                throw new InvalidOperationException("enable failed");
            }

            return _state;
        }
    }

    private sealed class FixedInspector : IPlatformInspector<MachineIdentityResult>
    {
        public FixedInspector(MachineIdentityResult result)
        {
            Result = result;
        }

        public MachineIdentityResult Result { get; }

        public bool Throw { get; init; }

        public int InspectCalls { get; private set; }

        public MachineIdentityResult Inspect()
        {
            InspectCalls++;
            if (Throw)
            {
                throw new InvalidOperationException("inspect failed");
            }

            return Result;
        }
    }

    /// <summary>Counts the enumerations of an inner store so a test can prove a refusal stopped before it.</summary>
    private sealed class CountingStore : IFirmwareVariableStore
    {
        private readonly IFirmwareVariableStore _inner;

        public CountingStore(IFirmwareVariableStore inner)
        {
            _inner = inner;
        }

        public int EnumerateCalls { get; private set; }

        public EnumerationResult Enumerate()
        {
            EnumerateCalls++;
            return _inner.Enumerate();
        }

        public ReadResult Read(string name, Guid vendorGuid) => _inner.Read(name, vendorGuid);
    }

    /// <summary>A store whose enumeration returns a fixed result (possibly null, which a real store never does).</summary>
    private sealed class FixedResultStore : IFirmwareVariableStore
    {
        private readonly EnumerationResult? _result;

        public FixedResultStore(EnumerationResult? result)
        {
            _result = result;
        }

        public EnumerationResult Enumerate() => _result!;

        public ReadResult Read(string name, Guid vendorGuid) => new(null, ReadStatus.Failed, "fixed store", 0);
    }

    private sealed class ThrowingStore : IFirmwareVariableStore
    {
        public EnumerationResult Enumerate() => throw new InvalidOperationException("native seam exploded");

        public ReadResult Read(string name, Guid vendorGuid) => throw new InvalidOperationException("native seam exploded");
    }
}
