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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.SqlTypes;

namespace AufBauWerk.Vivendi.Gateway;

public class DatabaseController(Settings settings) : ControllerBase
{
    public Settings Settings => settings;

    public Task<IResult> WithDatabaseAsync(string queryString, Func<SqlDataReader, IResult> action) => WithDatabaseAsync(queryString, (reader, _) => Task.FromResult(action(reader)));

    public async Task<IResult> WithDatabaseAsync(string queryString, Func<SqlDataReader, CancellationToken, Task<IResult>> action)
    {
        string? userName = User?.Identity?.Name;
        if (userName is null) { return Results.Forbid(); }
        using SqlConnection connection = new(settings.ConnectionString);
        using SqlCommand command = new(queryString, connection);
        await connection.OpenAsync(HttpContext.RequestAborted);
        command.Parameters.AddWithValue("@UserName", userName);
        using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, HttpContext.RequestAborted);
        if (!await reader.ReadAsync(HttpContext.RequestAborted)) { return Results.Forbid(); }
        return await action(reader, HttpContext.RequestAborted);
    }
}

internal static class Extensions
{
    public static void BuildJwtOptions(this Settings settings, JwtBearerOptions options)
    {
        // configure required AAD settings
        options.Authority = $"https://login.microsoftonline.com/{settings.TenantId}";
        options.TokenValidationParameters = new()
        {
            ValidAudience = settings.ApplicationId,
            ValidIssuers =
            [
                    $"https://sts.windows.net/{settings.TenantId}/",
                    $"https://login.microsoftonline.com/{settings.TenantId}/v2.0/",
            ],
        };
    }

    public static T GetMandatory<T>(this SqlDataReader reader, string name) where T : notnull
    {
        object value = reader[name];
        if (value == DBNull.Value) { throw new SqlNullValueException(); }
        if (value is not T typed) { throw new SqlTypeException(); }
        return typed;
    }

    public static T? GetOptional<T>(this SqlDataReader reader, string name)
    {
        object value = reader[name];
        if (value == DBNull.Value) { return default; }
        if (value is not T typed) { throw new SqlTypeException(); }
        return typed;
    }
}
