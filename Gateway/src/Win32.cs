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

using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AufBauWerk.Vivendi.Gateway;

internal static class Win32
{
    private class SafeWtsBuffer : SafeBuffer
    {
        public SafeWtsBuffer() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle()
        {
            WTSFreeMemory(handle);
            return true;
        }
    }

    private const uint LOGON32_LOGON_INTERACTIVE = 2;
    private const uint LOGON32_PROVIDER_DEFAULT = 0;
    private const nint WTS_CURRENT_SERVER_HANDLE = 0;

    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LogonUser(string userName, string? domain, string? password, uint logonType, uint logonProvider, out SafeAccessTokenHandle token);

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(in Guid id, uint flags, SafeAccessTokenHandle token, string path);

    [DllImport("wtsapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSDisconnectSession(nint server, int sessionId, bool wait);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSEnumerateSessions(nint server, uint reserved, uint version, out SafeWtsBuffer sessionInfo, out int count);

    [DllImport("wtsapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern void WTSFreeMemory(nint handle);

    [DllImport("wtsapi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQueryUserToken(int sessionId, out SafeAccessTokenHandle token);

    public enum ConnectionState
    {
        Active,
        Connected,
        ConnectQuery,
        Shadow,
        Disconnected,
        Idle,
        Listen,
        Reset,
        Down,
        Init
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Session
    {
        private readonly SafeAccessTokenHandle GetUserToken()
        {
            if (!WTSQueryUserToken(SessionId, out var token))
            {
                throw new Win32Exception();
            }
            return token;
        }

        public int SessionId;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string WinStationName;
        public ConnectionState State;

        public readonly SecurityIdentifier Sid
        {
            get
            {
                using SafeAccessTokenHandle token = GetUserToken();
                using WindowsIdentity identity = new(token.DangerousGetHandle());
                GC.KeepAlive(token);
                return identity.User ?? throw new NotSupportedException();
            }
        }

        public readonly void Disconnect(bool wait)
        {
            if (!WTSDisconnectSession(WTS_CURRENT_SERVER_HANDLE, SessionId, wait))
            {
                throw new Win32Exception();
            }
        }
    }

    public static Session[] EnumerateLocalSessions()
    {
        if (!WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, reserved: 0, version: 1, out var sessionInfo, out var count))
        {
            throw new Win32Exception();
        }
        sessionInfo.Initialize((uint)count, (uint)Marshal.SizeOf<Session>());
        Session[] result = new Session[count];
        sessionInfo.ReadArray(0, result, 0, count);
        return result;
    }

    public static SafeAccessTokenHandle LogonLocalUser(string userName, string password)
    {
        if (!LogonUser(userName, domain: null, password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, out var handle))
        {
            throw new Win32Exception();
        }
        return handle;
    }

    public static void RedirectKnownFolder(this SafeAccessTokenHandle token, Guid knownFolderId, string path) => Marshal.ThrowExceptionForHR(SHSetKnownFolderPath(knownFolderId, flags: 0, token, path));
}
