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

using System.DirectoryServices.AccountManagement;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace AufBauWerk.Vivendi.Syncer;

internal sealed class RemoteAppService(ILogger<LauncherService> logger, Configuration config) : PipeService("VivendiRemoteApp", PipeDirection.InOut, logger)
{
    private static readonly char[] PasswordChars = [.. Enumerable.Range(33, 94).Select(i => (char)(ushort)i)];
    private const int PasswordLength = 25;

    private void SetUserProperties(UserPrincipal user, ExternalUser externalUser)
    {
        user.AccountExpirationDate = DateTime.Now + TimeSpan.FromTicks(TimeSpan.TicksPerMinute);
        user.Description = config.UserDescription;
        user.DisplayName = externalUser.UserName;
        user.PasswordNeverExpires = true;
        user.PasswordNotRequired = false;
        user.UserCannotChangePassword = true;
    }

    protected override IdentityReference ClientIdentity => config.GetGatewayUserIdentity();

    protected override async Task ExecuteAsync(Stream stream, string _, CancellationToken stoppingToken)
    {
        if (await JsonSerializer.DeserializeAsync(stream, typeof(ExternalUser), SerializerContext.Default, stoppingToken) is ExternalUser externalUser)
        {
            string userName = externalUser.UserName;
            int separator = userName.LastIndexOf('@');
            if (-1 < separator) { userName = userName[..separator]; }
            string password = new(Random.Shared.GetItems(PasswordChars, PasswordLength));
            using PrincipalContext context = new(ContextType.Machine);
            using GroupPrincipal group = config.GetSyncGroupPrincipal(context);
            UserPrincipal? user = null;
            try
            {
                user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
                if (user is null)
                {
                    user = new(context, samAccountName: userName, password, enabled: true);
                    SetUserProperties(user, externalUser);
                    user.Save();
                    logger.LogInformation("{User} created.", user.Name);
                }
                else
                {
                    if (user.IsBuiltinAdministrator() || (!user.IsMemberOf(group) && user.Description != config.UserDescription))
                    {
                        throw new UnauthorizedAccessException();
                    }
                    Sessions.DisconnectForUser(user, wait: true);
                    user.SetPassword(password);
                    user.Enabled = true;
                    SetUserProperties(user, externalUser);
                    user.Save();
                }
                if (!user.IsMemberOf(group))
                {
                    group.Members.Add(user);
                    group.Save();
                    logger.LogInformation("{User} added to {Group}.", user.Name, group.Name);
                }
                await JsonSerializer.SerializeAsync(stream, new WindowsUser() { UserName = userName, Password = password }, typeof(WindowsUser), SerializerContext.Default, stoppingToken);
            }
            finally
            {
                user?.Dispose();
            }
        }
    }
}
