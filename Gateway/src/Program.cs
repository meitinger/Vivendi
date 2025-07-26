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

using AufBauWerk.Vivendi.Gateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using System.IO.Pipes;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);

builder.Services
    .AddWindowsService(options => options.ServiceName = "VivendiGateway")
    .AddSingleton<RdpFile>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => // configure required AAD settings
    {
        string GetSetting(string name) => builder.Configuration[name] ?? throw new InvalidOperationException(new ArgumentNullException(name).Message);
        string tenantId = GetSetting("TenantId");
        string applicationId = GetSetting("ApplicationId");
        options.Authority = $"https://login.microsoftonline.com/{tenantId}";
        options.TokenValidationParameters = new()
        {
            ValidAudience = applicationId,
            ValidIssuers = [$"https://sts.windows.net/{tenantId}/", $"https://login.microsoftonline.com/{tenantId}/v2.0/"],
        };
    });
builder.Services
    .AddAuthorization();

WebApplication app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/remoteapp", [Authorize] async (HttpContext context, RdpFile rdpFile) =>
{
    if (context.User?.Identity?.Name is not string userName) { return Results.Forbid(); }
    RemoteAppRequest? request = (RemoteAppRequest?)await context.Request.ReadFromJsonAsync(typeof(RemoteAppRequest), SerializerContext.Default, context.RequestAborted);
    if (request is null) { return Results.BadRequest(); }
    if (request.KnownPaths.Values.Any(Path.IsPathFullyQualified)) { return Results.BadRequest(); }
    if (!request.KnownPaths.Keys.All(KnownFolders.IsAllowed)) { return Results.BadRequest(); }
    byte[] rdpFileContent = await rdpFile.GetContentAsync(context.RequestAborted);
    using NamedPipeClientStream stream = new(".", "VivendiRemoteApp", PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification, HandleInheritability.None);
    await stream.ConnectAsync(timeout: 5000, context.RequestAborted);
    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new ExternalUser() { UserName = userName }, typeof(ExternalUser), SerializerContext.Default), context.RequestAborted);
    WindowsUser? windowsUser = (WindowsUser?)await JsonSerializer.DeserializeAsync(stream, typeof(WindowsUser), SerializerContext.Default, context.RequestAborted);
    if (windowsUser is null) { return Results.Forbid(); }
    stream.Close();
    KnownFolders.RedirectForUser(windowsUser, request.KnownPaths);
    return Results.Json(RemoteAppResponse.Build(windowsUser, rdpFileContent), SerializerContext.Default);
});

app.Run();
