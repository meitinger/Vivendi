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

namespace AufBauWerk.Vivendi.Launcher;

internal static partial class AutoLogon
{
    private const string ButtonClassNamePrefix = "WindowsForms10.Button";
    private const string EditClassNamePrefix = "WindowsForms10.Edit";
    private const string FormsClassNamePrexix = "WindowsForms10.Window.8";
    private const string LoginWindowText = "Login";
    private const string OKWindowText = "OK";

    private record Controls(Win32.NativeWindow UserNameEdit, Win32.NativeWindow PasswordEdit, Win32.NativeWindow OkButton);

    private static IEnumerable<Controls> FindControls(Win32.NativeWindow parentWindow, string classSuffix)
    {
        Win32.NativeWindow? userNameEdit = parentWindow
            .EnumerateChildren(className: EditClassNamePrefix + classSuffix)
            .FirstOrDefault(window => !window.IsPassword && window.IsVisible);
        Win32.NativeWindow? passwordEdit = parentWindow
            .EnumerateChildren(className: EditClassNamePrefix + classSuffix)
            .FirstOrDefault(window => window.IsPassword && window.IsVisible);
        Win32.NativeWindow? okButton = parentWindow
            .EnumerateChildren(className: ButtonClassNamePrefix + classSuffix)
            .FirstOrDefault(window => window.Text is OKWindowText && window.IsVisible);
        if (userNameEdit is not null && passwordEdit is not null && okButton is not null)
        {
            yield return new(userNameEdit, passwordEdit, okButton);
        }
    }

    public static async Task SignInAsync(this Process vivendi, Credential credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            Controls? controls = vivendi
                .Threads
                .Cast<ProcessThread>()
                .SelectMany(thread => thread
                    .EnumerateWindows()
                    .Where(window =>
                        window.Text is LoginWindowText &&
                        window.ClassName.StartsWith(FormsClassNamePrexix) &&
                        window.IsVisible)
                    .SelectMany(window => window
                        .EnumerateChildren(className: window.ClassName)
                        .Where(window => window.IsVisible)
                        .SelectMany(firstChild => FindControls(firstChild, window.ClassName[FormsClassNamePrexix.Length..])))
                )
                .FirstOrDefault();
            if (controls is not null)
            {
                controls.UserNameEdit.Text = credential.UserName;
                controls.PasswordEdit.Text = credential.Password;
                controls.OkButton.Click();
                break;
            }
            await Task.Delay(millisecondsDelay: 100, cancellationToken);
        }
    }
}
