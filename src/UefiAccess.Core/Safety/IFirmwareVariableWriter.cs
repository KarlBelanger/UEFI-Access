// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Safety;

/// <summary>
/// The one way a firmware variable is written: exactly one native call per invocation, no retry,
/// no loop. <see cref="FirmwareVariableWriter"/> is the production implementation and the single
/// call site of the write import; a test passes a scripted fake. The interface exposes this one
/// method and nothing else, and the method accepts only a <see cref="WritePlan"/>, which the
/// transaction alone can build.
/// </summary>
public interface IFirmwareVariableWriter
{
    /// <summary>
    /// Makes exactly one write call with the plan's name, GUID, attributes and bytes and reports
    /// what the call returned. Never called twice in one transaction, whatever the outcome.
    /// </summary>
    /// <param name="plan">The plan the transaction built and validated.</param>
    /// <returns>The call's outcome: success, or the Win32 error and the NTSTATUS observed beside it.</returns>
    WriteOutcome CommitOnce(WritePlan plan);
}
