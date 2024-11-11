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
using System.Security.Claims;

namespace AufBauWerk.Vivendi.Gateway;

public static class Authentication
{
    private const string InvalidDomainName = "Der Anmeldename gehört keiner gültigen Domain an.";
    private const string MissingUserName = "Der Anmeldename konnte nicht ermittelt werden.";
    private const string UnknownVivendiUser = "Kein Handzeichen stimmt mit dem Anmeldenamen überein.";

    private static bool CheckAndRemoveDomain(Settings settings, ref string userName)
    {
        int at = userName.IndexOf('@');
        if (at < 0) return false;
        string domain = userName[(at + 1)..];
        if (!settings.Authentication.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)) return false;
        userName = userName[..at];
        return true;
    }

    private static async Task<bool> VerifyUserAsync(Settings settings, string userName, CancellationToken cancellationToken)
    {
        using SqlConnection connection = new(settings.ConnectionString);
        using SqlCommand command = new(settings.Authentication.VerifyUserQuery, connection);
        command.Parameters.AddWithValue("@UserName", userName);
        await connection.OpenAsync(cancellationToken);
        return (bool)await command.ExecuteScalarAsync(cancellationToken);
    }

    public static Action<JwtBearerOptions> BuildJwtOptions(this Settings settings) => options =>
    {
        // configure required AAD settings
        options.Authority = $"https://login.microsoftonline.com/{settings.Authentication.TenantId}";
        options.TokenValidationParameters = new()
        {
            ValidAudience = settings.Authentication.ApplicationId,
            ValidIssuers =
            [
                $"https://sts.windows.net/{settings.Authentication.TenantId}/",
                $"https://login.microsoftonline.com/{settings.Authentication.TenantId}/v2.0/",
            ],
        };

        // ensure that the user is also registered in Vivendi
        options.Events = new()
        {
            OnTokenValidated = async context =>
            {
                string? userName = context.Principal?.Identity?.Name;
                if (userName is null)
                {
                    context.Fail(MissingUserName);
                    return;
                }
                if (!CheckAndRemoveDomain(settings, ref userName))
                {
                    context.Fail(InvalidDomainName);
                    return;
                }
                if (!await VerifyUserAsync(settings, userName, context.HttpContext.RequestAborted))
                {
                    context.Fail(UnknownVivendiUser);
                    return;
                }
            }
        };
    };

    public static string GetUserName(this ClaimsPrincipal? principal, Settings settings)
    {
        string userName = principal?.Identity?.Name ?? throw new UnauthorizedAccessException(MissingUserName);
        if (!CheckAndRemoveDomain(settings, ref userName)) throw new UnauthorizedAccessException(InvalidDomainName);
        return userName;
    }
}
