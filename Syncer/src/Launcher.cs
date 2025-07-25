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

using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace AufBauWerk.Vivendi.Syncer;

internal sealed class LauncherService(ILogger<LauncherService> logger, Configuration config, Database db) : PipeService("VivendiLauncher", PipeDirection.Out, logger)
{
    protected override IdentityReference ClientIdentity => config.GetSyncGroupIdentity();

    protected override async Task ExecuteAsync(Stream stream, string userName, CancellationToken stoppingToken)
    {
        if (await db.GetVivendiUserAsync(userName, stoppingToken) is VivendiUser user)
        {
            await JsonSerializer.SerializeAsync(stream, user, typeof(VivendiUser), SerializerContext.Default, stoppingToken);
        }
    }
}
