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

using System.Net;

namespace AufBauWerk.Vivendi.Gateway;

public class Settings
{
    public string ConnectionString { get; set; } = "";
    public ApiSettings Api { get; } = new();
    public AuthenticationSettings Authentication { get; } = new();
    public CertificateAuthoritySettings CertificateAuthority { get; } = new();
    public OpenOlatSettings OpenOlat { get; } = new();
}

public class ApiSettings
{
    public string SharedSecret { get; set; } = "";
    public string PasswordHashTemplate { get; set; } = "";
}

public class AuthenticationSettings
{

    public string ApplicationId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public IReadOnlyList<string> AllowedDomains { get; set; } = [];
    public string VerifyUserQuery { get; set; } = "";
}

public class CertificateAuthoritySettings
{
    public string Certificate { get; set; } = "";
    public string PrivateKey { get; set; } = "";
}

public class OpenOlatSettings
{
    public Uri? RestApiEndpoint { get; set; }
    public NetworkCredential Credentials { get; } = new();
    public string AuthProvider { get; set; } = "";
    public string LogonUserQuery { get; set; } = "";
}
