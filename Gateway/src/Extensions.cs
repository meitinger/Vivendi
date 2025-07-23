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
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Claims;

namespace AufBauWerk.Vivendi.Gateway;

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

    public static Task<string> GetStringAsync(this SqlDataReader reader, string name, CancellationToken cancellationToken) => reader.GetFieldValueAsync<string>(reader.GetOrdinal(name), cancellationToken);

    public static async Task<T?> TranslateAsync<T>(this ClaimsPrincipal? user, string connectionString, string queryString, Func<SqlDataReader, CancellationToken, Task<T>> resolver, CancellationToken cancellationToken) where T : class
    {
        string? userName = user?.Identity?.Name;
        if (userName is null) { return null; }
        using SqlConnection connection = new(connectionString);
        using SqlCommand command = new(queryString, connection);
        command.Parameters.AddWithValue("@UserName", userName);
        await connection.OpenAsync(cancellationToken);
        using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) { return null; }
        return await resolver(reader, cancellationToken);
    }
}
