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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AufBauWerk.Vivendi.Gateway;

internal static unsafe partial class Win32
{
    private const nint WTS_CURRENT_SERVER_HANDLE = 0;
    private static readonly ReadOnlyCollection<Guid> KnownFolderIds = Array.AsReadOnly<Guid>
    ([
        new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), //Documents
        new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), //Desktop
        new("374DE290-123F-4565-9164-39C4925E467B"), //Downloads
        new("4BD8D571-6D19-48D3-BE97-422220080E43"), //Music
        new("33E28130-4E1E-4676-835A-98395C3BC3BB"), //Pictures
        new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), //Videos
    ]);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUserW(string userName, string? domain, string? password, LOGON32_LOGON type, LOGON32_PROVIDER provider, nint* token);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHGetKnownFolderPath(in Guid id, KNOWN_FOLDER_FLAG flags, nint token, out string? path);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHSetKnownFolderPath(in Guid id, KNOWN_FOLDER_FLAG flags, nint token, string path);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSDisconnectSession(nint server, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [LibraryImport("wtsapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSEnumerateSessionsExW(nint server, uint* level, uint filter, WTS_SESSION_INFO_1W** sessionInfo, uint* count);

    [LibraryImport("wtsapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSFreeMemoryExW(WTS_TYPE_CLASS wtsTypeClass, void* memory, uint numberOfEntries);

    private enum KNOWN_FOLDER_FLAG
    {
        DONT_VERIFY = 0x00004000,
        DEFAULT_PATH = 0x00000400,
        NOT_PARENT_RELATIVE = 0x00000200,
    }

    private enum LOGON32_LOGON { INTERACTIVE = 2 }

    private enum LOGON32_PROVIDER { DEFAULT = 0 }

    private enum WTS_CONNECTSTATE_CLASS { Active = 0 }

    private enum WTS_TYPE_CLASS { SessionInfoLevel1 = 2 }

    private struct WTS_SESSION_INFO_1W
    {
        public uint ExecEnvId;
        public WTS_CONNECTSTATE_CLASS State;
        public uint SessionId;
        public char* SessionName;
        public char* HostName;
        public char* UserName;
        public char* DomainName;
        public char* FarmName;
    }

    public static void DisconnectSessions(string userName, string domain, bool wait)
    {
        uint level = 1;
        WTS_SESSION_INFO_1W* sessionInfos = null;
        uint count = 0;
        try
        {
            if (!WTSEnumerateSessionsExW(WTS_CURRENT_SERVER_HANDLE, &level, 0, &sessionInfos, &count))
            {
                throw new Win32Exception();
            }
            for (uint i = 0; i < count; i++)
            {
                WTS_SESSION_INFO_1W sessionInfo = sessionInfos[i];
                if
                (
                    sessionInfo.State is WTS_CONNECTSTATE_CLASS.Active &&
                    sessionInfo.UserName is not null &&
                    sessionInfo.DomainName is not null &&
                    userName.Equals(new(sessionInfo.UserName), StringComparison.OrdinalIgnoreCase) &&
                    domain.Equals(new(sessionInfo.DomainName), StringComparison.OrdinalIgnoreCase) &&
                    !WTSDisconnectSession(WTS_CURRENT_SERVER_HANDLE, sessionInfo.SessionId, wait)
                )
                {
                    throw new Win32Exception();
                }
            }
        }
        finally
        {
            if (sessionInfos is not null)
            {
                WTSFreeMemoryExW(WTS_TYPE_CLASS.SessionInfoLevel1, sessionInfos, count);
            }
        }
    }

    public static void RedirectKnownFolders(string userName, string domain, string password, IReadOnlyDictionary<Guid, string> redirects)
    {
        nint token = 0;
        try
        {
            if (!LogonUserW(userName, domain, password, LOGON32_LOGON.INTERACTIVE, LOGON32_PROVIDER.DEFAULT, &token))
            {
                throw new Win32Exception();
            }
            foreach (Guid knownFolderId in KnownFolderIds)
            {
                if (!redirects.TryGetValue(knownFolderId, out string? path))
                {
                    Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(knownFolderId, KNOWN_FOLDER_FLAG.DONT_VERIFY | KNOWN_FOLDER_FLAG.DEFAULT_PATH | KNOWN_FOLDER_FLAG.NOT_PARENT_RELATIVE, token, out path));
                    if (path is null) { continue; }
                }
                Marshal.ThrowExceptionForHR(SHSetKnownFolderPath(knownFolderId, 0, token, path));
            }
        }
        finally
        {
            if (token is not 0)
            {
                CloseHandle(token);
            }
        }
    }
}
