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
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AufBauWerk.Vivendi.Launcher;

public record Credential(string UserName, string Password);

internal static partial class Helpers
{
    #region Win32

    private delegate bool EnumWindowsProc(nint window, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumThreadWindows(int threadId, EnumWindowsProc fn, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string text, string? caption, uint type);

    #endregion

    public static Credential DecryptCredential(byte[] key, byte[] arg)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(key.Length, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(arg.Length, 8);
        using Aes aes = Aes.Create();
        aes.Key = key;
        try
        {
            byte[] creds = aes.DecryptCbc(ciphertext: arg.AsSpan(8..), iv: arg.AsSpan(0..8));
            using MemoryStream stream = new(creds);
            using BinaryReader reader = new(stream, Encoding.UTF8);
            string userName = reader.ReadString();
            string password = reader.ReadString();
            if (reader.Read() is not -1) { throw new InvalidCredentialException(); }
            return new(userName, password);
        }
        catch (Exception e) when (e is IOException or CryptographicException)
        {
            throw new InvalidCredentialException(null, e);
        }
    }

    public static bool EnumerateWindows(this ProcessThread thread, Action<NativeWindow, CancelEventArgs> callback)
    {
        CancelEventArgs args = new();
        EnumThreadWindows(thread.Id, (window, _) =>
        {
            callback(new(window), args);
            return !args.Cancel;
        }, 0);
        return !args.Cancel;
    }

    public static byte[] GetMainModuleHash()
    {
        string path = Process.GetCurrentProcess()?.MainModule?.FileName ?? throw new FileNotFoundException();
        using FileStream moduleStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(moduleStream);
    }

    public static void ShowError(string message) => MessageBox(0, message, "Vivendi Launcher", 0x00010010/*MB_OK|MB_ICONERROR|MB_SETFOREGROUND*/);

    public static Process StartVivendi(out byte[] hash)
    {
        string path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Connext\Vivendi", "Path", null) as string ?? AppContext.BaseDirectory;
        using FileStream vivendiStream = new(Path.Combine(path, "Vivendi.exe"), FileMode.Open, FileAccess.Read, FileShare.Read);
        hash = SHA256.HashData(vivendiStream);
        return Process.Start(vivendiStream.Name);
    }

    public static bool TrySignOn(Process vivendi, Credential credential)
    {
        NativeWindow loginWindow, userNameWindow, passwordWindow, buttonWindow;

        foreach (ProcessThread thread in vivendi.Threads)
        {
            loginWindow = NativeWindow.Invalid;
            if (thread.EnumerateWindows((window, e) =>
            {
                if (window.Text is "Login")
                {
                    loginWindow = window;
                    e.Cancel = true;
                }
            })) { continue; }
            userNameWindow = NativeWindow.Invalid;
            passwordWindow = NativeWindow.Invalid;
            buttonWindow = NativeWindow.Invalid;
            if (loginWindow.EnumerateAllChildren((window, e) =>
            {
                if (!window.IsVisible) { return; }
                string[] classNameParts = window.ClassName.Split('.');
                if (classNameParts.Length < 2 || classNameParts[0] is not "WindowsForms10") { return; }
                switch (classNameParts[1])
                {
                    case "Button":
                        if (window.Text is "OK") { buttonWindow = window; }
                        break;
                    case "Edit":
                        if (window.IsPassword) { passwordWindow = window; }
                        else { userNameWindow = window; }
                        break;
                    default:
                        return;
                }
                e.Cancel = userNameWindow.IsValid && passwordWindow.IsValid && buttonWindow.IsValid;
            })) { continue; }
            userNameWindow.Text = credential.UserName;
            passwordWindow.Text = credential.Password;
            buttonWindow.Click();
            return true;
        }
        return false;
    }

    public static unsafe byte[] XorKeys(byte[] key1, byte[] key2)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(key1.Length, 32);
        ArgumentOutOfRangeException.ThrowIfNotEqual(key2.Length, 32);
        byte[] key = new byte[32];
        fixed (byte* key1Ptr = key1, key2Ptr = key2, keyPtr = key)
        {
            (Vector256.Load(key1Ptr) ^ Vector256.Load(key2Ptr)).Store(keyPtr);
        }
        return key;
    }
}
