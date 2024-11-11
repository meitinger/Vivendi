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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AufBauWerk.Vivendi.Gateway;

public sealed class CertificateAuthority : IDisposable
{
    private readonly X509Certificate2 certificate;
    private bool disposed = false;
    private long lastSerialNumber = 0;

    public CertificateAuthority(Settings settings)
    {
        X509Certificate2 certOnly = new(Convert.FromBase64String(settings.CertificateAuthority.Certificate));
        using RSA rsaKey = RSA.Create();
        rsaKey.ImportPkcs8PrivateKey(Convert.FromBase64String(settings.CertificateAuthority.PrivateKey), out var _);
        certificate = certOnly.CopyWithPrivateKey(rsaKey);
    }

    public X509Certificate2 Certificate => disposed
        ? throw new ObjectDisposedException(nameof(CertificateAuthority))
        : certificate;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (!disposed)
        {
            disposed = true;
            certificate.Dispose();
        }
    }

    public byte[] GetNextSerialNumber() => disposed
        ? throw new ObjectDisposedException(nameof(CertificateAuthority))
        : BitConverter.GetBytes(Interlocked.Increment(ref lastSerialNumber));
}
