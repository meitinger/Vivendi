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

namespace AufBauWerk.Vivendi.Gateway;

[ApiController]
public sealed class LauncherController(Settings settings) : DatabaseController(settings)
{
    [Authorize(AuthenticationSchemes = NegotiateDefaults.AuthenticationScheme)]
    [HttpGet("/launcher")]
    public Task<IResult> GetAsync() => WithDatabaseAsync(Settings.VivendiUserQuery, reader => Results.Json(new { UserName = reader.GetMandatory<string>("UserName"), Password = reader.GetMandatory<string>("Password") }));
}
