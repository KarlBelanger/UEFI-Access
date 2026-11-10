// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Decoders;

/// <summary>
/// Holds the decoders and dispatches variables to the first one that can decode them.
/// Exceptions thrown by a decoder are converted into a <see cref="DecodedVariable"/> with an Error.
/// </summary>
public sealed class DecoderRegistry
{
    private readonly IVariableDecoder[] _decoders;

    /// <summary>Creates a registry over the given decoders, in priority order.</summary>
    /// <param name="decoders">The decoders; null entries are skipped.</param>
    public DecoderRegistry(IEnumerable<IVariableDecoder> decoders)
    {
        IEnumerable<IVariableDecoder> source = decoders ?? Enumerable.Empty<IVariableDecoder>();
        _decoders = source.Where(d => d is not null).ToArray();
    }

    /// <summary>The decoders in dispatch order.</summary>
    public IReadOnlyList<IVariableDecoder> Decoders => _decoders;

    /// <summary>Creates the registry with the M1 decoders (BootOrder, LoadOption, SecureBootState, OsIndications) and M4's NetworkStack decoder (task 4.6).</summary>
    /// <returns>The default registry.</returns>
    public static DecoderRegistry CreateDefault()
    {
        return new DecoderRegistry(new IVariableDecoder[]
        {
            new BootOrderDecoder(),
            new LoadOptionDecoder(),
            new SecureBootStateDecoder(),
            new OsIndicationsDecoder(),
            new NetworkStackDecoder(),
        });
    }

    /// <summary>Decodes a variable with the first decoder that accepts it.</summary>
    /// <param name="variable">The variable to decode.</param>
    /// <param name="lookup">Snapshot used to resolve references.</param>
    /// <returns>The decoded variable, or null when no decoder accepts it.</returns>
    public DecodedVariable? TryDecode(FirmwareVariable variable, IVariableLookup lookup)
    {
        if (variable is null)
        {
            return null;
        }

        foreach (IVariableDecoder decoder in _decoders)
        {
            bool canDecode;
            try
            {
                canDecode = decoder.CanDecode(variable);
            }
            catch (Exception)
            {
                canDecode = false;
            }

            if (!canDecode)
            {
                continue;
            }

            try
            {
                return decoder.Decode(variable, lookup);
            }
            catch (Exception ex)
            {
                return new DecodedVariable(
                    variable.Key,
                    decoder.GetType().Name,
                    "decoding failed: " + ex.Message,
                    Array.Empty<DecodedField>(),
                    null,
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        return null;
    }

    /// <summary>Decodes every variable in the snapshot that some decoder accepts, in key order.</summary>
    /// <param name="lookup">The snapshot to decode.</param>
    /// <returns>The decoded variables; empty when none could be decoded.</returns>
    public IReadOnlyList<DecodedVariable> DecodeAll(IVariableLookup lookup)
    {
        List<DecodedVariable> results = new();
        if (lookup is null)
        {
            return results;
        }

        foreach (FirmwareVariable variable in lookup.All)
        {
            DecodedVariable? decoded = TryDecode(variable, lookup);
            if (decoded is not null)
            {
                results.Add(decoded);
            }
        }

        return results;
    }
}
