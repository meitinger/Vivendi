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
using System.Diagnostics;

try
{
    ArgumentOutOfRangeException.ThrowIfNotEqual(args.Length, 1);
    byte[] arg = Convert.FromBase64String(args[0]);
    ArgumentOutOfRangeException.ThrowIfLessThan(arg.Length, 8);
    DateTime encryptTime = new(BitConverter.ToInt64(arg));
    DateTime now = DateTime.UtcNow;
    if (1 < Math.Abs((now - encryptTime).TotalMinutes)) { throw new UnauthorizedAccessException(); }
    byte[] keyPart1 = Helpers.GetMainModuleHash();
    using Process vivendi = Helpers.StartVivendi(out byte[] keyPart2);
    byte[] key = Helpers.XorKeys(keyPart1, keyPart2);
    Credential credential = Helpers.DecryptCredential(key, arg);
    for (int numberOfTry = 0; numberOfTry < 600; numberOfTry++)
    {
        if (Helpers.TrySignOn(vivendi, credential)) { return; }
        Thread.Sleep(100);
    }
    throw new TimeoutException();
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Helpers.ShowError(ex.Message);
    Environment.ExitCode = ex.HResult;
}
