// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Native;

/// <summary>
/// Win32 error codes, NTSTATUS values, token and privilege constants used by the native layer.
/// Values are taken from the Windows SDK headers (winerror.h, ntstatus.h, winnt.h).
/// </summary>
internal static class NativeConstants
{
    /// <summary>NTSTATUS: the operation completed successfully.</summary>
    public const uint STATUS_SUCCESS = 0x00000000;

    /// <summary>NTSTATUS: the requested operation was unsuccessful (the firmware returned an error Windows did not recognise).</summary>
    public const uint STATUS_UNSUCCESSFUL = 0xC0000001;

    /// <summary>NTSTATUS: the requested operation is not implemented (seen on legacy BIOS boot).</summary>
    public const uint STATUS_NOT_IMPLEMENTED = 0xC0000002;

    /// <summary>NTSTATUS: an invalid parameter was passed to a system service.</summary>
    public const uint STATUS_INVALID_PARAMETER = 0xC000000D;

    /// <summary>NTSTATUS: not enough virtual memory or paging file quota is available.</summary>
    public const uint STATUS_NO_MEMORY = 0xC0000017;

    /// <summary>NTSTATUS: access denied (the Win32 side of a firmware security violation).</summary>
    public const uint STATUS_ACCESS_DENIED = 0xC0000022;

    /// <summary>NTSTATUS: the buffer is too small; the required length is returned in the length argument.</summary>
    public const uint STATUS_BUFFER_TOO_SMALL = 0xC0000023;

    /// <summary>NTSTATUS: a required privilege is not held by the client.</summary>
    public const uint STATUS_PRIVILEGE_NOT_HELD = 0xC0000061;

    /// <summary>NTSTATUS: insufficient system resources exist to complete the call (kernel memory, not NVRAM space).</summary>
    public const uint STATUS_INSUFFICIENT_RESOURCES = 0xC000009A;

    /// <summary>NTSTATUS: the media is write-protected (the expected NT side of a write-protected firmware variable).</summary>
    public const uint STATUS_MEDIA_WRITE_PROTECTED = 0xC00000A2;

    /// <summary>NTSTATUS: the request is not supported.</summary>
    public const uint STATUS_NOT_SUPPORTED = 0xC00000BB;

    /// <summary>NTSTATUS: the specified environment variable name was not found.</summary>
    public const uint STATUS_VARIABLE_NOT_FOUND = 0xC0000100;

    /// <summary>NTSTATUS: the I/O device reported an I/O error.</summary>
    public const uint STATUS_IO_DEVICE_ERROR = 0xC0000185;

    /// <summary>NTSTATUS: insufficient NVRAM resources exist to complete the requested service (the NVRAM-full code).</summary>
    public const uint STATUS_INSUFFICIENT_NVRAM_RESOURCES = 0xC0000454;

    /// <summary>Win32: incorrect function (legacy BIOS boot or no runtime variable services).</summary>
    public const int ERROR_INVALID_FUNCTION = 1;

    /// <summary>Win32: access is denied.</summary>
    public const int ERROR_ACCESS_DENIED = 5;

    /// <summary>Win32: not enough memory resources are available to process this command.</summary>
    public const int ERROR_NOT_ENOUGH_MEMORY = 8;

    /// <summary>Win32: not enough storage is available to complete this operation.</summary>
    public const int ERROR_OUTOFMEMORY = 14;

    /// <summary>Win32: the media is write protected (a read-only firmware variable).</summary>
    public const int ERROR_WRITE_PROTECT = 19;

    /// <summary>Win32: a device attached to the system is not functioning (the firmware returned an error Windows did not recognise).</summary>
    public const int ERROR_GEN_FAILURE = 31;

    /// <summary>Win32: the request is not supported.</summary>
    public const int ERROR_NOT_SUPPORTED = 50;

    /// <summary>Win32: the parameter is incorrect (attributes or size the firmware rejects).</summary>
    public const int ERROR_INVALID_PARAMETER = 87;

    /// <summary>Win32: the data area passed to a system call is too small.</summary>
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>Win32: the system could not find the environment option (firmware variable) that was entered.</summary>
    public const int ERROR_ENVVAR_NOT_FOUND = 203;

    /// <summary>Win32: invalid access to memory location (named by Microsoft as the firmware's answer to a GUID namespace it does not know).</summary>
    public const int ERROR_NOACCESS = 998;

    /// <summary>Win32: the request could not be performed because of an I/O device error.</summary>
    public const int ERROR_IO_DEVICE = 1117;

    /// <summary>Win32: not all privileges or groups referenced are assigned to the caller.</summary>
    public const int ERROR_NOT_ALL_ASSIGNED = 1300;

    /// <summary>Win32: a required privilege is not held by the client.</summary>
    public const int ERROR_PRIVILEGE_NOT_HELD = 1314;

    /// <summary>Win32: insufficient system resources exist to complete the requested service.</summary>
    public const int ERROR_NO_SYSTEM_RESOURCES = 1450;

    /// <summary>Win32: insufficient NVRAM resources exist to complete the requested service; a reboot might be required.</summary>
    public const int ERROR_NO_NVRAM_RESOURCES = 1470;

    /// <summary>Name of the privilege required by every firmware environment variable call.</summary>
    public const string SE_SYSTEM_ENVIRONMENT_NAME = "SeSystemEnvironmentPrivilege";

    /// <summary>LUID_AND_ATTRIBUTES attribute: the privilege is enabled.</summary>
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    /// <summary>Token access right: required to change the privileges specified in an access token.</summary>
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;

    /// <summary>Token access right: required to query an access token.</summary>
    public const uint TOKEN_QUERY = 0x0008;

    /// <summary>FIRMWARE_TYPE value reported for a legacy BIOS boot.</summary>
    public const uint FIRMWARE_TYPE_BIOS = 1;

    /// <summary>FIRMWARE_TYPE value reported for a UEFI boot.</summary>
    public const uint FIRMWARE_TYPE_UEFI = 2;

    /// <summary>
    /// Information class for NtEnumerateSystemEnvironmentValuesEx that returns names and values
    /// (VARIABLE_NAME_AND_VALUE entries). Class 1 would return names only.
    /// </summary>
    public const uint SYSTEM_ENVIRONMENT_VALUE_INFORMATION = 2;
}
