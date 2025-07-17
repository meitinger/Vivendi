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
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AufBauWerk.Vivendi.RemoteApp;

[JsonSerializable(typeof(Request))]
[JsonSerializable(typeof(Response))]
internal partial class SerializerContext : JsonSerializerContext
{
    public static JsonTypeInfo GetInfo(Type type) => Default.GetTypeInfo(type) ?? throw new ArgumentOutOfRangeException(nameof(type));
}

public class Request
{
    public Dictionary<Guid, string> KnownPaths { get; } = [];
}

public class Response
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required string RdpFileContent { get; set; }
}

public static partial class Extensions
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
            requestMsg.Content = JsonContent.Create(request, SerializerContext.GetInfo(typeof(Request)));
            HttpResponseMessage responseMsg = await client.SendAsync(requestMsg, cancellationToken);
            if (responseMsg.StatusCode is System.Net.HttpStatusCode.Forbidden)
            {
                continue;
            }
            byte[] rawData = await responseMsg.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(cancellationToken);
            using Aes aes = Aes.Create();
            aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Settings.Instance.SharedSecret), salt: rawData.AsSpan(..16), iterations: 3000, HashAlgorithmName.SHA256, outputLength: aes.KeySize / 8);
            byte[] response = aes.DecryptCbc(ciphertext: rawData.AsSpan(32..), iv: rawData.AsSpan(16..32));
            return (Response?)JsonSerializer.Deserialize(Encoding.UTF8.GetString(response), SerializerContext.GetInfo(typeof(Response))) ?? throw new InvalidDataException();
        }
        return null;
    }
}
