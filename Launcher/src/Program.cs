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

using AufBauWerk.Vivendi.Launcher;
using Microsoft.Win32;
using System.Diagnostics;
using System.Net.Http.Json;

try
{
    const string registryPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Connext\Vivendi";
    string launcherUrl = Registry.GetValue(registryPath, "LauncherUrl", null) as string ?? throw new UnauthorizedAccessException();
    using HttpClient client = new(new HttpClientHandler() { UseDefaultCredentials = true });
    Credential credential = (Credential?)await client.GetFromJsonAsync(launcherUrl, typeof(Credential), SerializerContext.Default) ?? throw new UnauthorizedAccessException();
    string? path = Registry.GetValue(registryPath, "Path", null) as string;
    using Process vivendi = Process.Start(Path.Combine(path ?? AppContext.BaseDirectory, "Vivendi.exe"));
    vivendi.SignIn(credential);
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Win32.ShowError(ex.Message);
    Environment.ExitCode = ex.HResult;
}
