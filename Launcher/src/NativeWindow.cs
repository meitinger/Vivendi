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
using System.Runtime.InteropServices;

namespace AufBauWerk.Vivendi.Launcher;

public readonly partial struct NativeWindow(nint handle)
{
    public static readonly NativeWindow Invalid = new(0);

    #region Win32

    private delegate bool EnumWindowsProc(nint window, nint lParam);
    private unsafe delegate int GetStringProc(nint window, char* buffer, int length);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(nint windowParent, EnumWindowsProc enumFunc, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetClassName(nint window, char* className, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessage(nint window, int message, nint wParam, string lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial nint SendMessage(nint window, int message, nint wParam, char* lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, int message, nint wParam, nint lParam);

    #endregion

    private bool EnumerateTree(Action<NativeWindow, CancelEventArgs> callback, CancelEventArgs args)
    {
        callback(this, args);
        if (args.Cancel) { return false; }
        EnumChildWindows(handle, (childWindow, _) => new NativeWindow(childWindow).EnumerateTree(callback, args), 0);
        return !args.Cancel;
    }

    private bool HasStyle(int style) => (GetWindowLongPtr(handle, -16/*GWL_STYLE*/) & style) is not 0;

    private unsafe string GetStringInternal(GetStringProc callback)
    {
        char[] buffer = new char[200];
    Retry:
        int length;
        fixed (char* ptr = buffer) { length = callback(handle, ptr, buffer.Length); }
        if (length is 0) { return string.Empty; }
        if (length == buffer.Length - 1)
        {
            buffer = new char[buffer.Length + 100];
            goto Retry;
        }
        return new string(buffer, 0, length);
    }

    public unsafe string ClassName => GetStringInternal(GetClassName);

    public bool IsPassword => HasStyle(0x00000020/*ES_PASSWORD*/);

    public bool IsValid => handle is not 0;

    public bool IsVisible => HasStyle(0x10000000/*WS_VISIBLE*/);

    public unsafe string Text
    {
        get => GetStringInternal((window, buffer, length) => (int)SendMessage(window, 0x000D/*WM_GETTEXT*/, length, buffer));
        set => SendMessage(handle, 0x000C/*WM_SETTEXT*/, 0, value);
    }

    public void Click() => SendMessage(handle, 0x00F5/*BM_CLICK*/, 0, 0);

    public bool EnumerateAllChildren(Action<NativeWindow, CancelEventArgs> callback) => EnumerateTree(callback, new(cancel: false));
}
