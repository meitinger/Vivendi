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

namespace AufBauWerk.Vivendi.Syncer;

internal sealed class RemoteAppService(ILogger<RemoteAppService> logger, Settings settings, Database database, KnownFolders knownFolders, Sessions sessions) : PipeService("VivendiRemoteApp", PipeDirection.InOut, logger)
{
    private static readonly SecurityIdentifier BuiltinRemoteDesktopUsersSid = new(WellKnownSidType.BuiltinRemoteDesktopUsersSid, null);

    private bool UpdateUserProperties(UserPrincipal user, ExternalUser externalUser, bool checkIfNeeded)
    {
        user.AccountExpirationDate = DateTime.Now + TimeSpan.FromTicks(TimeSpan.TicksPerMinute);
        if (checkIfNeeded)
        {
            bool unmodified =
                user.Enabled is true &&
                user.Description == settings.UserDescription &&
                user.DisplayName == externalUser.UserName &&
                user.PasswordNeverExpires is true &&
                user.PasswordNotRequired is false &&
                user.UserCannotChangePassword is true;
            if (unmodified) { return false; }
        }
        user.Enabled = true;
        user.Description = settings.UserDescription;
        user.DisplayName = externalUser.UserName;
        user.PasswordNeverExpires = true;
        user.PasswordNotRequired = false;
        user.UserCannotChangePassword = true;
        return true;
    }

    protected override IdentityReference ClientIdentity => settings.GatewayUserIdentity;

    protected override async Task<Result> ExecuteAsync(NamedPipeServerStream stream, CancellationToken stoppingToken)
    {
        ExternalUser externalUser;
        try
        {
            logger.LogTrace("Receiving request...");
            externalUser = await stream.ReceiveMessageAsync(SerializerContext.Default.ExternalUser, stoppingToken) ?? throw new InvalidDataException();
            logger.LogTrace("Received request (UserName={UserName}, KnownFoldersCount={KnownFoldersCount}).", externalUser.UserName, externalUser.KnownFolders.Count);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Receive external user from pipe failed: {Message}", ex.Message);
            return ex;
        }
        string userName = externalUser.UserName;
        int separator = userName.LastIndexOf('@');
        if (-1 < separator) { userName = userName[..separator]; }
        if (!await database.IsVivendiUserAsync(userName, stoppingToken)) { return null as Credential; }
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
                logger.LogTrace("Creating user '{User}'...", userName);
                user = new(context, samAccountName: userName, password: password, enabled: true);
                UpdateUserProperties(user, externalUser, checkIfNeeded: false);
                user.Save();
                try
                {
                    group.Members.Add(user);
                    group.Save();
                    rdpUsers.Members.Add(user);
                    rdpUsers.Save();
                }
                catch
                {
                    user.Delete();
                    throw;
                }
                logger.LogInformation("User '{User}' created.", userName);
            }
            else if (user.IsMemberOf(group))
            {
                logger.LogTrace("Updating existing user '{User}'...", userName);
                user.EnsureNotAdministrator();
                sessions.DisconnectForUser(user, wait: false);
                user.SetPassword(password);
                bool updated = UpdateUserProperties(user, externalUser, checkIfNeeded: true);
                user.Save();
                if (updated)
                {
                    logger.LogInformation("User '{User}' updated.", userName);
                }
                else
                {
                    logger.LogTrace("User '{User}' already up-to-date.", userName);
                }
            }
            else
            {
                throw new PrincipalOperationException("Not allowed for unsynced users.");
            }
            Credential credential = new() { UserName = userName, Password = password };
            knownFolders.RedirectForUser(credential, externalUser.KnownFolders);
            return credential;
        }
        catch (PrincipalException ex)
        {
            logger.LogWarning(ex, "Setup account for user '{User}' failed: {Message}", userName, ex.Message);
            return ex;
        }
        finally
        {
            user?.Dispose();
        }
    }
}
