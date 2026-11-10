// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>Summary of a signature database variable (PK, KEK, db, dbx, dbt) built from EFI_SIGNATURE_LIST headers.</summary>
/// <param name="Size">Total size of the variable data in bytes.</param>
/// <param name="ListCount">Number of well-formed EFI_SIGNATURE_LIST structures.</param>
/// <param name="SignatureTypes">Signature type names (or GUIDs for unknown types) in list order.</param>
/// <param name="SignatureCount">Total number of signatures across every well-formed list.</param>
/// <param name="Error">Description of a malformed header, with the counts gathered before it, or null.</param>
public sealed record SignatureDbInfo(
    int Size,
    int ListCount,
    IReadOnlyList<string> SignatureTypes,
    int SignatureCount,
    string? Error);

/// <summary>
/// Reads EFI_SIGNATURE_LIST headers only (UEFI 2.10, section 32.4.1): SignatureType GUID (16 bytes),
/// SignatureListSize (UINT32), SignatureHeaderSize (UINT32), SignatureSize (UINT32). The number of
/// signatures in a list is (SignatureListSize - 28 - SignatureHeaderSize) / SignatureSize.
/// Inconsistent sizes stop the walk and are reported in Error together with what was counted. Never throws.
/// </summary>
public static class SignatureListReader
{
    /// <summary>Size of the fixed EFI_SIGNATURE_LIST header in bytes.</summary>
    public const int HeaderSize = 28;

    /// <summary>
    /// Smallest SignatureSize that can describe a list: every entry is an EFI_SIGNATURE_DATA, which
    /// begins with the 16-byte SignatureOwner GUID (UEFI 2.10, section 32.4.1). The entries
    /// themselves are never parsed; this only validates the header field.
    /// </summary>
    public const int MinimumSignatureSize = 16;

    /// <summary>EFI_CERT_X509_GUID.</summary>
    public static Guid EfiCertX509 { get; } = new("a5c059a1-94e4-4aa7-87b5-ab155c2bf072");

    /// <summary>EFI_CERT_SHA256_GUID.</summary>
    public static Guid EfiCertSha256 { get; } = new("c1c41626-504c-4092-aca9-41f936934328");

    private static readonly Dictionary<Guid, string> TypeNames = new()
    {
        [EfiCertX509] = "X509",
        [EfiCertSha256] = "SHA256",
        [new Guid("826ca512-cf10-4ac9-b187-be01496631bd")] = "SHA1",
        [new Guid("ff3e5307-9fd0-48c9-85f1-8ad56c701e01")] = "SHA384",
        [new Guid("093e0fae-a6c4-4f50-9f1b-d41e2b89c19a")] = "SHA512",
        [new Guid("3c5766e8-269c-4e34-aa14-ed776e85b3b6")] = "RSA2048",
        [new Guid("3bd2a492-96c0-4079-b420-fcf98ef103ed")] = "X509_SHA256",
    };

    /// <summary>Names a signature type GUID ("X509", "SHA256", ...) or returns the GUID in lowercase "D" format.</summary>
    /// <param name="signatureType">The EFI_SIGNATURE_LIST SignatureType.</param>
    /// <returns>The name or GUID text.</returns>
    public static string TypeName(Guid signatureType)
    {
        return TypeNames.TryGetValue(signatureType, out string? name) ? name : signatureType.ToString("D");
    }

    /// <summary>Summarises a signature database.</summary>
    /// <param name="data">The raw PK/KEK/db/dbx/dbt data.</param>
    /// <returns>The summary; Error is set for malformed input.</returns>
    public static SignatureDbInfo Read(ReadOnlySpan<byte> data)
    {
        List<string> types = new();
        int lists = 0;
        int signatures = 0;
        int offset = 0;
        string? error = null;

        while (offset < data.Length)
        {
            int remaining = data.Length - offset;
            if (remaining < HeaderSize)
            {
                error = Invariant($"signature list header at offset {offset} is truncated: {remaining} byte(s) remain, {HeaderSize} needed");
                break;
            }

            Guid type = new(data.Slice(offset, 16));
            uint listSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 16, 4));
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 20, 4));
            uint signatureSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 24, 4));
            string typeName = TypeName(type);
            string where = Invariant($"signature list {lists} ({typeName}) at offset {offset}");

            if (listSize < HeaderSize)
            {
                error = Invariant($"{where} has SignatureListSize {listSize}, less than the {HeaderSize}-byte header");
                break;
            }

            if (listSize > (uint)remaining)
            {
                error = Invariant($"{where} has SignatureListSize {listSize}, which exceeds the {remaining} remaining byte(s)");
                break;
            }

            long payload = (long)listSize - HeaderSize - headerSize;
            if (payload < 0)
            {
                error = Invariant($"{where} has SignatureHeaderSize {headerSize}, which exceeds the {listSize - HeaderSize} byte(s) after the header");
                break;
            }

            if (signatureSize == 0 && payload > 0)
            {
                error = Invariant($"{where} has SignatureSize 0 with {payload} byte(s) of signature data");
                break;
            }

            if (signatureSize != 0 && signatureSize < MinimumSignatureSize)
            {
                error = Invariant($"{where} has SignatureSize {signatureSize}, smaller than the {MinimumSignatureSize}-byte SignatureOwner GUID that starts every signature entry");
                break;
            }

            if (signatureSize != 0 && payload % signatureSize != 0)
            {
                error = Invariant($"{where} has {payload} byte(s) of signature data, which is not a multiple of SignatureSize {signatureSize}");
                break;
            }

            lists++;
            types.Add(typeName);
            signatures += signatureSize == 0 ? 0 : (int)(payload / signatureSize);
            offset += (int)listSize;
        }

        if (error is not null)
        {
            error = Invariant($"{error}; counted {Plural(lists, "list")} and {Plural(signatures, "signature")} before it");
        }

        return new SignatureDbInfo(data.Length, lists, types, signatures, error);
    }
}
