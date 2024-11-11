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
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace AufBauWerk.Vivendi.Launcher;

public static class Win32
{
    private sealed class SafeMemory : SafeHandleZeroOrMinusOneIsInvalid
    {
        private static readonly nint Heap;

        static SafeMemory()
        {
            Heap = GetProcessHeap();
            if (Heap is 0) { throw new Win32Exception(); }
        }

        private SafeMemory() : base(ownsHandle: true) { }

        public static SafeMemory Alloc(int size)
        {
            SafeMemory safeMemory = HeapAlloc(Heap, 0x00000008/*HEAP_ZERO_MEMORY*/, size);
            if (safeMemory.IsInvalid)
            {
                safeMemory.SetHandleAsInvalid();
                throw new OutOfMemoryException();
            }
            return safeMemory;
        }

        protected override bool ReleaseHandle() => HeapFree(Heap, 0, handle);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate bool EnumWindowsProc(nint window, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TcpKeepAlive
    {
        public int OnOff;
        public int Time;
        public int Interval;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TcpRow2
    {
        public int State;
        public uint LocalAddr;
        public int LocalPort;
        public uint RemoteAddr;
        public int RemotePort;
        public int OwningProcessId;
        public int OffloadState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TcpTable2
    {
        public int NumEntries;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public TcpRow2[] Table;
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumChildWindows(nint parentWindow, EnumWindowsProc fn, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumThreadWindows(int threadId, EnumWindowsProc fn, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(nint window, StringBuilder className, int classNameMax);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint GetDesktopWindow();

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetProcessHeap();

    [DllImport("iphlpapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetTcpTable2(SafeMemory tcpTable, ref int size, bool order);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeMemory HeapAlloc(nint heap, int flags, int size);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HeapFree(nint heap, int flags, nint mem);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(nint window, string text, string caption, int type);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessage(nint window, int message, nint wParam, string lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessage(nint window, int message, nint wParam, StringBuilder lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    # region Extensions

    public static void Click(this nint window) => SendMessage(window, 0x00F5/*BM_CLICK*/, 0, 0);

    public static void EnableTcpKeepAlive(this Socket socket, TimeSpan idleTime, TimeSpan interval)
    {
        TcpKeepAlive keepAlive = new()
        {
            OnOff = 1,
            Time = (int)idleTime.TotalMilliseconds,
            Interval = (int)interval.TotalMilliseconds,
        };
        byte[] buffer = new byte[Marshal.SizeOf<TcpKeepAlive>()];
        GCHandle handle = GCHandle.Alloc(keepAlive, GCHandleType.Pinned);
        try
        {
            Marshal.Copy(handle.AddrOfPinnedObject(), buffer, 0, buffer.Length);
            socket.IOControl(IOControlCode.KeepAliveValues, buffer, null);
        }
        finally
        {
            handle.Free();
        }
    }

    public static bool EnumerateTree(this nint parentWindow, Action<nint, CancelEventArgs> callback) => EnumerateTreeInternal(parentWindow, callback, new(cancel: false));

    private static bool EnumerateTreeInternal(nint parentWindow, Action<nint, CancelEventArgs> callback, CancelEventArgs args)
    {
        callback(parentWindow, args);
        if (args.Cancel) { return false; }
        EnumChildWindows(parentWindow, (window, _) => EnumerateTreeInternal(window, callback, args), 0);
        return !args.Cancel;
    }

    public static bool EnumerateWindows(this ProcessThread thread, Action<nint, CancelEventArgs> callback)
    {
        CancelEventArgs args = new(cancel: false);
        EnumThreadWindows(thread.Id, (window, _) =>
        {
            callback(window, args);
            return !args.Cancel;
        }, 0);
        return !args.Cancel;
    }

    public static string? GetClassName(this nint window) => GetStringInternal(window, GetClassName);

    public static int GetRemoteProcessId(this Socket socket)
    {
        if (socket.LocalEndPoint is not IPEndPoint local || socket.RemoteEndPoint is not IPEndPoint remote)
        {
            return 0;
        }
        int size = 3000;
        SafeMemory memory = SafeMemory.Alloc(size);
        try
        {
        Retry:
            int result = GetTcpTable2(memory, ref size, order: true);
            switch (result)
            {
                case 0: break; // ERROR_SUCCESS
                case 232: return 0; // ERROR_NO_DATA
                case 122: // ERROR_INSUFFICIENT_BUFFER
                    memory.Dispose();
                    memory = SafeMemory.Alloc(size);
                    goto Retry;
                default: throw new NetworkInformationException(result);
            }
            if (size < Marshal.SizeOf<int>()) { throw new NetworkInformationException(122); }
            nint ptr = memory.DangerousGetHandle();
            nint offset = Marshal.OffsetOf<TcpTable2>(nameof(TcpTable2.Table));
            int numEntries = Marshal.ReadInt32(ptr);
            nint rowSize = Marshal.SizeOf<TcpRow2>();
            if (size < (offset + numEntries * rowSize)) { throw new NetworkInformationException(122); }
            ptr += offset;
            while (0 < numEntries--)
            {
                TcpRow2 entry = Marshal.PtrToStructure<TcpRow2>(ptr);
                if (IsSame(remote, entry.LocalAddr, entry.LocalPort) && IsSame(local, entry.RemoteAddr, entry.RemotePort))
                {
                    return entry.OwningProcessId;
                }
                ptr += rowSize;
            }
            return 0;
        }
        finally
        {
            memory.Dispose();
        }

        static bool IsSame(IPEndPoint endpoint, uint address, int port) => endpoint.Address.Equals(new IPAddress(address)) && endpoint.Port == NetworkToHostOrder(port);
        static int NetworkToHostOrder(int port) => (port & 0xFF) << 8 | ((port >> 8) & 0xFF);
    }

    private static string? GetStringInternal(nint window, Func<nint, StringBuilder, int, int> callback)
    {
        StringBuilder buffer = new(200);
    Retry:
        int length = callback(window, buffer, buffer.Capacity);
        if (length is 0) { return null; }
        if (length == buffer.Capacity - 1)
        {
            buffer.Capacity += 100;
            goto Retry;
        }
        return buffer.ToString(0, length);
    }

    public static string? GetText(this nint window) => GetStringInternal(window, (window, buffer, count) => (int)SendMessage(window, 0x000D /*WM_GETTEXT*/, count, buffer));

    private static bool HasStyleInternal(this nint window, int style) => (GetWindowLong(window, -16/*GWL_STYLE*/) & style) is not 0;

    public static bool IsPassword(this nint window) => window.HasStyleInternal(0x00000020/*ES_PASSWORD*/);

    public static bool IsVisible(this nint window) => window.HasStyleInternal(0x10000000/*WS_VISIBLE*/);

    public static bool SetForeground(this nint window) => SetForegroundWindow(window);

    public static bool SetText(this nint window, string text) => SendMessage(window, 0x000C/*WM_SETTEXT*/, 0, text) is not 0;

    public static bool ShowError(this Process process, string message) => ShowMessageInternal(process, message, 0x00000010/*MB_ICONERROR*/);

    private static bool ShowMessageInternal(Process process, string message, int type) => MessageBox(process.MainWindowHandle, message, process.MainWindowTitle, type) is not 0;

    public static bool ShowWarning(this Process process, string message) => ShowMessageInternal(process, message, 0x00000030/*MB_ICONWARNING*/);

    public static PublicClientApplicationBuilder WithDesktopAsParent(this PublicClientApplicationBuilder builder) => builder.WithParentActivityOrWindow(GetDesktopWindow);

    #endregion
}
