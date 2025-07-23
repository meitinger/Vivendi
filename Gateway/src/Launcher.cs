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

using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AufBauWerk.Vivendi.Gateway;

public class LauncherResponse()
{
    public static async Task<LauncherResponse> FromDatabase(SqlDataReader reader, CancellationToken cancellationToken) => new()
    {
        UserName = await reader.GetStringAsync(nameof(UserName), cancellationToken),
        Password = await reader.GetStringAsync(nameof(Password), cancellationToken),
    };

    public required string UserName { get; set; }
    public required string Password { get; set; }
}

[ApiController]
public sealed class LauncherController(Settings settings) : ControllerBase
{
    [Authorize(AuthenticationSchemes = NegotiateDefaults.AuthenticationScheme)]
    [HttpGet("/launcher")]
    public async Task<IResult> GetAsync() => await User.TranslateAsync(settings.ConnectionString, settings.VivendiUserQuery, LauncherResponse.FromDatabase, HttpContext.RequestAborted) is { } user ? Results.Json(user) : Results.Forbid();
}
