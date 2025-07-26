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

internal sealed class RemoteAppService(ILogger<LauncherService> logger, Settings settings) : PipeService("VivendiRemoteApp", PipeDirection.InOut, logger)
{
    private static readonly SecurityIdentifier BuiltinRemoteDesktopUsersSid = new(WellKnownSidType.BuiltinRemoteDesktopUsersSid, null);

    private void SetUserProperties(UserPrincipal user, ExternalUser externalUser)
    {
        user.AccountExpirationDate = DateTime.Now + TimeSpan.FromTicks(TimeSpan.TicksPerMinute);
        user.Description = settings.UserDescription;
        user.DisplayName = externalUser.UserName;
        user.PasswordNeverExpires = true;
        user.PasswordNotRequired = false;
        user.UserCannotChangePassword = true;
    }

    protected override IdentityReference ClientIdentity => settings.GatewayUserIdentity;

    protected override async Task ExecuteAsync(PipeStream stream, string _, CancellationToken stoppingToken)
    {
        using MemoryStream message = await stream.ReadMessageAsync(settings.GatewayTimeout, stoppingToken);
        if (await JsonSerializer.DeserializeAsync(message, typeof(ExternalUser), SerializerContext.Default, stoppingToken) is ExternalUser externalUser)
        {
            string userName = externalUser.UserName;
            int separator = userName.LastIndexOf('@');
            if (-1 < separator) { userName = userName[..separator]; }
            string password = new(Random.Shared.GetItems(settings.PasswordChars, settings.PasswordLength));
            using PrincipalContext context = new(ContextType.Machine);
            using GroupPrincipal group = settings.FindSyncGroup(context);
            using GroupPrincipal rdpUsers = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, BuiltinRemoteDesktopUsersSid.Value);
            UserPrincipal? user = null;
            try
            {
                user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
                if (user is null)
                {
                    user = new(context, samAccountName: userName, password, enabled: true);
                    SetUserProperties(user, externalUser);
                    user.Save();
                    try
                    {
                        group.Members.Add(user);
                        group.Save();
                        rdpUsers.Members.Add(user);
                        rdpUsers.Save();
                    }
                    catch (PrincipalException)
                    {
                        user.Delete();
                        throw;
                    }
                    logger.LogInformation("{User} created.", user.Name);
                }
                else if (user.IsMemberOf(group))
                {
                    user.EnsureNotAdministrator();
                    Sessions.DisconnectForUser(user, wait: true);
                    user.SetPassword(password);
                    user.Enabled = true;
                    SetUserProperties(user, externalUser);
                    user.Save();
                }
                else
                {
                    throw new PrincipalOperationException("Not allowed for unsynced users.");
                }
                await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new Credential() { UserName = userName, Password = password }, typeof(Credential), SerializerContext.Default), stoppingToken);
            }
            catch (PrincipalException ex)
            {
                logger.LogWarning(ex, "{User}: {Message}", userName, ex.Message);
            }
            finally
            {
                user?.Dispose();
            }
        }
    }
}
