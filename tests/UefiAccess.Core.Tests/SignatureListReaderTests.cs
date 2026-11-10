// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Decoders;
using UefiAccess.Core.Tests.Fixtures;
using Xunit;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests;

/// <summary>Header-walking tests for <see cref="SignatureListReader"/>.</summary>
public sealed class SignatureListReaderTests
{
    [Fact]
    public void X509List_CountsOneListAndOneSignature()
    {
        byte[] data = SignatureListFixtures.X509List(64);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.Null(info.Error);
        Assert.Equal(data.Length, info.Size);
        Assert.Equal(28 + 16 + 64, info.Size);
        Assert.Equal(1, info.ListCount);
        Assert.Equal(1, info.SignatureCount);
        Assert.Equal(new[] { "X509" }, info.SignatureTypes);
    }

    [Fact]
    public void Sha256List_CountsThreeSignatures()
    {
        byte[] data = SignatureListFixtures.Sha256List(3);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.Null(info.Error);
        Assert.Equal(28 + (3 * 48), info.Size);
        Assert.Equal(1, info.ListCount);
        Assert.Equal(3, info.SignatureCount);
        Assert.Equal(new[] { "SHA256" }, info.SignatureTypes);
    }

    [Fact]
    public void TwoLists_AreCountedInOrder()
    {
        byte[] data = Concat(SignatureListFixtures.X509List(100), SignatureListFixtures.Sha256List(3));
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.Null(info.Error);
        Assert.Equal(2, info.ListCount);
        Assert.Equal(4, info.SignatureCount);
        Assert.Equal(new[] { "X509", "SHA256" }, info.SignatureTypes);
    }

    [Fact]
    public void ListWithSignatureHeader_SubtractsHeaderBeforeCounting()
    {
        byte[] data = SignatureListFixtures.List(SignatureListReader.EfiCertSha256, new byte[] { 1, 2, 3, 4 }, SignatureListFixtures.Signature(new byte[32]), SignatureListFixtures.Signature(new byte[32]));
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.Null(info.Error);
        Assert.Equal(2, info.SignatureCount);
    }

    [Fact]
    public void EmptyData_HasNoListsAndNoError()
    {
        SignatureDbInfo info = SignatureListReader.Read(ReadOnlySpan<byte>.Empty);

        Assert.Null(info.Error);
        Assert.Equal(0, info.Size);
        Assert.Equal(0, info.ListCount);
        Assert.Equal(0, info.SignatureCount);
        Assert.Empty(info.SignatureTypes);
    }

    [Fact]
    public void UnknownSignatureType_IsReportedAsGuid()
    {
        byte[] data = SignatureListFixtures.List(SignatureListFixtures.UnknownType, null, SignatureListFixtures.Signature(new byte[8]));
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.Null(info.Error);
        Assert.Equal(new[] { "0badc0de-0000-4000-8000-000000000001" }, info.SignatureTypes);
    }

    [Fact]
    public void TruncatedHeader_ReportsErrorWithCountsSoFar()
    {
        byte[] data = Concat(SignatureListFixtures.Sha256List(2), new byte[10]);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains("truncated", info.Error, StringComparison.Ordinal);
        Assert.Contains("counted 1 list and 2 signatures", info.Error, StringComparison.Ordinal);
        Assert.Equal(1, info.ListCount);
        Assert.Equal(2, info.SignatureCount);
    }

    [Fact]
    public void ListSizeBeyondBuffer_ReportsError()
    {
        byte[] data = SignatureListFixtures.RawList(SignatureListReader.EfiCertSha256, 1000, 0, 48, new byte[48]);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains("SignatureListSize 1000", info.Error, StringComparison.Ordinal);
        Assert.Contains("exceeds", info.Error, StringComparison.Ordinal);
        Assert.Equal(0, info.ListCount);
    }

    [Fact]
    public void ListSizeSmallerThanHeader_ReportsError()
    {
        byte[] data = SignatureListFixtures.RawList(SignatureListReader.EfiCertX509, 12, 0, 0, Array.Empty<byte>());
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains("SignatureListSize 12", info.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureHeaderSizeBeyondList_ReportsError()
    {
        byte[] data = SignatureListFixtures.RawList(SignatureListReader.EfiCertSha256, 28 + 48, 100, 48, new byte[48]);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains("SignatureHeaderSize 100", info.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureSizeZeroWithPayload_ReportsError()
    {
        byte[] data = SignatureListFixtures.RawList(SignatureListReader.EfiCertSha256, 28 + 48, 0, 0, new byte[48]);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains("SignatureSize 0", info.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PayloadNotMultipleOfSignatureSize_ReportsErrorAfterCountingEarlierLists()
    {
        byte[] bad = SignatureListFixtures.RawList(SignatureListReader.EfiCertSha256, 28 + 50, 0, 48, new byte[50]);
        byte[] data = Concat(SignatureListFixtures.X509List(30), bad);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains("not a multiple of SignatureSize 48", info.Error, StringComparison.Ordinal);
        Assert.Contains("signature list 1 (SHA256)", info.Error, StringComparison.Ordinal);
        Assert.Equal(1, info.ListCount);
        Assert.Equal(1, info.SignatureCount);
        Assert.Equal(new[] { "X509" }, info.SignatureTypes);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(8u)]
    [InlineData(15u)]
    public void SignatureSizeSmallerThanSignatureOwner_ReportsErrorInsteadOfInflatedCount(uint signatureSize)
    {
        byte[] data = SignatureListFixtures.RawList(SignatureListReader.EfiCertSha256, 28 + 48, 0, signatureSize, new byte[48]);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.NotNull(info.Error);
        Assert.Contains($"SignatureSize {signatureSize}", info.Error, StringComparison.Ordinal);
        Assert.Contains("SignatureOwner", info.Error, StringComparison.Ordinal);
        Assert.Equal(0, info.ListCount);
        Assert.Equal(0, info.SignatureCount);
        Assert.Empty(info.SignatureTypes);
    }

    [Fact]
    public void SignatureSizeOfExactlyTheSignatureOwner_IsAccepted()
    {
        byte[] data = SignatureListFixtures.RawList(SignatureListReader.EfiCertSha256, 28 + 48, 0, (uint)SignatureListReader.MinimumSignatureSize, new byte[48]);
        SignatureDbInfo info = SignatureListReader.Read(data);

        Assert.Null(info.Error);
        Assert.Equal(1, info.ListCount);
        Assert.Equal(3, info.SignatureCount);
    }

    [Fact]
    public void TypeName_NamesKnownGuidsAndFallsBackToGuidText()
    {
        Assert.Equal("X509", SignatureListReader.TypeName(SignatureListReader.EfiCertX509));
        Assert.Equal("SHA256", SignatureListReader.TypeName(SignatureListReader.EfiCertSha256));
        Assert.Equal("11111111-2222-3333-4444-555555555555", SignatureListReader.TypeName(SignatureListFixtures.Owner));
    }

    [Fact]
    public void Read_NeverThrows_ForEveryPrefixAndRandomBytes()
    {
        byte[] data = Concat(SignatureListFixtures.X509List(40), SignatureListFixtures.Sha256List(2));
        for (int length = 0; length <= data.Length; length++)
        {
            byte[] prefix = data[..length];
            Assert.Null(Record.Exception(() => SignatureListReader.Read(prefix)));
        }

        Random random = new(42);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            byte[] bytes = new byte[random.Next(0, 160)];
            random.NextBytes(bytes);
            Assert.Null(Record.Exception(() => SignatureListReader.Read(bytes)));
        }
    }
}
