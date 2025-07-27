/*
 * AufBauWerk Erweiterungen für Vivendi
 * Copyright (C) 2024  Manuel Meitinger
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System.ComponentModel;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AufBauWerk.Vivendi.Syncer;

internal unsafe partial class Sessions(ILogger<Sessions> logger)
{
    #region Win32

    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const nint WTS_CURRENT_SERVER_HANDLE = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint tokenHandle, TOKEN_INFORMATION_CLASS tokenInformationClass, void* tokenInformation, uint tokenInformationLength, uint* returnLength);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSDisconnectSession(nint server, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSEnumerateSessionsW(nint server, uint reserved, uint version, WTS_SESSION_INFOW** sessionInfo, uint* count);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial void WTSFreeMemory(void* memory);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, nint* token);

    private enum TOKEN_INFORMATION_CLASS { TokenUser = 1 };

    private enum WTS_CONNECTSTATE_CLASS { Active = 0 }

#pragma warning disable CS0649
    private struct SID_AND_ATTRIBUTES
    {
        public byte* Sid;
        public uint Attributes;
    }

    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    private struct WTS_SESSION_INFOW
    {
        public uint SessionId;
        public char* WinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }
#pragma warning restore CS0649

    #endregion

    private static void ThrowExceptionForWin32(bool success)
    {
        if (!success) { throw new Win32Exception(); }
    }

    private SecurityIdentifier GetSidFromSession(uint sessionId)
    {
        nint token = 0;
        try
        {
            ThrowExceptionForWin32(WTSQueryUserToken(sessionId, &token));
            uint size = 100;
        Resize:
            byte[] buffer = new byte[size];
            fixed (byte* ptr = buffer)
            {
                if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenUser, ptr, size, &size))
                {
                    ThrowExceptionForWin32(Marshal.GetLastPInvokeError() is ERROR_INSUFFICIENT_BUFFER && buffer.Length < size);
                    goto Resize;
                }
                return new SecurityIdentifier(buffer, (int)(((TOKEN_USER*)ptr)->User.Sid - ptr));
            }
        }
        finally
        {
            if (token is not 0)
            {
                if (!CloseHandle(token))
                {
                    logger.LogWarning("Release user token for session #{SessionId} failed: {Message}", sessionId, Marshal.GetLastPInvokeErrorMessage());
                }
            }
        }
    }

    public void DisconnectForUser(UserPrincipal user, bool wait)
    {
        WTS_SESSION_INFOW* sessionInfos = null;
        uint count = 0;
        try
        {
            ThrowExceptionForWin32(WTSEnumerateSessionsW(WTS_CURRENT_SERVER_HANDLE, reserved: 0, version: 1, &sessionInfos, &count));
            for (uint i = 0; i < count; i++)
            {
                WTS_SESSION_INFOW sessionInfo = sessionInfos[i];
                if (sessionInfo.State is not WTS_CONNECTSTATE_CLASS.Active || GetSidFromSession(sessionInfo.SessionId) != user.Sid)
                {
                    continue;
                }
                ThrowExceptionForWin32(WTSDisconnectSession(WTS_CURRENT_SERVER_HANDLE, sessionInfo.SessionId, wait));
            }
        }
        finally
        {
            if (sessionInfos is not null)
            {
                WTSFreeMemory(sessionInfos);
            }
        }
    }
}
