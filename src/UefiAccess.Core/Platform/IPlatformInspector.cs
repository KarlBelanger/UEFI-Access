// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Platform;

/// <summary>
/// A read-only platform inspector (WMI, registry, or command line) that returns a result object
/// with an Errors list instead of throwing.
/// </summary>
/// <typeparam name="TResult">The result record produced by the inspector.</typeparam>
public interface IPlatformInspector<TResult>
{
    /// <summary>Runs the inspection. Never throws.</summary>
    /// <returns>The inspection result, including any errors encountered.</returns>
    TResult Inspect();
}
