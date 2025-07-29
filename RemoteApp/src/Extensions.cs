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

using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace AufBauWerk.Vivendi.RemoteApp;

internal static partial class Extensions
{
    #region Win32

    [LibraryImport("user32.dll")]
    private static partial nint GetDesktopWindow();

    #endregion

    public static async Task<AuthenticationResult?> AcquireTokenAsync(this IPublicClientApplication app, bool useCache, CancellationToken cancellationToken)
    {
        string[] scopes = [$"{Settings.Instance.ApplicationId}/.default"];
        if (useCache)
        {
            IAccount account = (await app.GetAccountsAsync()).FirstOrDefault(PublicClientApplication.OperatingSystemAccount);
            try { return await app.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken); }
            catch (MsalUiRequiredException) { }
        }
        try { return await app.AcquireTokenInteractive(scopes).ExecuteAsync(cancellationToken); }
        catch (MsalClientException ex) when (ex.ErrorCode is MsalError.AuthenticationCanceledError) { }
        return null;
    }

    public static async Task<Response?> CallEndpointAsync(this IPublicClientApplication app, Request request, CancellationToken cancellationToken)
    {
        for (bool useCache = true; await app.AcquireTokenAsync(useCache, cancellationToken) is AuthenticationResult auth; useCache = false)
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("Authorization", auth.CreateAuthorizationHeader());
            HttpResponseMessage responseMsg = await client.PostAsJsonAsync(Settings.Instance.EndpointUri, request, SerializerContext.Default.Request, cancellationToken);
            if (responseMsg.StatusCode is System.Net.HttpStatusCode.Forbidden) { continue; }
            responseMsg.EnsureSuccessStatusCode();
            Response? response = await responseMsg.Content.ReadFromJsonAsync(SerializerContext.Default.Response, cancellationToken);
            return response ?? throw new InvalidDataException();
        }
        return null;
    }

    public static async Task<IPublicClientApplication> EnableTokenCacheAsync(this IPublicClientApplication app)
    {
        StorageCreationProperties properties = new StorageCreationPropertiesBuilder(Settings.Instance.CacheFileName, Settings.Instance.CacheDirectory).Build();
        MsalCacheHelper cache = await MsalCacheHelper.CreateAsync(properties);
        cache.RegisterCache(app.UserTokenCache);
        return app;
    }

    public static PublicClientApplicationBuilder WithDesktopAsParent(this PublicClientApplicationBuilder builder) => builder.WithParentActivityOrWindow(GetDesktopWindow);
}
