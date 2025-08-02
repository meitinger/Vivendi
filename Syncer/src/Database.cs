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

using Microsoft.Data.SqlClient;
using System.Data;

namespace AufBauWerk.Vivendi.Syncer;

internal class Database(ILogger<Database> logger, Settings settings)
{
    public async Task<bool> IsVivendiUserAsync(string userName, CancellationToken cancellationToken) => await GetVivendiCredentialAsync(userName, cancellationToken) is not null;

    public async Task<Credential?> GetVivendiCredentialAsync(string userName, CancellationToken cancellationToken)
    {
        logger.LogTrace("Looking up Vivendi user for Windows User '{WindowsUser}'...", userName);
        using SqlConnection connection = new(settings.ConnectionString);
        using SqlCommand command = new(settings.QueryString, connection);
        await connection.OpenAsync(cancellationToken);
        command.Parameters.AddWithValue("@UserName", userName);
        using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            logger.LogTrace("Vivendi user for Windows user '{WindowsUser}' not found.", userName);
            return null;
        }
        Credential credential = new()
        {
            UserName = await reader.GetFieldValueAsync<string>("UserName", cancellationToken),
            Password = await reader.GetFieldValueAsync<string>("Password", cancellationToken),
        };
        logger.LogTrace("Found Vivendi user '{VivendiUser}' for Windows user '{WindowsUser}'.", credential.UserName, userName);
        return credential;
    }
}
