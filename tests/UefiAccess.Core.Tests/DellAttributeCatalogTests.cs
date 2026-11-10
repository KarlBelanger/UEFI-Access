// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Settings.Dell;
using UefiAccess.Core.Tests.Fixtures;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The agreed list as data: the ten rows of the gate memo
/// in their order, with the setup screen's words, the place in the BIOS setup, Dell Command |
/// Monitor's attribute and value names (checked against every name the A18 export holds) and
/// Dell Command | Configure's read option and words. Nothing here reads the machine.
/// </summary>
public sealed class DellAttributeCatalogTests
{
    private static readonly string[] Ids = { "BootListOption", "SecureBoot", "SataOperation", "TpmSecurity", "TpmActivation", "Virtualization", "VtForDirectIo", "IntegratedNic", "WakeOnLan", "NumLock" };

    [Fact]
    public void Default_HoldsTheTenRowsOfTheGateMemoInOrder()
    {
        IReadOnlyList<DellCatalogEntry> catalog = DellAttributeCatalog.Default;

        Assert.Equal(Ids, catalog.Select(entry => entry.Id));
        Assert.Equal(
            new[] { "Boot list option", "Secure Boot", "SATA operation", "TPM security", "TPM activation", "Virtualization", "VT for Direct I/O", "Integrated NIC", "Wake on LAN", "Num Lock at boot" },
            catalog.Select(entry => entry.DisplayName));
        Assert.Equal(
            new[] { "Boot Mode", "Secure Boot", "Embedded SATA Controller", "Trusted Platform Module", "Trusted Platform Module Activation", "Processor Virtualization Technology", "VT for Direct IO", "NIC", "Wake-On-LAN", "Num Lock" },
            catalog.Select(entry => entry.DcmAttributeName));
        Assert.Equal(
            new[] { "General", "Secure Boot", "System Configuration", "Security", "Security", "Virtualization Support", "Virtualization Support", "System Configuration", "Power Management", "POST Behavior" },
            catalog.Select(entry => entry.Category));
        Assert.Equal(
            new[]
            {
                "General, Boot Sequence, Boot List Option",
                "Secure Boot, Secure Boot Enable",
                "System Configuration, SATA Operation",
                "Security, TPM Security",
                "Security, TPM Security, TPM State",
                "Virtualization Support, Virtualization",
                "Virtualization Support, VT for Direct I/O",
                "System Configuration, Integrated NIC",
                "Power Management, Wake on LAN",
                "POST Behavior, Numlock LED",
            },
            catalog.Select(entry => entry.SetupLocation));
        Assert.Equal(
            new[]
            {
                "Legacy, UEFI",
                "Disabled, Enabled",
                "Disabled, ATA, AHCI",
                "Enabled, Disabled",
                "Deactivated, Activated",
                "Disabled, Enabled",
                "Disabled, Enabled",
                "Disabled, Enabled with PXE, Enabled, Enabled with image server",
                "Disabled, LAN Only, LAN with PXE Boot",
                "Disabled, Enabled",
            },
            catalog.Select(entry => string.Join(", ", entry.Values.Select(value => value.Text))));

        // Every value as the table of section 3.3 writes it: the text, Dell Command Monitor's name
        // and Dell Command Configure's words, in order, so no word can move to another value.
        Assert.Equal(
            new[]
            {
                "Legacy (BIOS; )|UEFI (UEFI; )",
                "Disabled (Disable; Disabled disable off)|Enabled (Enable; Enabled enable on)",
                "Disabled (Off; Disabled off)|ATA (ATA; Ata)|AHCI (AHCI; Ahci)",
                "Enabled (Enable; Enabled enable on)|Disabled (Disable; Disabled disable off)",
                "Deactivated (Deactivate; Disabled deactivated Deactivate)|Activated (Activate; Enabled activate Activated)",
                "Disabled (Disable; Disabled disable off)|Enabled (Enable; Enabled enable on)",
                "Disabled (Disable; Disabled disable off)|Enabled (Enable; Enabled enable on)",
                "Disabled (Disable; Disabled off)|Enabled with PXE (EnabledwithPXE; EnabledPxe on)|Enabled (Enable; Enabled onnopxe)|Enabled with image server (Enable image server; OnWithImageServerBoot)",
                "Disabled (Disable; Disabled disable)|LAN Only (LAN; LanOnly enable)|LAN with PXE Boot (LAN with PXE Boot; LanWithPxeBoot lanwithpxeboot)",
                "Disabled (Disable; Disabled disable off)|Enabled (Enable; Enabled enable on)",
            },
            catalog.Select(entry => string.Join("|", entry.Values.Select(value => $"{value.Text} ({value.DcmName}; {string.Join(" ", value.CctkTokens)})"))));

        // The table's read option and output names of each row.
        Assert.Equal(
            new (CctkReadOption?, string)[]
            {
                (null, string.Empty),
                (CctkReadOption.SecureBoot, "SecureBoot secureboot"),
                (CctkReadOption.SataOperation, "EmbSataRaid embsataraid"),
                (CctkReadOption.TpmSecurity, "TpmSecurity tpm"),
                (CctkReadOption.TpmActivation, "TpmActivation tpmactivation"),
                (CctkReadOption.Virtualization, "Virtualization virtualization"),
                (CctkReadOption.VtForDirectIo, "VtForDirectIo vtfordirectio"),
                (CctkReadOption.IntegratedNic, "EmbNic1 embnic1"),
                (CctkReadOption.WakeOnLan, "WakeOnLan wakeonlan"),
                (CctkReadOption.NumLock, "NumLock numlock"),
            },
            catalog.Select(entry => (entry.CctkOption, string.Join(" ", entry.CctkOutputNames))));

        DellCatalogEntry secureBoot = catalog[1];
        Assert.Equal(DellSentences.SecureBootDcmNote, secureBoot.DcmNote);
        Assert.Equal(DellSentences.SecureBootNoValueNote, secureBoot.NoValueNote);
        Assert.All(catalog.Where(entry => entry.Id != "SecureBoot"), entry => Assert.Null(entry.DcmNote ?? entry.NoValueNote));

        Assert.Equal("A18", DellAttributeCatalog.VerifiedBiosVersion);
        Assert.Equal("Approved list of the Dell BIOS settings this tool shows", DellAttributeCatalog.ListStatus);
    }

