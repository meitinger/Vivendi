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

namespace AufBauWerk.Vivendi.Gateway;

public class RdpFile(ILogger<RdpFile> logger)
{
    private readonly string path = Path.Combine(AppContext.BaseDirectory, "vivendi.rdp");
    private (DateTime, byte[])? cache = null;

    public async Task<byte[]> GetContentAsync(CancellationToken cancellationToken)
    {
        DateTime time = File.GetLastWriteTimeUtc(path);
        if (cache is (DateTime cacheTime, byte[] cacheContent) && time == cacheTime)
        {
            return cacheContent;
        }
        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
        cache = (time, content);
        logger.LogInformation("Cached RDP file of {Length} bytes.", content.Length);
        return content;
    }
}
