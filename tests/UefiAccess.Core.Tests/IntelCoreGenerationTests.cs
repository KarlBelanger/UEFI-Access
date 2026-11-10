// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// CPU generation parsing for brand strings without an "Nth Gen" prefix. Intel numbered 1st-generation
/// parts with three digits and used a two-digit generation from the 10th onwards, so a four-digit
/// number that starts with 1 (Ice Lake i7-1065G7, Tiger Lake i5-1135G7, Alder Lake i5-1235U) is
/// 10th generation or later, never 1st. The expected-model check for the planned unit, an
/// i7-4790 in the 2014 OptiPlex 7020, must be unaffected.
/// </summary>
public sealed class IntelCoreGenerationTests
{
    private const string PlannedCpu = "Intel(R) Core(TM) i7-4790 CPU @ 3.60GHz";
    private const string IceLake = "Intel(R) Core(TM) i7-1065G7 CPU @ 1.30GHz";

    [Theory]
    [InlineData(IceLake, 10)]
    [InlineData("Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz", 10)]
    [InlineData("Intel(R) Core(TM) i3-1005G1 CPU @ 1.20GHz", 10)]
    [InlineData("Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", 11)]
    [InlineData("Intel(R) Core(TM) i7-10700 CPU @ 2.90GHz", 10)]
    [InlineData("Intel(R) Core(TM) i5-1235U", 12)]
    [InlineData("Intel(R) Core(TM) i7-14700", 14)]
    [InlineData("14th Gen Intel(R) Core(TM) i7-14700", 14)]
    [InlineData(PlannedCpu, 4)]
    [InlineData("Intel(R) Core(TM) i5-2500K CPU @ 3.30GHz", 2)]
    [InlineData("Intel(R) Core(TM) i7-920 CPU @ 2.67GHz", 1)]
    [InlineData("Intel(R) Core(TM) i5-520M CPU @ 2.40GHz", 1)]
    public void Generation_IsReadFromTheModelNumber(string cpu, int expected)
    {
        Assert.Equal(expected, MachineIdentity.TryGetIntelCoreGeneration(cpu));
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-1235U", true)]
    [InlineData("Intel(R) Core(TM) i7-14700", true)]
    [InlineData(IceLake, false)]
    [InlineData("Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", false)]
    [InlineData("Intel(R) Core(TM) i7-10700 CPU @ 2.90GHz", false)]
    [InlineData(PlannedCpu, false)]
    public void TwelfthGenOrNewer_UsesTheCorrectedGeneration(string cpu, bool expected)
    {
        Assert.Equal(expected, MachineIdentity.IsTwelfthGenOrNewer(cpu));
    }

    [Fact]
    public void ExpectedModel_IceLake_IsReportedAsTenthGeneration()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("XPS 13 7390 2-in-1", "A05", IceLake);

        Assert.False(evaluation.Confirmed);
        Assert.False(evaluation.CpuMatched);
        Assert.False(evaluation.LooksLikeRelaunch);
        Assert.Contains($"CPU \"{IceLake}\": 10th-generation Intel Core, not 4th generation (no match).", evaluation.Notes, StringComparison.Ordinal);
        Assert.DoesNotContain("1st-generation", evaluation.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedModel_PlannedOptiPlex7020WithI7_4790_IsConfirmed()
    {
        ExpectedModelEvaluation evaluation = MachineIdentity.EvaluateExpectedModel("OptiPlex 7020", "A18", PlannedCpu);

        Assert.True(evaluation.Confirmed);
        Assert.True(evaluation.ModelMatched);
        Assert.True(evaluation.BiosMatched);
        Assert.True(evaluation.CpuMatched);
        Assert.False(evaluation.LooksLikeRelaunch);
        Assert.Contains($"CPU \"{PlannedCpu}\": 4th-generation Intel (Haswell) part (match).", evaluation.Notes, StringComparison.Ordinal);
        Assert.Contains("Expected model confirmed: 2014 Dell OptiPlex 7020", evaluation.Notes, StringComparison.Ordinal);
    }
}
