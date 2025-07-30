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

namespace AufBauWerk.Vivendi.Syncer;

internal abstract class PipeService(string name, PipeDirection direction, ILogger<PipeService> logger) : BackgroundService
{
    private static readonly SecurityIdentifier LocalSystemSid = new(WellKnownSidType.LocalSystemSid, null);

    protected abstract IdentityReference ClientIdentity { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogTrace("Opening named pipe...");
            PipeSecurity security = new();
            PipeAccessRights clientRights = ((direction & PipeDirection.In) is not 0 ? PipeAccessRights.Write : 0) | ((direction & PipeDirection.Out) is not 0 ? PipeAccessRights.Read : 0);
            security.AddAccessRule(new(LocalSystemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new(ClientIdentity, clientRights, AccessControlType.Allow));
            using NamedPipeServerStream stream = NamedPipeServerStreamAcl.Create(name, direction, maxNumberOfServerInstances: 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, security);
            logger.LogInformation("Opened named pipe '{Pipe}' ({Direction}) for client '{Identity}'.", name, direction, ClientIdentity);
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogTrace("Waiting for connection...");
                await stream.WaitForConnectionAsync(stoppingToken);
                logger.LogTrace("Connection established.");
                try
                {
                    Result result;
                    try
                    {
                        result = await ExecuteAsync(stream, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Request failed.\nPipe={Pipe}\nDirection={Direction}\nIsConnected={IsConnected}\nCanRead={CanRead}\nCanWrite={CanWrite}\n\n{Message}", name, direction, stream.IsConnected, stream.CanRead, stream.CanWrite, ex.Message);
                        result = ex;
                    }
                    try
                    {
                        logger.LogTrace("Sending response (Error={Error}, UserName={UserName})...", result.Error, result.Credential?.UserName);
                        await stream.SendMessageAsync(result, SerializerContext.Default.Result, stoppingToken);
                        logger.LogTrace("Sent response.");
                    }
                    catch (IOException ex)
                    {
                        logger.LogWarning(ex, "Send result failed: {Message}", ex.Message);
                    }
                }
                finally
                {
                    logger.LogTrace("Disconnecting...");
                    stream.Disconnect();
                    logger.LogTrace("Disconnected.");
                }
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == stoppingToken) { }
        catch (Exception ex) { logger.LogExceptionAndExit(ex); }
    }

    protected abstract Task<Result> ExecuteAsync(NamedPipeServerStream stream, CancellationToken stoppingToken);
}
