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
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AufBauWerk.Vivendi.Syncer;

internal static class Extensions
{
    private const int MessageTimeout = 1000;
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

    public static async Task<T?> ReceiveMessageAsync<T>(this PipeStream stream, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        using MemoryStream memoryStream = new();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(MessageTimeout);
        try
        {
            byte[] buffer = new byte[4096];
            do
            {
                int read = await stream.ReadAsync(buffer, cts.Token);
                memoryStream.Write(buffer, 0, read);
            } while (!stream.IsMessageComplete);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            throw new IOException(ex.Message, ex);
        }
        try
        {
            return JsonSerializer.Deserialize(memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length), jsonTypeInfo);
        }
        catch (JsonException ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    public static async Task SendMessageAsync<T>(this PipeStream stream, T value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo);
        if (64 * 1024 < message.Length)
        {
            throw new IOException($"Message too big ({message.Length} bytes).");
        }
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(MessageTimeout);
        try
        {
            await stream.WriteAsync(message, cts.Token);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            throw new IOException(ex.Message, ex);
        }
    }
}
