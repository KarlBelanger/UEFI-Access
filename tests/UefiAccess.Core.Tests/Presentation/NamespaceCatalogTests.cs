// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Tests for <see cref="NamespaceCatalog"/>: the two UEFI specification GUIDs, the vendor GUIDs
/// the catalog knows, and the "Unknown vendor" fallback that carries the lowercase GUID.
/// </summary>
public sealed class NamespaceCatalogTests
{
    [Theory]
    [InlineData("8be4df61-93ca-11d2-aa0d-00e098032b8c", "EFI global variable")]
    [InlineData("d719b2cb-3d3a-4596-a3bc-dad00e67656f", "Image security database")]
    [InlineData("77fa9abd-0359-4d32-bd60-28f4e78f784b", "Microsoft")]
    [InlineData("605dab50-e046-4300-abb6-3dd810dd8b23", "Shim / Machine Owner Keys")]
    [InlineData("39b68c46-f7fb-441b-b6ec-16b0f69821f3", "Capsule update results")]
    [InlineData("414e6bdd-e47b-47cc-b244-bb61020cf516", "Hardware error records")]
    [InlineData("e20939be-32d4-41be-a150-897f85d49829", "Memory clearing request (TCG MOR)")]
    [InlineData("d1405d16-7afc-4695-bb12-41459d3695a2", "Network stack configuration")]
    public void Describe_KnownGuid_GivesTheLabel(string guidText, string expectedLabel)
    {
        Guid guid = new(guidText);

        NamespaceInfo info = NamespaceCatalog.Describe(guid);

        Assert.Equal(expectedLabel, info.Label);
        Assert.True(info.IsKnown);
        Assert.Equal(guid, info.Guid);
        Assert.Equal(expectedLabel, NamespaceCatalog.LabelFor(guid));
        Assert.True(NamespaceCatalog.IsKnown(guid));
        Assert.DoesNotContain(guidText, info.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_UsesTheCoreKnownGuidsForTheSpecificationNamespaces()
    {
        Assert.Equal(NamespaceCatalog.EfiGlobalVariableLabel, NamespaceCatalog.LabelFor(KnownGuids.EfiGlobalVariable));
        Assert.Equal(NamespaceCatalog.ImageSecurityDatabaseLabel, NamespaceCatalog.LabelFor(KnownGuids.EfiImageSecurityDatabase));
        Assert.Equal("EFI global variable", NamespaceCatalog.EfiGlobalVariableLabel);
        Assert.Equal("Image security database", NamespaceCatalog.ImageSecurityDatabaseLabel);
        Assert.Equal(new Guid("77fa9abd-0359-4d32-bd60-28f4e78f784b"), NamespaceCatalog.MicrosoftVendor);
        Assert.Equal(new Guid("605dab50-e046-4300-abb6-3dd810dd8b23"), NamespaceCatalog.ShimMachineOwnerKeys);
    }

    [Fact]
    public void Describe_IsCaseInsensitiveBecauseGuidsAre()
    {
        NamespaceInfo upper = NamespaceCatalog.Describe(new Guid("8BE4DF61-93CA-11D2-AA0D-00E098032B8C"));
        NamespaceInfo braced = NamespaceCatalog.Describe(new Guid("{D719B2CB-3D3A-4596-A3BC-DAD00E67656F}"));

        Assert.Equal("EFI global variable", upper.Label);
        Assert.Equal("Image security database", braced.Label);
        Assert.True(upper.IsKnown);
        Assert.True(braced.IsKnown);
    }

    [Theory]
    [InlineData("12345678-1234-1234-1234-123456789abc")]
    [InlineData("EC87D643-EBA4-4BB5-A1E5-3F3E36B20DA9")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Describe_UnknownGuid_GivesUnknownVendorPlusTheLowercaseGuid(string guidText)
    {
        Guid guid = new(guidText);

        NamespaceInfo info = NamespaceCatalog.Describe(guid);

        Assert.Equal("Unknown vendor " + guidText.ToLowerInvariant(), info.Label);
        Assert.False(info.IsKnown);
        Assert.Equal(guid, info.Guid);
        Assert.Equal(info.Label, NamespaceCatalog.LabelFor(guid));
        Assert.Equal(info.Label, NamespaceCatalog.UnknownLabelFor(guid));
        Assert.False(NamespaceCatalog.IsKnown(guid));
        Assert.StartsWith(Strings.UnknownVendorLabel + " ", info.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("{", info.Label, StringComparison.Ordinal);
        Assert.Equal(info.Label, info.Label.ToLowerInvariant().Replace("unknown vendor", "Unknown vendor", StringComparison.Ordinal));
    }

    [Fact]
    public void Known_ListsEveryCataloguedNamespaceOnce_WithTheEfiGlobalVariableFirst()
    {
        IReadOnlyList<NamespaceInfo> known = NamespaceCatalog.Known;

        Assert.Equal(8, known.Count);
        Assert.Equal(KnownGuids.EfiGlobalVariable, known[0].Guid);
        Assert.Equal(KnownGuids.EfiImageSecurityDatabase, known[1].Guid);
        Assert.Equal(known.Count, known.Select(info => info.Guid).Distinct().Count());
        Assert.Equal(known.Count, known.Select(info => info.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(known, info => Assert.True(info.IsKnown));
        Assert.All(known, info => Assert.Same(info, NamespaceCatalog.Describe(info.Guid)));
        Assert.All(known, info => Assert.False(string.IsNullOrWhiteSpace(info.Label)));
        Assert.All(known, info => Assert.DoesNotContain(info.Guid.ToString("D"), info.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NetworkStackSetup_IsLabelledAfterItsSetupForm_SoNetworkStackVarIsNoUnknownVendor()
    {
        // The Dell setup varstore of NetworkStackVar, the decoder's GUID.
        NamespaceInfo info = NamespaceCatalog.Describe(KnownGuids.NetworkStackSetup);

        Assert.True(info.IsKnown);
        Assert.Equal(NamespaceCatalog.NetworkStackSetupLabel, info.Label);
        Assert.Equal("Network stack configuration", info.Label);
        Assert.Equal(new Guid("d1405d16-7afc-4695-bb12-41459d3695a2"), KnownGuids.NetworkStackSetup);
        Assert.DoesNotContain(Strings.UnknownVendorLabel, info.Label, StringComparison.Ordinal);
        Assert.Same(info, NamespaceCatalog.Known[^1]);
    }

    [Fact]
    public void Labels_ContainNoWriteImplyingWords()
    {
        // Stricter than the label scan of the XAML tests: not even as a lowercase substring, since
        // the labels are spoken as part of every row sentence and group header.
        string[] forbidden = { "Apply", "Save", "Toggle", "Edit", "Write", "Set" };
        foreach (NamespaceInfo info in NamespaceCatalog.Known)
        {
            foreach (string word in forbidden)
            {
                Assert.DoesNotContain(word, info.Label, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.DoesNotContain("set", NamespaceCatalog.UnknownLabelFor(Guid.NewGuid()), StringComparison.OrdinalIgnoreCase);
    }
}
