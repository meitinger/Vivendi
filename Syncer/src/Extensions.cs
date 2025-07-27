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

using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.AccountManagement;
using System.IO.Pipes;
using System.Security.Principal;

namespace AufBauWerk.Vivendi.Syncer;

internal static class Extensions
{
    private static readonly SecurityIdentifier BuiltinAdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static void EnsureNotAdministrator(this UserPrincipal user)
    {
        if (user.IsMemberOf(user.Context, IdentityType.Sid, BuiltinAdministratorsSid.Value))
        {
            throw new PrincipalOperationException("Operation not allowed on Administrator accounts.");
        }
    }

    [DoesNotReturn]
    public static void LogExceptionAndExit(this ILogger logger, Exception ex)
    {
        logger.LogError(ex, "{Message}", ex.Message);
        Environment.Exit(1);
    }

    public static async Task<MemoryStream> ReadMessageAsync(this PipeStream stream, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        CancellationToken timedCancellationToken = cts.Token;
        byte[] buffer = new byte[1024];
        MemoryStream result = new();
        do
        {
            int read = await stream.ReadAsync(buffer, timedCancellationToken);
            result.Write(buffer, 0, read);
        } while (!stream.IsMessageComplete);
        result.Position = 0;
        return result;
    }
}
