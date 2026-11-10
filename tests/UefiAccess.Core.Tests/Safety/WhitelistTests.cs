// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;
using static UefiAccess.Core.Tests.Safety.SafetyFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// Task 3.1: the whitelist holds exactly BootOrder (reorder only) and BootNext (one shot) under
/// the EFI global GUID with attributes 0x7, and nothing a caller can say puts a third variable on
/// it. The guard test item 6 reads the same facts by reflection.
/// </summary>
public sealed class WhitelistTests
{
    [Fact]
    public void Entries_AreExactlyBootOrderAndBootNext()
    {
        Assert.Equal(2, Whitelist.Entries.Count);
        Assert.Same(Whitelist.BootOrder, Whitelist.Entries[0]);
        Assert.Same(Whitelist.BootNext, Whitelist.Entries[1]);
        Assert.Equal(new[] { "BootOrder", "BootNext" }, Whitelist.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Entries_AreUnderTheEfiGlobalGuidWithAttributes7()
    {
        Assert.All(Whitelist.Entries, entry =>
        {
            Assert.Equal(KnownGuids.EfiGlobalVariable, entry.Guid);
            Assert.Equal(0x7u, entry.RequiredAttributes);
            Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:" + entry.Name, entry.Key);
        });
    }

    [Fact]
    public void Entries_CannotBeReplacedThroughACast()
    {
        Assert.IsNotType<WhitelistEntry[]>(Whitelist.Entries);
        IList<WhitelistEntry> list = Assert.IsAssignableFrom<IList<WhitelistEntry>>(Whitelist.Entries);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = Whitelist.BootNext);
        Assert.Same(Whitelist.BootOrder, Whitelist.Entries[0]);
    }

    [Fact]
    public void Entries_CarryTheKindsOfSection21()
    {
        Assert.Equal(WritableKind.ReorderOnly, Whitelist.BootOrder.Kind);
        Assert.Equal(WritableKind.OneShot, Whitelist.BootNext.Kind);
    }

    [Fact]
    public void TryDescribe_FindsTheTwoByOrdinalNameAndExactGuid()
    {
        Assert.True(Whitelist.TryDescribe("BootOrder", Global, out WhitelistEntry? order));
        Assert.Same(Whitelist.BootOrder, order);
        Assert.True(Whitelist.TryDescribe("BootNext", Global, out WhitelistEntry? next));
        Assert.Same(Whitelist.BootNext, next);
    }

    [Theory]
    [InlineData("bootorder")]
    [InlineData("BOOTNEXT")]
    [InlineData("BootOrder ")]
    [InlineData("Boot0002")]
    [InlineData("BootCurrent")]
    [InlineData("Timeout")]
    [InlineData("PK")]
    [InlineData("OsIndications")]
    [InlineData("")]
    public void TryDescribe_RefusesEveryOtherNameUnderTheGlobalGuid(string name)
    {
        Assert.False(Whitelist.TryDescribe(name, Global, out WhitelistEntry? entry));
        Assert.Null(entry);
    }

    [Fact]
    public void TryDescribe_RefusesTheRightNameUnderAnotherGuid()
    {
        Assert.False(Whitelist.TryDescribe("BootOrder", KnownGuids.EfiImageSecurityDatabase, out _));
        Assert.False(Whitelist.TryDescribe("BootNext", DellBootGuid, out _));
        Assert.False(Whitelist.TryDescribe("BootFFFB", DellBootGuid, out _));
    }

    [Fact]
    public void TryDescribe_NullName_IsFalseWithoutThrowing()
    {
        Assert.False(Whitelist.TryDescribe(null!, Global, out WhitelistEntry? entry));
        Assert.Null(entry);
    }
}
