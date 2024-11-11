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

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AufBauWerk.Vivendi.Gateway;

[ApiController]
public sealed class Api(Settings settings, CertificateAuthority ca) : ControllerBase
{
    private async Task<IResult> WithAuthenticationAsync(Func<string, AuthenticateResult, IResult> action)
    {
        AuthenticateResult result = await HttpContext.AuthenticateAsync();
        if (!result.Succeeded) return Results.Unauthorized();
        string userName = result.Principal.GetUserName(settings);
        return action(userName, result);
    }

    [HttpGet("/api/v1/certificate")]
    public Task<IResult> GetCertificateAsync() => WithAuthenticationAsync((userName, auth) =>
    {
        DateTimeOffset notBefore = auth.Properties?.IssuedUtc ?? DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset notAfter = auth.Properties?.ExpiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(5);
        using RSA privateKey = RSA.Create(2048);
        CertificateRequest request = new($"CN={userName}", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(X509BasicConstraintsExtension.CreateForEndEntity()); // basicConstraints = CA:FALSE
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false)); // keyUsage = digitalSignature
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false)); // extendedKeyUsage = clientAuth
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false)); // subjectKeyIdentifier = hash
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca.Certificate, true, false)); // authorityKeyIdentifier = keyid,issuer
        using X509Certificate2 certificate = request.Create(ca.Certificate, notBefore, notAfter, ca.GetNextSerialNumber());
        return Results.Bytes(certificate.CopyWithPrivateKey(privateKey).Export(X509ContentType.Pkcs12, settings.Api.SharedSecret));
    });

    [HttpGet("/api/v1/password")]
    public Task<IResult> GetPasswordAsync() => WithAuthenticationAsync((userName, _) =>
    {
        string expandedTemplate = settings.Api.PasswordHashTemplate.Replace("{UserName}", userName, StringComparison.OrdinalIgnoreCase);
        byte[] passwordHash = SHA256.HashData(Encoding.Unicode.GetBytes(expandedTemplate));
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(settings.Api.SharedSecret), salt, iterations: 3000, HashAlgorithmName.SHA256, outputLength: 32);
        using ICryptoTransform encryptor = Aes.Create().CreateEncryptor(key, iv);
        byte[] encryptedHash = encryptor.TransformFinalBlock(passwordHash, 0, passwordHash.Length);
        return Results.Bytes([.. salt, .. iv, .. encryptedHash]);
    });
}
