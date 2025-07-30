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

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AufBauWerk.Vivendi.Syncer;

internal unsafe partial class KnownFolders(ILogger<KnownFolders> logger)
{
    #region Win32

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LoadUserProfileW(nint token, PROFILEINFOW* profileInfo);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUserW(string userName, string? domain, string? password, LOGON32_LOGON type, LOGON32_PROVIDER provider, nint* token);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHGetKnownFolderPath(in Guid id, KNOWN_FOLDER_FLAG flags, nint token, out string? path);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHSetKnownFolderPath(in Guid id, KNOWN_FOLDER_FLAG flags, nint token, string path);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnloadUserProfile(nint token, nint profile);

    private enum KNOWN_FOLDER_FLAG
    {
        DONT_VERIFY = 0x00004000,
        DEFAULT_PATH = 0x00000400,
        NOT_PARENT_RELATIVE = 0x00000200,
    }

    private enum LOGON32_LOGON { INTERACTIVE = 2 }

    private enum LOGON32_PROVIDER { DEFAULT = 0 }

    private enum PT { NOUI = 0x00000001 }

    private struct PROFILEINFOW
    {
        public int Size;
        public PT Flags;
        public char* UserName;
        public char* ProfilePath;
        public char* DefaultPath;
        public char* ServerName;
        public char* PolicyPath;
        public nint Profile;
    }

    #endregion

    private static readonly HashSet<Guid> AllowedIds =
    [
        new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), //Documents
        new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), //Desktop
        new("374DE290-123F-4565-9164-39C4925E467B"), //Downloads
        new("4BD8D571-6D19-48D3-BE97-422220080E43"), //Music
        new("33E28130-4E1E-4676-835A-98395C3BC3BB"), //Pictures
        new ("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), //Videos
    ];

    [GeneratedRegex(@"^([a-zA-Z]):")]
    private static partial Regex GetDriveLetterRegex();

    public void RedirectForUser(Credential user, IReadOnlyDictionary<Guid, string> redirects)
    {
        fixed (char* userName = user.UserName)
        {
            int hr;
            nint token = 0;
            PROFILEINFOW profileInfo = new()
            {
                Size = sizeof(PROFILEINFOW),
                Flags = PT.NOUI,
                UserName = userName,
            };
            try
            {
                logger.LogTrace("Redirecting known folders for user '{User}'...", user.UserName);
                if (!LogonUserW(user.UserName, ".", user.Password, LOGON32_LOGON.INTERACTIVE, LOGON32_PROVIDER.DEFAULT, &token))
                {
                    logger.LogWarning("Logon user '{User}' failed: {Message}", user.UserName, Marshal.GetLastPInvokeErrorMessage());
                    return;
                }
                if (!LoadUserProfileW(token, &profileInfo))
                {
                    logger.LogWarning("Load profile for user '{User}' failed: {Message}", user.UserName, Marshal.GetLastPInvokeErrorMessage());
                    return;
                }
                foreach (Guid knownFolderId in AllowedIds)
                {
                    if (redirects.TryGetValue(knownFolderId, out string? path))
                    {
                        path = GetDriveLetterRegex().Replace(path, @"\\tsclient\$1");
                    }
                    else
                    {
                        if ((hr = SHGetKnownFolderPath(knownFolderId, KNOWN_FOLDER_FLAG.DONT_VERIFY | KNOWN_FOLDER_FLAG.DEFAULT_PATH | KNOWN_FOLDER_FLAG.NOT_PARENT_RELATIVE, token, out path)) < 0 || path is null)
                        {
                            logger.LogWarning("Get known folder {KnownFolderId} for user '{User}' failed: {Message}", knownFolderId, user.UserName, Marshal.GetPInvokeErrorMessage(hr));
                            continue;
                        }
                    }
                    if ((hr = SHSetKnownFolderPath(knownFolderId, 0, token, path)) < 0)
                    {
                        logger.LogWarning("Set known folder {KnownFolderId} to path '{Path}' for user '{User}' failed: {Message}", knownFolderId, path, user.UserName, Marshal.GetPInvokeErrorMessage(hr));
                    }
                    else
                    {
                        logger.LogTrace("Redirect known folder {KnownFolderId} for user '{User}' to path '{Path}'.", knownFolderId, user.UserName, path);
                    }
                }
                logger.LogTrace("Redirected known folders for user '{User}'.", user.UserName);
            }
            finally
            {
                if (token is not 0)
                {
                    if (profileInfo.Profile is not 0)
                    {
                        if (!UnloadUserProfile(token, profileInfo.Profile))
                        {
                            logger.LogError("Unload profile for user '{User}' failed: {Message}", user.UserName, Marshal.GetLastPInvokeErrorMessage());
                        }
                    }
                    if (!CloseHandle(token))
                    {
                        logger.LogError("Logoff user '{User}' failed: {Message}", user.UserName, Marshal.GetLastPInvokeErrorMessage());
                    }
                }
            }
        }
    }
}
