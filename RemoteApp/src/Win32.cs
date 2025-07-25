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

using Microsoft.Identity.Client;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace AufBauWerk.Vivendi.RemoteApp;

public static partial class Win32
{
    [CustomMarshaller(typeof(string[]), MarshalMode.Default, typeof(BStrSafeArrayMarshaller))]
    internal static class BStrSafeArrayMarshaller
    {
        public static unsafe void* ConvertToUnmanaged(string[] managed)
        {
            if (managed is null)
            {
                return null;
            }
            SAFEARRAYBOUND bound = new()
            {
                cElements = (uint)managed.Length,
                lLbound = 0
            };
            SAFEARRAY* psa = SafeArrayCreate(VT.BSTR, 1, &bound);
            if (psa is null)
            {
                throw new OutOfMemoryException();
            }
            try
            {
                for (int i = 0; i < managed.Length; i++)
                {
                    nint bstr = Marshal.StringToBSTR(managed[i]);
                    try
                    {
                        Marshal.ThrowExceptionForHR(SafeArrayPutElement(psa, &i, bstr));
                    }
                    finally
                    {
                        Marshal.FreeBSTR(bstr);
                    }
                }
            }
            catch
            {
                _ = SafeArrayDestroy(psa);
                throw;
            }
            return psa;
        }

        public static unsafe void Free(void* unmanaged)
        {
            if (unmanaged is not null)
            {
                Marshal.ThrowExceptionForHR(SafeArrayDestroy((SAFEARRAY*)unmanaged));
            }
        }
    }

    #region Constants

    private static readonly Guid CLSID_MsRdpSessionManagerSingleUseClass = new("6B7F33AC-D91D-4563-BF36-0ACCB24E66FB");
    public const int ERROR_CANCELLED = 1223;

    #endregion

    #region Enums

    [Flags]
    private enum CLSCTX
    {
        LOCAL_SERVER = 0x4,
    }

    [Flags]
    private enum KF_FLAG
    {
        DONT_VERIFY = 0x00004000,
        NO_ALIAS = 0x00001000,
        NO_PACKAGE_REDIRECTION = 0x00010000,
    }

    [Flags]
    private enum MB
    {
        ICONERROR = 0x00000010,
        OK = 0x00000000,
        SETFOREGROUND = 0x00010000,
    }

    private enum VT : ushort
    {
        BSTR = 8,
    }

    #endregion

    #region Interfaces

    [GeneratedComInterface]
    [Guid("A0B2DD9A-7F53-4E65-8547-851952EC8C96")]
    internal partial interface IMsRdpSessionManager
    {
        void StartRemoteApplication([MarshalUsing(typeof(BStrSafeArrayMarshaller))] string[] psaCreds, [MarshalUsing(typeof(BStrSafeArrayMarshaller))] string[] psaParams, int lFlags);
        int GetProcessId();
    }

    #endregion

    #region LibraryImports

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(in Guid classId, void* unknownOuter, CLSCTX context, in Guid interfaceId, out void* ptr);

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int MessageBoxW(nint window, string text, string? caption, MB type);

    [LibraryImport("oleaut32.dll")]
    private static unsafe partial SAFEARRAY* SafeArrayCreate(VT type, uint dims, SAFEARRAYBOUND* bounds);

    [LibraryImport("oleaut32.dll")]
    private static unsafe partial int SafeArrayDestroy(SAFEARRAY* array);

    [LibraryImport("oleaut32.dll")]
    private static unsafe partial int SafeArrayPutElement(SAFEARRAY* array, int* indices, nint element);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHGetKnownFolderPath(in Guid id, KF_FLAG flags, nint token, out string? path);

    #endregion

    #region Structs
#pragma warning disable CS0649

    private struct SAFEARRAY
    {
        public ushort cDims;
        public ushort fFeatures;
        public uint cbElements;
        public uint cLocks;
        public nint pvData;
    }

    private struct SAFEARRAYBOUND
    {
        public uint cElements;
        public int lLbound;
    }

#pragma warning restore CS0649
    #endregion

    public static void ShowError(string message) => MessageBoxW(0, message, Settings.Instance.Title, MB.OK | MB.ICONERROR | MB.SETFOREGROUND);

    public static unsafe Process StartRemoteApp(string userName, string password, byte[] rdpFileContent)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(CLSID_MsRdpSessionManagerSingleUseClass, null, CLSCTX.LOCAL_SERVER, typeof(IMsRdpSessionManager).GUID, out void* managerPtr));
        try
        {
            IMsRdpSessionManager manager = ComInterfaceMarshaller<IMsRdpSessionManager>.ConvertToManaged(managerPtr) ?? throw new NullReferenceException();
            using (FileStream fileStream = new(Path.GetTempFileName(), FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.DeleteOnClose))
            {
                fileStream.Write(rdpFileContent);
                fileStream.Flush();
                manager.StartRemoteApplication([userName, password], [fileStream.Name], 0);
            }
            return Process.GetProcessById(manager.GetProcessId());
        }
        finally
        {
            ComInterfaceMarshaller<IMsRdpSessionManager>.Free(managerPtr);
        }
    }

    public static bool TryGetKnownFolderPath(Guid knownFolderId, [NotNullWhen(true)] out string? path) => 0 <= SHGetKnownFolderPath(knownFolderId, KF_FLAG.DONT_VERIFY | KF_FLAG.NO_ALIAS | KF_FLAG.NO_PACKAGE_REDIRECTION, 0, out path) && !string.IsNullOrEmpty(path);

    public static PublicClientApplicationBuilder WithDesktopAsParent(this PublicClientApplicationBuilder builder) => builder.WithParentActivityOrWindow(GetDesktopWindow);
}
