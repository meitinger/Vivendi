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
using System.Diagnostics;

nint parent = 0;
try
{
    // show the progress dialog
    using Progress progress = new();
    progress.Title = Settings.Instance.Title;
    progress.Show();
    parent = progress.Window;
    if (parent is 0) { parent = Win32.GetDesktopWindow(); }

    // create the local app using WAM
    IPublicClientApplication app = await PublicClientApplicationBuilder
        .Create(Settings.Instance.ApplicationId)
        .WithTenantId(Settings.Instance.TenantId)
        .WithDefaultRedirectUri()
        .WithParentActivityOrWindow(() => parent)
        .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = Settings.Instance.Title })
        .Build()
        .EnableTokenCacheAsync();

    // authenticate with Entra ID and fetch the remote app definition
    Request request = new() { KnownFolders = KnownFolders.GetPaths() };
    using CancellationTokenSource cancellation = new();
    Task<Response> responseTask = app.CallEndpointAsync(request, cancellation.Token);
    Response response;
    while (true)
    {
        using CancellationTokenSource timeout = new(millisecondsDelay: 100);
        try
        {
            response = await responseTask.WaitAsync(timeout.Token);
            break;
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == timeout.Token)
        {
            if (progress.IsCancelled) { cancellation.Cancel(); }
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancellation.Token)
        {
            Environment.ExitCode = Win32.ERROR_CANCELLED;
            return;
        }
    }

    // launch the remote app
    using Process process = Mstsc.StartRemoteApp(response.UserName, response.Password, response.RdpFileContent);
    process.WaitForInputIdle();
    parent = process.MainWindowHandle;
    progress.Hide();
    process.WaitForExit();
    Environment.ExitCode = process.ExitCode;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Console.WriteLine(parent);
    Win32.ShowError(parent, ex.Message);
    Console.WriteLine("ehefghfhfggfehe");
    Environment.ExitCode = ex.HResult;
}
