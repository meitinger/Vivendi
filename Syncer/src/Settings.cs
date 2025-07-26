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

internal class Settings(IConfiguration configuration)
{
    private static readonly char[] DefaultPasswordChars = [.. Enumerable.Range(33, 94).Select(i => (char)(ushort)i)];

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

    private readonly IConfigurationSection section = configuration.GetRequiredSection("Syncer");

    private static T GetPrincipal<T>(Func<PrincipalContext, IdentityType, string, T> find, PrincipalContext context, string nameOrSid) => GetIdentity(nameOrSid) switch
    {
        SecurityIdentifier sid => find(context, IdentityType.Sid, sid.Value),
        NTAccount account => find(context, IdentityType.SamAccountName, account.Value),
        _ => throw new InvalidOperationException(),
    };

    private T Get<T>(T? defaultValue = default, [CallerMemberName] string name = "") => section.GetValue(name, defaultValue) ?? throw new InvalidOperationException(new ArgumentNullException(name).Message);

    public string ConnectionString => Get<string>();
    public TimeSpan CleanupInterval => Get(TimeSpan.FromHours(1));
    public TimeSpan GatewayTimeout => Get(TimeSpan.FromSeconds(5));
    private string GatewayUser => Get<string>();
    public IdentityReference GatewayUserIdentity => GetIdentity(GatewayUser);
    public char[] PasswordChars => Get(DefaultPasswordChars);
    public int PasswordLength => Get(25);
    public string QueryString => Get<string>();
    private string SyncGroup => Get<string>();
    public IdentityReference SyncGroupIdentity => GetIdentity(SyncGroup);
    public string UserDescription => Get("");

    public GroupPrincipal FindSyncGroup(PrincipalContext context) => GetPrincipal(GroupPrincipal.FindByIdentity, context, SyncGroup) ?? throw new NoMatchingPrincipalException();
}
