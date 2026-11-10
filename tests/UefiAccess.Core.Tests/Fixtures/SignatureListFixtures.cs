// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests.Fixtures;

/// <summary>Builders for EFI_SIGNATURE_LIST bytes (UEFI 2.10 section 32.4.1).</summary>
internal static class SignatureListFixtures
{
    /// <summary>Header size: SignatureType (16) + SignatureListSize (4) + SignatureHeaderSize (4) + SignatureSize (4).</summary>
    public const int HeaderSize = 28;

    /// <summary>An arbitrary signature owner GUID.</summary>
    public static readonly Guid Owner = new("11111111-2222-3333-4444-555555555555");

    /// <summary>A signature type GUID no reader knows.</summary>
    public static readonly Guid UnknownType = new("0badc0de-0000-4000-8000-000000000001");

    /// <summary>EFI_SIGNATURE_DATA: SignatureOwner (16) followed by the signature bytes.</summary>
    public static byte[] Signature(byte[] data) => Concat(Owner.ToByteArray(), data);

    /// <summary>A consistent list: sizes computed from the signatures (all of which must have the same length).</summary>
    public static byte[] List(Guid type, byte[]? header, params byte[][] signatures)
    {
        byte[] headerBytes = header ?? Array.Empty<byte>();
        byte[] body = Concat(signatures);
        uint signatureSize = signatures.Length == 0 ? 0u : (uint)signatures[0].Length;
        uint listSize = (uint)(HeaderSize + headerBytes.Length + body.Length);
        return RawList(type, listSize, (uint)headerBytes.Length, signatureSize, Concat(headerBytes, body));
    }

    /// <summary>A list with explicit header fields, for malformed-input tests.</summary>
    public static byte[] RawList(Guid type, uint listSize, uint headerSize, uint signatureSize, byte[] body)
    {
        return Concat(type.ToByteArray(), U32(listSize), U32(headerSize), U32(signatureSize), body);
    }

    /// <summary>One X509 certificate of the given length (a deterministic byte pattern).</summary>
    public static byte[] X509List(int certificateLength)
    {
        byte[] certificate = Enumerable.Range(0, certificateLength).Select(i => (byte)(0x30 + (i % 7))).ToArray();
        return List(SignatureListReader.EfiCertX509, null, Signature(certificate));
    }

    /// <summary>The given number of SHA-256 hashes (32 bytes each, deterministic patterns).</summary>
    public static byte[] Sha256List(int hashCount)
    {
        byte[][] signatures = Enumerable.Range(0, hashCount)
            .Select(n => Signature(Enumerable.Range(0, 32).Select(i => (byte)(n + i)).ToArray()))
            .ToArray();
        return List(SignatureListReader.EfiCertSha256, null, signatures);
    }
}
