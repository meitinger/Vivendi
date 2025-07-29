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

using Microsoft.AspNetCore.Authorization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace AufBauWerk.Vivendi.Gateway;

internal static partial class Extensions
{
    public static RouteHandlerBuilder MapRemoteAppApi(this IEndpointRouteBuilder app)
    {
        return app.MapPost("/gateway/remoteapp", [Authorize] async (HttpContext context, RdpFile rdpFile) =>
        {
            // verify the request data
            if (context.User?.Identity?.Name is not string userName) { return Results.Challenge(); }
            Request? request = await context.Request.ReadFromJsonAsync(SerializerContext.Default.Request, context.RequestAborted);
            if (request is null) { return Results.BadRequest(); }
            if (!request.KnownPaths.Values.All(Path.IsPathFullyQualified)) { return Results.BadRequest(); }
            ExternalUser externalUser = new() { UserName = userName, KnownPaths = request.KnownPaths };
            byte[] message = JsonSerializer.SerializeToUtf8Bytes(externalUser, SerializerContext.Default.ExternalUser);
            if (64 * 1024 < message.Length) { return Results.BadRequest(); }

            // retrieve the RDP file and credential
            byte[] rdpFileContent = await rdpFile.GetContentAsync(context.RequestAborted);
            using NamedPipeClientStream stream = new(".", "VivendiRemoteApp", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);
            await stream.ConnectAsync(context.RequestAborted);
            await stream.WriteAsync(message, context.RequestAborted);
            Result result = await JsonSerializer.DeserializeAsync(stream, SerializerContext.Default.Result, context.RequestAborted) ?? throw new InvalidDataException();
            if (result.Error is not null) { return Results.InternalServerError(); }
            if (result.Credential is null) { return Results.Forbid(); }

            // done
            return Results.Json(Response.Build(result.Credential, rdpFileContent), SerializerContext.Default.Response);
        });
    }
}
