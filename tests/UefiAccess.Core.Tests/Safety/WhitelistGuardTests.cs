// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The whitelist guard, item 6, read by reflection: exactly two
/// entries, BootOrder (reorder only) and BootNext (one-shot), both under the EFI global GUID with
/// the attributes NV BS RT required, held in read-only fields of a class that exposes nothing else;
/// and no request type, in any product assembly, that carries bytes, so "binary blobs are never
/// writable" is a property of the types rather than a check that could be skipped. The lookup's
/// behaviour (ordinal name, exact GUID) is exercised by <see cref="WhitelistTests"/>.
/// </summary>
public sealed class WhitelistGuardTests
{
    [Fact]
    public void WhitelistHoldsExactlyBootOrderAndBootNext()
    {
        IReadOnlyList<WhitelistEntry> entries = Whitelist.Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "BootOrder", "BootNext" }, entries.Select(entry => entry.Name));
        Assert.All(entries, entry => Assert.Equal(KnownGuids.EfiGlobalVariable, entry.Guid));

        // 0x7 is NV BS RT, the only attributes a write plan can carry.
        Assert.All(entries, entry => Assert.Equal(0x7u, entry.RequiredAttributes));
        Assert.All(entries, entry => Assert.Equal(WritePlan.RequiredAttributes, entry.RequiredAttributes));
        Assert.Equal(WritableKind.ReorderOnly, entries[0].Kind);
        Assert.Equal(WritableKind.OneShot, entries[1].Kind);
        Assert.Same(Whitelist.BootOrder, entries[0]);
        Assert.Same(Whitelist.BootNext, entries[1]);

        // The kinds are the operations of section 2.1 and nothing for a third variable yet.
        Assert.Equal(new[] { "OneShot", "ReorderOnly" }, Enum.GetNames<WritableKind>().Order(StringComparer.Ordinal));

        // The class exposes the two entries, the list and the lookup, so a third entry cannot be
        // added as another member without this test naming it; the two entries are read-only fields.
        const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        string[] members = typeof(Whitelist)
            .GetMembers(PublicStatic)
            .Where(member => member is not MethodBase { IsSpecialName: true })
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "BootNext", "BootOrder", "Entries", "TryDescribe" }, members);
        Assert.All(typeof(Whitelist).GetFields(PublicStatic), field => Assert.True(field.IsInitOnly, "Whitelist." + field.Name + " must be read-only"));
        Assert.Null(typeof(Whitelist).GetProperty(nameof(Whitelist.Entries), PublicStatic)?.SetMethod);
        Assert.True(typeof(WhitelistEntry).IsSealed, "WhitelistEntry must be sealed");
    }

    [Fact]
    public void NoChangeRequestCarriesBytes()
    {
        Type[] requests = ProductAssemblies.Types()
            .Where(type => typeof(ChangeRequest).IsAssignableFrom(type) && type != typeof(ChangeRequest))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(typeof(ChangeRequest).IsAbstract, "ChangeRequest must be abstract");
        Assert.Equal(new[] { "BootNextRequest", "CheckRequest", "ClearBootNextRequest", "ReorderRequest", "RestoreRequest" }, requests.Select(type => type.Name));
        Assert.All(requests, type => Assert.True(type.IsSealed, type.Name + " must be sealed"));

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        List<string> carriers = new();
        foreach (Type type in requests.Append(typeof(ChangeRequest)))
        {
            foreach (ConstructorInfo constructor in type.GetConstructors(Declared))
            {
                carriers.AddRange(constructor.GetParameters().Where(parameter => CarriesBytes(parameter.ParameterType)).Select(parameter => $"{type.Name}({parameter.Name})"));
            }

            carriers.AddRange(type.GetProperties(Declared).Where(property => CarriesBytes(property.PropertyType)).Select(property => $"{type.Name}.{property.Name}"));
            carriers.AddRange(type.GetFields(Declared).Where(field => CarriesBytes(field.FieldType)).Select(field => $"{type.Name}.{field.Name}"));
        }

        Assert.True(carriers.Count == 0, "No request may carry bytes; the pipeline encodes every write itself from Boot#### numbers. Found: " + string.Join(", ", carriers));
    }

    /// <summary>True for a byte array, anything that enumerates bytes or wraps a byte buffer, and any generic type over byte (a sequence that does not enumerate, for example).</summary>
    private static bool CarriesBytes(Type type) =>
        type == typeof(byte[])
        || type == typeof(ReadOnlyMemory<byte>)
        || type == typeof(Memory<byte>)
        || typeof(IEnumerable<byte>).IsAssignableFrom(type)
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => argument == typeof(byte)));
}
