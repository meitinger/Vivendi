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
using System.Runtime.CompilerServices;
using System.Security.Principal;

namespace AufBauWerk.Vivendi.Syncer;

internal class Configuration(IConfiguration configuration)
{
    private static IdentityReference GetIdentity(string nameOrSid)
    {
        try
        {
            return new SecurityIdentifier(nameOrSid);
        }
        catch (ArgumentException)
        {
            return new NTAccount(nameOrSid);
        }
    }

    private static T GetPrincipal<T>(Func<PrincipalContext, IdentityType, string, T> find, PrincipalContext context, string nameOrSid) => GetIdentity(nameOrSid) switch
    {
        SecurityIdentifier sid => find(context, IdentityType.Sid, sid.Value),
        NTAccount account => find(context, IdentityType.SamAccountName, account.Value),
        _ => throw new InvalidOperationException(),
    };

    private string Get([CallerMemberName] string name = "") => configuration[name] is string value && 0 < value.Length ? value : throw new ArgumentNullException(name);

    public string ConnectionString => Get();
    private string GatewayUser => Get();
    public string QueryString => Get();
    private string SyncGroup => Get();
    public string UserDescription => Get();

    public IdentityReference GetGatewayUserIdentity() => GetIdentity(GatewayUser);
    public GroupPrincipal GetSyncGroupPrincipal(PrincipalContext context) => GetPrincipal(GroupPrincipal.FindByIdentity, context, SyncGroup);
    public IdentityReference GetSyncGroupIdentity() => GetIdentity(SyncGroup);
}
