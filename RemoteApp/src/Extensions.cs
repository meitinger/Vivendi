﻿/*
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
using System.Text.Json;

namespace AufBauWerk.Vivendi.RemoteApp;

internal static partial class Extensions
{
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

    public static async Task<Response?> CallEndpointAsync(this IPublicClientApplication app, Request request, CancellationToken cancellationToken = default)
    {
        for (bool useCache = true; await app.AcquireTokenAsync(useCache, cancellationToken) is { } auth; useCache = false)
        {
            using HttpClient client = new();
            HttpRequestMessage requestMsg = new(HttpMethod.Post, Settings.Instance.EndpointUri);
            requestMsg.Headers.Add("Authorization", auth.CreateAuthorizationHeader());
            ByteArrayContent content = new(JsonSerializer.SerializeToUtf8Bytes(request, typeof(Request), SerializerContext.Default));
            content.Headers.ContentType = new(mediaType: "application/json", charSet: "utf-8");
            requestMsg.Content = content;
            HttpResponseMessage responseMsg = await client.SendAsync(requestMsg, cancellationToken);
            if (responseMsg.StatusCode is System.Net.HttpStatusCode.Forbidden)
            {
                continue;
            }
            using Stream responseStream = await responseMsg.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(cancellationToken);
            Response? response = (Response?)await JsonSerializer.DeserializeAsync(responseStream, typeof(Response), SerializerContext.Default, cancellationToken);
            return response ?? throw new InvalidDataException();
        }
        return null;
    }
}
