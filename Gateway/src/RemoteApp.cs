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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AufBauWerk.Vivendi.Gateway;

public class RemoteAppRequest
{
    public Dictionary<Guid, string> KnownPaths { get; } = [];
}

public class RemoteAppResponse
{
    public static async Task<RemoteAppResponse> FromDatabase(SqlDataReader reader, CancellationToken cancellationToken) => new()
    {
        UserName = await reader.GetStringAsync(nameof(UserName), cancellationToken),
        Domain = await reader.GetStringAsync(nameof(Domain), cancellationToken),
        Password = await reader.GetStringAsync(nameof(Password), cancellationToken),
        RdpFileContent = await reader.GetStringAsync(nameof(RdpFileContent), cancellationToken),
    };

    public required string UserName { get; set; }
    public required string Domain { get; set; }
    public required string Password { get; set; }
    public required string RdpFileContent { get; set; }
}

[ApiController]
public sealed class RemoteAppController(Settings settings) : ControllerBase
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("/remoteapp")]
    public async Task<IResult> GetAsync(RemoteAppRequest request)
    {
        if (request.KnownPaths.Values.Any(string.IsNullOrWhiteSpace)) { return Results.BadRequest(); }
        if (await User.TranslateAsync(settings.ConnectionString, settings.RemoteAppQuery, RemoteAppResponse.FromDatabase, HttpContext.RequestAborted) is not { } response) { return Results.Forbid(); }
        Win32.DisconnectSessions(response.UserName, response.Domain, wait: true);
        Win32.RedirectKnownFolders(response.UserName, response.Domain, response.Password, request.KnownPaths);
        return Results.Json(response);
    }
}
