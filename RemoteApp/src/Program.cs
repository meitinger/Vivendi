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

using AufBauWerk.Vivendi.RemoteApp;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using System.Diagnostics;

try
{
    // create the local app using WAM
    IPublicClientApplication app = PublicClientApplicationBuilder
        .Create(Settings.Instance.ApplicationId)
        .WithTenantId(Settings.Instance.TenantId)
        .WithDefaultRedirectUri()
        .WithDesktopAsParent()
        .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = Settings.Instance.Title })
        .Build();

    // setup a token cache in %APPDATA%
    MsalCacheHelper cache = await MsalCacheHelper.CreateAsync(new StorageCreationPropertiesBuilder
    (
        cacheFileName: "token.cache",
        cacheDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VivendiLauncher")
    ).Build());
    cache.RegisterCache(app.UserTokenCache);

    // authenticate with Entra ID and fetch the remote app definition
    Request request = new() { KnownPaths = KnownFolders.GetPaths() };
    if (await app.CallEndpointAsync(request) is not Response response)
    {
        Environment.ExitCode = Win32.ERROR_CANCELLED;
        return;
    }

    // launch the remote app
    using Process process = Win32.StartRemoteApp(response.UserName, response.Password, response.RdpFileContent);
    process.WaitForExit();
    Environment.ExitCode = process.ExitCode;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Win32.ShowError(ex.Message);
    Environment.ExitCode = ex.HResult;
}
