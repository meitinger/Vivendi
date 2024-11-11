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
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;

namespace AufBauWerk.Vivendi.Launcher;

public sealed record Credentials(IPublicClientApplication App, IAccount Account, string UserName, string Password) : IDisposable
{
    private (string Header, X509Certificate2 Certificate)? _cache;
    private bool _disposed = false;
    private readonly object _lock = new();

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException($"{nameof(Credentials)}({UserName})");
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed) { return; }
        lock (_lock)
        {
            if (_disposed) { return; }
            _disposed = true;
            _cache?.Certificate.Dispose();
            _cache = null;
        }
    }

    public async Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken)
    {
        CheckDisposed();
        AuthenticationResult auth = await App.AcquireTokenAsync(useCache: true, cancellationToken) ?? throw new OperationCanceledException();
        string header = auth.CreateAuthorizationHeader();
        lock (_lock)
        {
            CheckDisposed();
            if (_cache is { } cache && cache.Header == header) { return cache.Certificate; }
        }
        using HttpClient client = new() { BaseAddress = Settings.Instance.BaseUri };
        client.DefaultRequestHeaders.Add("Authorization", header);
        byte[] rawData = await client.GetByteArrayAsync("api/v1/certificate", cancellationToken);
        X509Certificate2 certificate = new(rawData, Settings.Instance.SharedSecret);
        lock (_lock)
        {
            CheckDisposed();
            _cache = (header, certificate);
        }
        return certificate;
    }
}

public static partial class Extensions
{
    public static async Task<AuthenticationResult?> AcquireTokenAsync(this IPublicClientApplication app, bool useCache, CancellationToken cancellationToken)
    {
        string[] scopes = new[] { $"{Settings.Instance.ApplicationId}/.default" };
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

    public static async Task<Credentials?> LoginAsync(this IPublicClientApplication app, CancellationToken cancellationToken)
    {
        for (bool useCache = true; await app.AcquireTokenAsync(useCache, cancellationToken) is { } auth; useCache = false)
        {
            string userName = auth.Account.Username;
            int at = userName.IndexOf('@');
            if (-1 < at) { userName = userName[..at]; }
            using HttpClient client = new() { BaseAddress = Settings.Instance.BaseUri };
            client.DefaultRequestHeaders.Add("Authorization", auth.CreateAuthorizationHeader());
            byte[] rawData;
            try { rawData = await client.GetByteArrayAsync("api/v1/password", cancellationToken); }
            catch (HttpRequestException ex)
            {
                if (Program.ShowMessage($"Anmeldung am Server fehlgeschlagen: {ex.Message}\nWollen Sie die Anmeldung mit anderen Zugangsdaten erneut versuchen?", MessageBoxButton.YesNo, MessageBoxImage.Warning) is MessageBoxResult.Yes)
                {
                    continue;
                }
                break;
            }
            byte[] salt = rawData[..16];
            byte[] iv = rawData[16..32];
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Settings.Instance.SharedSecret), salt, iterations: 3000, HashAlgorithmName.SHA256, outputLength: 32);
            using ICryptoTransform decryptor = Aes.Create().CreateDecryptor(key, iv);
            byte[] passwordHash = decryptor.TransformFinalBlock(rawData, 32, rawData.Length - 32);
            string password = Convert.ToBase64String(passwordHash)[..20];
            return new(app, auth.Account, userName, password);
        }
        return null;
    }
}