    [Fact]
    public void Default_CannotBeChangedByItsReaders()
    {
        Assert.False(DellAttributeCatalog.Default is DellCatalogEntry[], "the list is handed out as an array a caller could change");
        Assert.All(DellAttributeCatalog.Default, entry =>
        {
            Assert.False(entry.Values is DellValueText[], entry.Id + ": its values are an array a caller could change");
            Assert.All(entry.Values, value => Assert.False(value.CctkTokens is string[], entry.Id + ": a value's words are an array a caller could change"));
            Assert.False(entry.CctkOutputNames is string[] { Length: > 0 }, entry.Id + ": its output names are an array a caller could change");
        });
    }

    [Fact]
    public void EveryEntryHasDisplayNameCategorySetupLocationAndDcmName()
    {
        Assert.All(DellAttributeCatalog.Default, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName), entry.Id);
            Assert.False(string.IsNullOrWhiteSpace(entry.Category), entry.Id);
            Assert.False(string.IsNullOrWhiteSpace(entry.SetupLocation), entry.Id);
            Assert.False(string.IsNullOrWhiteSpace(entry.DcmAttributeName), entry.Id);
            Assert.StartsWith(entry.Category + ", ", entry.SetupLocation, StringComparison.Ordinal);
            Assert.NotEmpty(entry.Values);
            Assert.All(entry.Values, value =>
            {
                Assert.False(string.IsNullOrWhiteSpace(value.Text), entry.Id);
                Assert.False(string.IsNullOrWhiteSpace(value.DcmName), entry.Id + ": every value has Dell Command Monitor's name");
            });
        });
    }

    [Fact]
    public void NoTextContainsAPipeAGreaterThanOrReadOnly()
    {
        foreach (string text in ShownTexts().Append(DellAttributeCatalog.ListStatus))
        {
            Assert.DoesNotContain("|", text, StringComparison.Ordinal);
            Assert.DoesNotContain(">", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\u2013", text, StringComparison.Ordinal);
            Assert.DoesNotContain("read-only", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("read only", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoShownTextIsANumber()
    {
        Assert.All(
            DellAttributeCatalog.Default.SelectMany(entry => entry.Values).Select(value => value.Text),
            text =>
            {
                Assert.False(long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _), text + " is a number");
                Assert.False(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase), text + " is a code");
                Assert.Contains(text, character => char.IsLetter(character));
            });
    }

    [Fact]
    public void ValueTexts_AreUniquePerEntry()
    {
        Assert.All(DellAttributeCatalog.Default, entry =>
        {
            Assert.Equal(entry.Values.Count, entry.Values.Select(value => value.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(entry.Values.Count, entry.Values.Select(value => value.DcmName).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // A word of Dell Command Configure names one value only, in any casing.
            string[] words = entry.Values.SelectMany(value => value.CctkTokens.Distinct(StringComparer.OrdinalIgnoreCase)).ToArray();
            Assert.Equal(words.Length, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }

    [Fact]
    public void DependentEntries_ReferToAnEarlierEntryAndOneOfItsTexts()
    {
        IReadOnlyList<DellCatalogEntry> catalog = DellAttributeCatalog.Default;
        for (int i = 0; i < catalog.Count; i++)
        {
            DellCatalogEntry entry = catalog[i];
            Assert.Equal(entry.DependsOnId is null, entry.DependsOnText is null);
            if (entry.DependsOnId is null)
            {
                continue;
            }

            int index = catalog.ToList().FindIndex(other => other.Id == entry.DependsOnId);
            Assert.InRange(index, 0, i - 1);
            Assert.Contains(entry.DependsOnText, catalog[index].Values.Select(value => value.Text));

            // The not-applicable words name the other value, so it must be the only other one.
            Assert.Single(catalog[index].Values, value => value.Text != entry.DependsOnText);
        }

        DellCatalogEntry dependent = Assert.Single(catalog, entry => entry.DependsOnId is not null);
        Assert.Equal("TpmActivation", dependent.Id);
        Assert.Equal("TpmSecurity", dependent.DependsOnId);
        Assert.Equal("Enabled", dependent.DependsOnText);
    }

    [Fact]
    public void EveryCctkOptionMapsToAReadOptionArgument()
    {
        DellCatalogEntry[] read = DellAttributeCatalog.Default.Where(entry => entry.CctkOption is not null).ToArray();

        // Each of the nine read options once, in the order of the enumeration.
        Assert.Equal(Enum.GetValues<CctkReadOption>(), read.Select(entry => entry.CctkOption!.Value));
        Assert.All(read, entry =>
        {
            string argument = CctkReader.ArgumentFor(entry.CctkOption!.Value);
            Assert.StartsWith("--", argument, StringComparison.Ordinal);
            Assert.DoesNotContain("=", argument, StringComparison.Ordinal);

            // The output line carries the option's own name (the legacy one) or its 5.2.2 name.
            Assert.Equal(2, entry.CctkOutputNames.Count);
            Assert.Contains(argument[2..], entry.CctkOutputNames, StringComparer.OrdinalIgnoreCase);
            Assert.All(entry.Values, value => Assert.NotEmpty(value.CctkTokens));
        });
    }

    [Fact]
    public void BootListOption_HasNoCctkOption()
    {
        DellCatalogEntry bootList = DellAttributeCatalog.Default[0];

        Assert.Equal("BootListOption", bootList.Id);
        Assert.Null(bootList.CctkOption);
        Assert.Empty(bootList.CctkOutputNames);
        Assert.All(bootList.Values, value => Assert.Empty(value.CctkTokens));
        Assert.Single(DellAttributeCatalog.Default, entry => entry.CctkOption is null);
    }

    [Fact]
    public void EveryDescriptionSeenOnA18_IsMapped()
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = DcmRowFixtures.UnitA18();
        Assert.Equal(72, rows.Count);

        List<string> texts = new();
        foreach (DellCatalogEntry entry in DellAttributeCatalog.Default)
        {
            IReadOnlyDictionary<string, object?> row = Assert.Single(rows, candidate => (string?)candidate["AttributeName"] == entry.DcmAttributeName);
            IReadOnlyList<string> possible = DellInterfaceProbe.AlignedTextList(row["PossibleValues"]);
            IReadOnlyList<string> names = DellInterfaceProbe.AlignedTextList(row["PossibleValuesDescription"]);
            Assert.All(names, name => Assert.Contains(entry.Values, value => string.Equals(value.DcmName, name, StringComparison.OrdinalIgnoreCase)));

            string? current = DellInterfaceProbe.DescribeValues(DellInterfaceProbe.AlignedTextList(row["CurrentValue"]), possible, names);
            texts.Add(entry.Values.Single(value => string.Equals(value.DcmName, current, StringComparison.OrdinalIgnoreCase)).Text);
        }

        Assert.Equal(DcmRowFixtures.UnitA18Texts, texts);
    }

    [Fact]
    public void Find_ReturnsTheEntryOrNull()
    {
        foreach (DellCatalogEntry entry in DellAttributeCatalog.Default)
        {
            Assert.Same(entry, DellAttributeCatalog.Find(entry.Id));
        }

        Assert.Null(DellAttributeCatalog.Find("numlock"));
        Assert.Null(DellAttributeCatalog.Find("Num Lock"));
        Assert.Null(DellAttributeCatalog.Find("FastBoot"));
        Assert.Null(DellAttributeCatalog.Find(string.Empty));
        Assert.Null(DellAttributeCatalog.Find(null!));
    }

    /// <summary>Every text of the catalog a person sees or hears: names, places, value words and notes.</summary>
    private static IEnumerable<string> ShownTexts()
    {
        foreach (DellCatalogEntry entry in DellAttributeCatalog.Default)
        {
            yield return entry.DisplayName;
            yield return entry.Category;
            yield return entry.SetupLocation;
            foreach (DellValueText value in entry.Values)
            {
                yield return value.Text;
            }

            foreach (string? note in new[] { entry.DcmNote, entry.NoValueNote, entry.DependsOnText })
            {
                if (note is not null)
                {
                    yield return note;
                }
            }
        }
    }
}
