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

namespace AufBauWerk.Vivendi.Launcher;

internal static partial class Win32
{
    #region Native

    private delegate bool EnumThreadWindowProc(nint window, nint lParam);
    private unsafe delegate int GetStringProc(nint window, char* buffer, int length);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumThreadWindows(int threadId, EnumThreadWindowProc fn, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parentWindow, nint afterChildWindow, string? className, string? windowText);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetClassName(nint window, char* className, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string text, string? caption, uint type);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessage(nint window, uint message, nint wParam, string lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial nint SendMessage(nint window, uint message, nint wParam, char* lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    #endregion

    public class NativeWindow
    {
        public static unsafe NativeWindow FromHandle(nint handle)
        {
            ArgumentNullException.ThrowIfNull(handle.ToPointer());
            return new(handle);
        }

        private string? className = null;
        private readonly nint handle;
        private nint? style = null;
        private string? text = null;

        private NativeWindow(nint handle) => this.handle = handle;

        private T GetCached<T>(ref T? value, Func<nint, T> getter) => value ??= getter(handle);

        private bool HasStyle(int flags) => (GetCached(ref style, handle => GetWindowLongPtr(handle, -16/*GWL_STYLE*/)) & flags) == flags;

        public unsafe string ClassName => GetCached(ref className, handle => GetString(handle, GetClassName));

        public bool IsPassword => HasStyle(0x00000020/*ES_PASSWORD*/);

        public bool IsVisible => HasStyle(0x10000000/*WS_VISIBLE*/);

        public unsafe string Text
        {
            get => GetCached(ref text, handle => GetString(handle, (window, buffer, length) => (int)SendMessage(window, 0x000D/*WM_GETTEXT*/, length, buffer)));
            set => SendMessage(handle, 0x000C/*WM_SETTEXT*/, 0, text = value);
        }

        public void Click() => SendMessage(handle, 0x00F5/*BM_CLICK*/, 0, 0);

        public IEnumerable<NativeWindow> EnumerateChildren(string? className = null)
        {
            nint childHandle = 0;
            while ((childHandle = FindWindowEx(handle, childHandle, className, null)) is not 0)
            {
                yield return new(childHandle);
            }
        }
    }

    private unsafe static string GetString(nint handle, GetStringProc callback)
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

    public static IEnumerable<NativeWindow> EnumerateWindows(this ProcessThread thread)
    {
        List<nint> handles = [];
        EnumThreadWindows(thread.Id, (handle, _) => { handles.Add(handle); return true; }, 0);
        foreach (nint handle in handles)
        {
            yield return NativeWindow.FromHandle(handle);
        }
    }

    public static void ShowError(string message) => MessageBox(0, message, "Vivendi Launcher", 0x00010010/*MB_OK|MB_ICONERROR|MB_SETFOREGROUND*/);
}
