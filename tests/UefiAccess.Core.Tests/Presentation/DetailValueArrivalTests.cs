// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Tests.Fixtures;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// What the window does with a detail value box's selection when keyboard focus reaches it
/// (<see cref="VariableDetailViewModel.SelectionOnKeyboardArrival"/>) and the help text a value box
/// carries (<see cref="VariableDetailViewModel.HelpTextFor"/>). Final review, findings R4 and R14:
/// focus coming back to a box from its own context menu or from another window kept replacing a
/// selection the user had made with the whole value (so the context menu's Copy copied the whole
/// value), and a single-line value of 512 characters or more, selected whole on arrival, was
/// spoken by NVDA only as "selected N characters". Now a return keeps a selection or caret
/// position of the user's own, and a value that long is not selected: the caret goes to its start,
/// NVDA reads its first line on screen, and its help text gives its length and says that Down reads
/// it line by line.
/// </summary>
public sealed class DetailValueArrivalTests
{
    private const VariableAttributes NvBsRtAt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess | VariableAttributes.TimeBasedAuthenticatedWriteAccess;

    [Fact]
    public void LongValueLength_IsNvdasLimitForSpeakingASelection()
    {
        // NVDA speech.py, speakSelectionMessage: "if len(text) < 512" speaks the text, otherwise the count.
        Assert.Equal(512, VariableDetailViewModel.LongValueLength);
        Assert.False(VariableDetailViewModel.IsLongValue(new string('v', 511), isMultiline: false));
        Assert.True(VariableDetailViewModel.IsLongValue(new string('v', 512), isMultiline: false));
        Assert.False(VariableDetailViewModel.IsLongValue(new string('v', 5000), isMultiline: true));
        Assert.False(VariableDetailViewModel.IsLongValue(null, isMultiline: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(102)]
    [InlineData(511)]
    public void ArrivalFromAnotherElement_SelectsAShortValueWhole(int length)
    {
        Assert.Equal((0, length), Arrive(length));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(798)]
    [InlineData(20000)]
    public void ArrivalFromAnotherElement_PutsTheCaretAtTheStartOfALongValue_InsteadOfSelectingIt(int length)
    {
        Assert.Equal((0, 0), Arrive(length));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(600)]
    public void AClick_LeavesTheBoxAlone(int length)
    {
        Assert.Null(VariableDetailViewModel.SelectionOnKeyboardArrival(false, length, clicked: true, returning: false, 0, 0));
        Assert.Null(VariableDetailViewModel.SelectionOnKeyboardArrival(false, length, clicked: true, returning: true, 0, length));
    }

    [Fact]
    public void AMultilineBox_IsNeverSelected()
    {
        Assert.Null(VariableDetailViewModel.SelectionOnKeyboardArrival(true, 80, clicked: false, returning: false, 0, 0));
        Assert.Null(VariableDetailViewModel.SelectionOnKeyboardArrival(true, 4000, clicked: false, returning: true, 0, 0));
    }

    [Theory]
    [InlineData(100, 10, 20)] // part of the value selected with Shift+arrows or a drag: the context menu's Copy copies that part
    [InlineData(100, 0, 99)]
    [InlineData(100, 1, 99)]
    [InlineData(100, 37, 0)] // the caret moved (Down read the second line): the reading position is kept
    [InlineData(100, 0, 0)]
    [InlineData(700, 150, 0)] // a long value read line by line, then Alt+Tab and back
    [InlineData(700, 0, 700)] // a long value the user selected whole with Ctrl+A
    public void AReturnFromTheContextMenuOrAnotherWindow_KeepsTheUsersOwnSelection(int length, int start, int selected)
    {
        Assert.Null(VariableDetailViewModel.SelectionOnKeyboardArrival(false, length, clicked: false, returning: true, start, selected));
    }

    [Fact]
    public void AReturnToABoxStillAsArrivalLeftIt_MakesTheSameSelectionAgain_WhichChangesNothing()
    {
        Assert.Equal((0, 100), VariableDetailViewModel.SelectionOnKeyboardArrival(false, 100, clicked: false, returning: true, 0, 100));
        Assert.Equal((0, 0), VariableDetailViewModel.SelectionOnKeyboardArrival(false, 700, clicked: false, returning: true, 0, 0));
    }

    [Fact]
    public void ArrivalFromAnotherElement_IgnoresTheSelectionTheBoxHadBefore()
    {
        // Shift+Tab back to a box from the next field is an arrival: the whole value again.
        Assert.Equal((0, 100), VariableDetailViewModel.SelectionOnKeyboardArrival(false, 100, clicked: false, returning: false, 10, 20));
    }

    [Fact]
    public void HelpText_IsTheOrdinaryOne_ExceptForALongSingleLineValue()
    {
        Assert.Equal(Strings.DetailFieldHelp, VariableDetailViewModel.HelpTextFor(null));
        Assert.Equal(Strings.DetailFieldHelp, VariableDetailViewModel.HelpTextFor(new DetailFieldViewModel("GUID", "8be4df61-93ca-11d2-aa0d-00e098032b8c")));
        Assert.Equal(Strings.DetailFieldHelp, VariableDetailViewModel.HelpTextFor(new DetailFieldViewModel("Long", new string('v', 511))));
        Assert.Equal(Strings.DetailFieldHelp, VariableDetailViewModel.HelpTextFor(new DetailFieldViewModel(VariableDetailViewModel.HexDumpLabel, new string('0', 4000), isMultiline: true)));

        // A2-6 (the tester's A2-4): neither help text says "Read-only"; the screen reader says the box's
        // read-only state itself, and the word in the help was heard a second time.
        string help = VariableDetailViewModel.HelpTextFor(new DetailFieldViewModel("Long", new string('v', 3012)));
        Assert.Equal("Long value, 3012 characters: Down arrow reads it line by line. Escape returns to the list at the same row.", help);
        Assert.Equal("Escape returns to the list at the same row.", Strings.DetailFieldHelp);
        Assert.EndsWith(" " + Strings.DetailFieldHelp, help, StringComparison.Ordinal);
        Assert.DoesNotContain("read-only", help, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("read only", help, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The realistic case the review names: firmware that stores one hash per signature list gives
    /// dbx a "Signature types" value with one entry per list, here 100 lists and 798 characters. It
    /// stays single-line and wrapped (a low-vision reader never scrolls sideways), but it is a long
    /// value: not selected on arrival, and its help text says how long it is. The short fields next
    /// to it are selected whole as before.
    /// </summary>
    [Fact]
    public void ADbxWithOneHashPerList_HasALongSignatureTypesValue_ReadLineByLine()
    {
        byte[] data = LoadOptionFixtures.Concat(Enumerable.Range(0, 100).Select(_ => SignatureListFixtures.Sha256List(1)).ToArray());
        FirmwareVariable dbx = new("dbx", KnownGuids.EfiImageSecurityDatabase, NvBsRtAt, data);
        DecodedVariable? decoded = DecoderRegistry.CreateDefault().TryDecode(dbx, new VariableSet(new[] { dbx }));
        Assert.NotNull(decoded);
        VariableDetailViewModel detail = new();
        detail.Show(new VariableItemViewModel(dbx, NamespaceCatalog.Describe(dbx.VendorGuid), decoded));

        DetailFieldViewModel types = detail.Sections.SelectMany(section => section.Fields).Single(field => field.Label == "Signature types");
        Assert.Equal(798, types.Value.Length);
        Assert.False(types.IsMultiline);
        Assert.True(VariableDetailViewModel.IsLongValue(types.Value, types.IsMultiline));
        Assert.Equal((0, 0), VariableDetailViewModel.SelectionOnKeyboardArrival(types.IsMultiline, types.Value.Length, clicked: false, returning: false, 0, 0));
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, Strings.LongDetailFieldHelpFormat, 798), VariableDetailViewModel.HelpTextFor(types));

        DetailFieldViewModel lists = detail.Sections.SelectMany(section => section.Fields).Single(field => field.Label == "Signature lists");
        Assert.Equal("100", lists.Value);
        Assert.Equal((0, 3), VariableDetailViewModel.SelectionOnKeyboardArrival(lists.IsMultiline, lists.Value.Length, clicked: false, returning: false, 0, 0));
        Assert.Equal(Strings.DetailFieldHelp, VariableDetailViewModel.HelpTextFor(lists));
    }

    /// <summary>
    /// The Writability line is short, so it is selected whole: its longest sentences, the one on a
    /// machine that is not the M3 test unit and a Secure Boot variable's with the firmware-setup
    /// instruction after it, stay under <see cref="VariableDetailViewModel.LongValueLength"/>.
    /// </summary>
    [Fact]
    public void TheWritabilityLine_IsShort_SoItIsSelectedWhole()
    {
        string secureBoot = CheckSentences.Format(WritabilitySentences.AuthenticatedWriteFormat, "TA") + " " + Strings.FirmwareSetupHint;
        foreach (string line in new[] { WritabilitySentences.WrongMachine, secureBoot })
        {
            Assert.True(line.Length < VariableDetailViewModel.LongValueLength);
            Assert.Equal((0, line.Length), Arrive(line.Length));
        }
    }

    private static (int Start, int Length)? Arrive(int length) =>
        VariableDetailViewModel.SelectionOnKeyboardArrival(isMultiline: false, length, clicked: false, returning: false, 0, 0);
}
