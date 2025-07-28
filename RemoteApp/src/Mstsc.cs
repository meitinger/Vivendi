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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace AufBauWerk.Vivendi.RemoteApp;

internal static partial class Mstsc
{
    #region Win32

    private static readonly Guid CLSID_MsRdpSessionManagerSingleUseClass = new("6B7F33AC-D91D-4563-BF36-0ACCB24E66FB");

    [Flags]
    private enum CLSCTX
    {
        LOCAL_SERVER = 0x4,
    }

    [GeneratedComInterface]
    [Guid("A0B2DD9A-7F53-4E65-8547-851952EC8C96")]
    internal partial interface IMsRdpSessionManager
    {
        void StartRemoteApplication([MarshalUsing(typeof(Win32.BStrSafeArrayMarshaller))] string[] psaCreds, [MarshalUsing(typeof(Win32.BStrSafeArrayMarshaller))] string[] psaParams, int lFlags);
        int GetProcessId();
    }

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(in Guid classId, void* unknownOuter, CLSCTX context, in Guid interfaceId, out void* ptr);

    #endregion

    public static unsafe Process StartRemoteApp(string userName, string password, byte[] rdpFileContent)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(CLSID_MsRdpSessionManagerSingleUseClass, null, CLSCTX.LOCAL_SERVER, typeof(IMsRdpSessionManager).GUID, out void* managerPtr));
        try
        {
            IMsRdpSessionManager manager = ComInterfaceMarshaller<IMsRdpSessionManager>.ConvertToManaged(managerPtr) ?? throw new NullReferenceException();
            string rdpFileName = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(rdpFileName, rdpFileContent);
                manager.StartRemoteApplication([userName, password], [rdpFileName], 0);
            }
            finally
            {
                File.Delete(rdpFileName);
            }
            return Process.GetProcessById(manager.GetProcessId());
        }
        finally
        {
            ComInterfaceMarshaller<IMsRdpSessionManager>.Free(managerPtr);
        }
    }
}
