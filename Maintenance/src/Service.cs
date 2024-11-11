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

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;

namespace AufBauWerk.Vivendi.Maintenance;

public class Service(ILogger<Service> logger, IOptionsMonitor<Settings> options) : BackgroundService
{
    private static async Task<IReadOnlyDictionary<int, string>> GetImageIdsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        using SqlCommand command = new("SELECT [Z_BE], ISNULL([BildPruefSumme],'') FROM [dbo].[BEMERKUNGEN] WHERE [Bild] IS NOT NULL", connection);
        using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Dictionary<int, string> result = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add((int)reader[0], (string)reader[1]);
        }
        return result;
    }

    private static async Task<byte[]?> GetImageByIdAsync(SqlConnection connection, int id, CancellationToken cancellationToken)
    {
        using SqlCommand command = new("SELECT [Bild] FROM [dbo].[BEMERKUNGEN] WHERE [Z_BE] = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        object result = await command.ExecuteScalarAsync(cancellationToken);
        return result == DBNull.Value ? null : (byte[])result;
    }

    private static async Task<bool> SetImageByIdAsync(SqlConnection connection, int id, byte[] image, CancellationToken cancellationToken)
    {
        using SqlCommand command = new("UPDATE [dbo].[BEMERKUNGEN] SET [BildPruefSumme] = LOWER(CONVERT(char(32),HASHBYTES('md5',@Image),2)), [Bild] = @Image WHERE [Z_BE] = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Image", image);
        return 0 < await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Dictionary<(string ConnectionString, int Id), (Size Size, string Hash)> knownImages = [];
            while (!stoppingToken.IsCancellationRequested)
            {
                string connectionString = options.CurrentValue.ConnectionString;
                Size maxSize = options.CurrentValue.MaxImageSize;
                using SqlConnection connection = new(connectionString);
                await connection.OpenAsync(stoppingToken);
                foreach (var (id, hash) in await GetImageIdsAsync(connection, stoppingToken))
                {
                    if (knownImages.TryGetValue((connectionString, id), out var knownImage) &&
                        knownImage.Size.Width <= maxSize.Width &&
                        knownImage.Size.Height <= maxSize.Height &&
                        string.Equals(knownImage.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    byte[]? data = await GetImageByIdAsync(connection, id, stoppingToken);
                    if (data is null)
                    {
                        knownImages.Remove((connectionString, id));
                        continue;
                    }
                    try
                    {
                        using MemoryStream inStream = new(data);
                        using Image image = await Image.LoadAsync(inStream, stoppingToken);
                        if (image.Width <= maxSize.Width && image.Height <= maxSize.Height)
                        {
                            knownImages[(connectionString, id)] = (image.Size, hash);
                            logger.LogInformation("Image #{Id} with {Dimension} is already within bounds, using {Size} bytes.", id, image.Size, data.Length);
                            continue;
                        }
                        image.Mutate(op => op.Resize(new ResizeOptions()
                        {
                            Mode = ResizeMode.Max,
                            Sampler = KnownResamplers.Bicubic,
                            Size = maxSize,
                        }));
                        using MemoryStream outStream = new();
                        await image.SaveAsJpegAsync(outStream, stoppingToken);
                        data = outStream.ToArray();
                        knownImage = (image.Size, Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant());
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to resize image #{Id}: {Message}", id, ex.ToString());
                        continue;
                    }
                    if (!await SetImageByIdAsync(connection, id, data, stoppingToken))
                    {
                        knownImages.Remove((connectionString, id));
                        continue;
                    }
                    knownImages[(connectionString, id)] = knownImage;
                    logger.LogInformation("Resized image #{Id} to {Dimension} using {Size} bytes.", id, knownImage.Size, data.Length);
                }
                await Task.Delay(options.CurrentValue.Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Message}", ex.ToString());

            // hard exit for SCM to notice
            Environment.Exit(1);
        }
    }
}
