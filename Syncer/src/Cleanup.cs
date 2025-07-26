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

namespace AufBauWerk.Vivendi.Syncer;

internal sealed class CleanupService(ILogger<LauncherService> logger, Settings settings, Database database) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using PrincipalContext context = new(ContextType.Machine);
                using GroupPrincipal group = settings.FindSyncGroup(context);
                foreach (UserPrincipal user in group.GetMembers().OfType<UserPrincipal>())
                {
                    try
                    {
                        user.EnsureNotAdministrator();
                        if (await database.GetVivendiCredentialAsync(user.Name, stoppingToken) is null)
                        {
                            user.Delete();
                            logger.LogInformation("{User} deleted.", user.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "{User}: {Message}", user.Name, ex.Message);
                    }
                }
                await Task.Delay(settings.CleanupInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == stoppingToken) { }
        catch (Exception ex) { logger.LogExceptionAndExit(ex); }
    }
}
