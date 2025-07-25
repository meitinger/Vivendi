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
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

try
{
    const int timeout = 10000;
    using NamedPipeClientStream stream = new(".", "VivendiLauncher", PipeDirection.In, PipeOptions.None, TokenImpersonationLevel.Identification, HandleInheritability.None) { ReadTimeout = timeout };
    stream.Connect(timeout);
    Credential credential = (Credential?)JsonSerializer.Deserialize(stream, typeof(Credential), SerializerContext.Default) ?? throw new UnauthorizedAccessException();
    stream.Close();
    string? path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Connext\Vivendi", "Path", null) as string;
    using Process vivendi = Process.Start(Path.Combine(path ?? AppContext.BaseDirectory, "Vivendi.exe"));
    vivendi.SignIn(credential);
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Win32.ShowError(ex.Message);
    Environment.ExitCode = ex.HResult;
}
