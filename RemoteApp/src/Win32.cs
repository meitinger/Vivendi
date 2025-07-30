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
using System.Runtime.InteropServices.Marshalling;

namespace AufBauWerk.Vivendi.RemoteApp;

internal static partial class Win32
{
    [CustomMarshaller(typeof(string[]), MarshalMode.Default, typeof(BStrSafeArrayMarshaller))]
    internal static unsafe partial class BStrSafeArrayMarshaller
    {
        private enum VT : ushort
        {
            BSTR = 8,
        }

#pragma warning disable CS0649
        private struct SAFEARRAY
        {
            public ushort cDims;
            public ushort fFeatures;
            public uint cbElements;
            public uint cLocks;
            public nint pvData;
        }
#pragma warning restore CS0649

        private struct SAFEARRAYBOUND
        {
            public uint cElements;
            public int lLbound;
        }

        [LibraryImport("oleaut32.dll")]
        private static partial SAFEARRAY* SafeArrayCreate(VT type, uint dims, SAFEARRAYBOUND* bounds);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayDestroy(SAFEARRAY* array);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayPutElement(SAFEARRAY* array, int* indices, nint element);

        public static void* ConvertToUnmanaged(string[] managed)
        {
            if (managed is null) { return null; }
            SAFEARRAYBOUND bound = new()
            {
                cElements = (uint)managed.Length,
                lLbound = 0
            };
            SAFEARRAY* psa = SafeArrayCreate(VT.BSTR, 1, &bound);
            if (psa is null) { throw new OutOfMemoryException(); }
            try
            {
                for (int i = 0; i < managed.Length; i++)
                {
                    nint bstr = Marshal.StringToBSTR(managed[i]);
                    try { Marshal.ThrowExceptionForHR(SafeArrayPutElement(psa, &i, bstr)); }
                    finally { Marshal.FreeBSTR(bstr); }
                }
            }
            catch
            {
                _ = SafeArrayDestroy(psa);
                throw;
            }
            return psa;
        }

        public static void Free(void* unmanaged)
        {
            if (unmanaged is not null)
            {
                Marshal.ThrowExceptionForHR(SafeArrayDestroy((SAFEARRAY*)unmanaged));
            }
        }
    }

    public const int ERROR_CANCELLED = 1223;

    [Flags]
    private enum CLSCTX
    {
        INPROC_SERVER = 0x1,
        LOCAL_SERVER = 0x4,
    }

    [Flags]
    private enum MB
    {
        ICONERROR = 0x00000010,
        OK = 0x00000000,
        SETFOREGROUND = 0x00010000,
    }

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(in Guid classId, nint unknownOuter, CLSCTX context, in Guid interfaceId, out void* ptr);

    [LibraryImport("user32.dll")]
    public static partial nint GetDesktopWindow();

    [LibraryImport("shlwapi.dll")]
    private static unsafe partial int IUnknown_GetWindow(void* unknown, out nint window);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int MessageBoxW(nint window, string text, string? caption, MB type);

    public static unsafe T CreateInstance<T>(in Guid classId, bool inProc, out void* ptr)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(classId, 0, inProc ? CLSCTX.INPROC_SERVER : CLSCTX.LOCAL_SERVER, typeof(T).GUID, out ptr));
        try
        {
            return ComInterfaceMarshaller<T>.ConvertToManaged(ptr) ?? throw new NullReferenceException();
        }
        catch
        {
            if (ptr is not null)
            {
                ComInterfaceMarshaller<T>.Free(ptr);
                ptr = null;
            }
            throw;
        }
    }

    public static unsafe nint GetWindowFromIUnknown(void* obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        int hr = IUnknown_GetWindow(obj, out nint window);
        if (hr < 0)
        {
            Console.Error.WriteLine(Marshal.GetPInvokeErrorMessage(hr));
            return 0;
        }
        return window;
    }

    public static void ShowError(nint parentWindow, string message) => MessageBoxW(parentWindow, message, Settings.Instance.Title, MB.OK | MB.ICONERROR | MB.SETFOREGROUND);
}
