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
using Microsoft.Win32.SafeHandles;
using System.DirectoryServices.AccountManagement;
using System.Security.Principal;

namespace AufBauWerk.Vivendi.Gateway;

public class RemoteAppRequest
{
    public required Dictionary<Guid, string> KnownPaths { get; set; }
}

public class RemoteAppResponse
{
    public static RemoteAppResponse FromDatabase(SqlDataReader reader) => new()
    {
        UserName = reader.GetMandatory<string>(nameof(UserName)),
        Password = reader.GetMandatory<string>(nameof(Password)),
        RdpFileContent = reader.GetMandatory<string>(nameof(RdpFileContent)),
    };

    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required string RdpFileContent { get; set; }
}

[ApiController]
public sealed class RemoteAppController(Settings settings) : DatabaseController(settings)
{
    private static SecurityIdentifier UpsertUser(SqlDataReader reader, string userName, string password)
    {
        using PrincipalContext context = new(ContextType.Machine);
        using UserPrincipal principal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName) ?? new(context);
        principal.SamAccountName = userName;
        principal.Name = userName;
        principal.SetPassword(password);
        principal.Enabled = reader.GetMandatory<bool>(nameof(UserPrincipal.Enabled));
        principal.PasswordNotRequired = false;
        principal.DisplayName = reader.GetMandatory<string>(nameof(UserPrincipal.DisplayName));
        principal.Description = reader.GetOptional<string>(nameof(UserPrincipal.Description));
        principal.AccountExpirationDate = reader.GetOptional<DateTime>(nameof(UserPrincipal.AccountExpirationDate));
        principal.UserCannotChangePassword = reader.GetMandatory<bool>(nameof(UserPrincipal.UserCannotChangePassword));
        principal.PasswordNeverExpires = reader.GetMandatory<bool>(nameof(UserPrincipal.PasswordNeverExpires));
        principal.Save();
        if (principal.IsAccountLockedOut()) { principal.UnlockAccount(); }
        return principal.Sid;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("/remoteapp")]
    public Task<IResult> GetAsync(RemoteAppRequest request) => WithDatabaseAsync(Settings.RemoteAppQuery, reader =>
    {
        RemoteAppResponse response = RemoteAppResponse.FromDatabase(reader);
        SecurityIdentifier sid = UpsertUser(reader, response.UserName, response.Password);
        foreach (Win32.Session session in Win32.EnumerateLocalSessions())
        {
            if (session.State is Win32.ConnectionState.Active && session.Sid == sid)
            {
                session.Disconnect(wait: true);
            }
        }
        using SafeAccessTokenHandle user = Win32.LogonLocalUser(response.UserName, response.Password);
        foreach ((Guid knownFolderId, string path) in request.KnownPaths)
        {
            user.RedirectKnownFolder(knownFolderId, path);
        }
        return Results.Json(response);
    });
}
