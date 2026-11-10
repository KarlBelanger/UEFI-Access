// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Safety;

/// <summary>
/// Exactly what one firmware write passes to the native call: the variable's name and vendor
/// GUID, the attributes read back in the same transaction (always NV BS RT, 0x7) and the bytes the
/// pipeline encoded itself from Boot#### numbers. The constructor is internal and
/// <c>WriteTransaction</c> is its only caller: the CLI, the GUI and a test have no public way to
/// build a plan from bytes. A plan with attributes other than 0x7 is
/// refused at construction, so a write with other attributes cannot be described, let alone made
/// (section 2.4, item 3).
/// </summary>
public sealed class WritePlan
{
    /// <summary>The only attributes a plan may carry: NV BS RT, 0x7, the attributes of BootOrder and BootNext.</summary>
    public const uint RequiredAttributes = 0x7;

    private readonly byte[] _bytes;

    /// <summary>Creates a plan. Internal: only the transaction builds one.</summary>
    /// <param name="name">Variable name (case-sensitive).</param>
    /// <param name="guid">Vendor GUID.</param>
    /// <param name="attributes">The attributes to pass; must equal <see cref="RequiredAttributes"/>.</param>
    /// <param name="bytes">The bytes to write; empty for a deletion. Copied, so the caller's array cannot change the plan afterwards.</param>
    /// <exception cref="ArgumentException">The name is empty or the attributes are not NV BS RT.</exception>
    internal WritePlan(string name, Guid guid, uint attributes, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("a variable name is required", nameof(name));
        }

        if (attributes != RequiredAttributes)
        {
            throw new ArgumentException("a write plan carries attributes NV BS RT (0x7) and nothing else", nameof(attributes));
        }

        Name = name;
        Guid = guid;
        Attributes = attributes;
        _bytes = (byte[])bytes.Clone();
    }

    /// <summary>Variable name (case-sensitive).</summary>
    public string Name { get; }

    /// <summary>Vendor GUID.</summary>
    public Guid Guid { get; }

    /// <summary>The attributes passed with the value: always <see cref="RequiredAttributes"/>.</summary>
    public uint Attributes { get; }

    /// <summary>A copy of the bytes to write; empty for a deletion.</summary>
    public byte[] Bytes => (byte[])_bytes.Clone();

    /// <summary>The number of bytes to write; 0 deletes the variable.</summary>
    public uint Size => (uint)_bytes.Length;

    /// <summary>The variable's display key, "guid:name" as everywhere else.</summary>
    public string Key => FirmwareVariable.MakeKey(Guid, Name);
}
