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

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace AufBauWerk.Vivendi.Syncer;

internal abstract class PipeService(string name, PipeDirection direction, ILogger<PipeService> logger) : BackgroundService
{
    private static readonly SecurityIdentifier LocalSystemSid = new(WellKnownSidType.LocalSystemSid, null);

    protected abstract IdentityReference ClientIdentity { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            PipeSecurity security = new();
            PipeAccessRights clientRights = ((direction & PipeDirection.In) is not 0 ? PipeAccessRights.Write : 0) | ((direction & PipeDirection.Out) is not 0 ? PipeAccessRights.Read : 0);
            security.AddAccessRule(new(LocalSystemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new(ClientIdentity, clientRights, AccessControlType.Allow));
            using NamedPipeServerStream stream = NamedPipeServerStreamAcl.Create(name, direction, maxNumberOfServerInstances: 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, security);
            while (!stoppingToken.IsCancellationRequested)
            {
                await stream.WaitForConnectionAsync(stoppingToken);
                try
                {
                    string userName;
                    try
                    {
                        userName = stream.GetImpersonationUserName();
                    }
                    catch (IOException ex)
                    {
                        logger.LogWarning(ex, "Failed to identify pipe client: {Message}", ex.Message);
                        continue;
                    }
                    Message message;
                    try
                    {
                        message = await ExecuteAsync(stream, userName, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Request from user '{User}' failed: {Message}", userName, ex.Message);
                        message = Message.InternalServerError();
                    }
                    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(message, SerializerContext.Default.Message), stoppingToken);
                }
                finally
                {
                    stream.Disconnect();
                }
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == stoppingToken) { }
        catch (Exception ex) { logger.LogExceptionAndExit(ex); }
    }

    protected abstract Task<Message> ExecuteAsync(PipeStream stream, string userName, CancellationToken stoppingToken);
}
