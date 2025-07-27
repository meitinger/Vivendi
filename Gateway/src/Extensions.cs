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

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace AufBauWerk.Vivendi.Gateway;

internal static partial class Extensions
{
    public static void BuildEntraJwtOptions(this IConfiguration configuration, JwtBearerOptions options)
    {
        string GetSetting(string name) => configuration[name] ?? throw new InvalidOperationException(new ArgumentNullException(name).Message);
        string tenantId = GetSetting("TenantId");
        string applicationId = GetSetting("ApplicationId");
        options.Authority = $"https://login.microsoftonline.com/{tenantId}";
        options.TokenValidationParameters = new()
        {
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidAudience = applicationId,
            ValidIssuers = [$"https://sts.windows.net/{tenantId}/", $"https://login.microsoftonline.com/{tenantId}/v2.0/"],
        };
    }

    public static RouteHandlerBuilder MapEndpoint(this WebApplication app)
    {
        return app.MapPost("/", [Authorize] async (HttpContext context, RdpFile rdpFile) =>
        {
            // verify the request data
            if (context.User?.Identity?.Name is not string userName) { return Results.Forbid(); }
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
            Credential? credential = await JsonSerializer.DeserializeAsync(stream, SerializerContext.Default.Credential, context.RequestAborted);
            if (credential is null) { return Results.Forbid(); }

            // done
            return Results.Json(Response.Build(credential, rdpFileContent), SerializerContext.Default.Response);
        });
    }
}
