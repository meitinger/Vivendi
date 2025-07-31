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

using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AufBauWerk.Vivendi.OlatAuth;

[ApiController]
public sealed class OpenOlat : ControllerBase, IDisposable
{
    private class ManagedUser
    {
        [JsonPropertyName("key")] public required long IdentityKey { get; set; }
        [JsonPropertyName("externalId")] public required string ExternalId { get; set; }
        [JsonPropertyName("login")] public string? Login { get; set; }
        [JsonPropertyName("firstName")] public string? FirstName { get; set; }
        [JsonPropertyName("lastName")] public string? LastName { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonIgnore] public string UserName { get; set; } = "";
        [JsonIgnore] public Stream? Portrait { get; set; }

        public bool NeedsUpdate(ManagedUser user)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(user.ExternalId, ExternalId);

            // Login cannot be updated
            return
                FirstName != user.FirstName ||
                LastName != user.LastName ||
                Email != user.Email;
        }
    }

    private readonly HttpClient client;
    private readonly Settings settings;

    public OpenOlat(IOptions<Settings> options)
    {
        settings = options.Value;
        client = new(new HttpClientHandler() { Credentials = settings.Credentials }) { BaseAddress = settings.RestApiEndpoint };
        client.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    private async Task<Stream?> CheckPortraitAsync(byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            MemoryStream inStream = new(data);
            using Image image = await Image.LoadAsync(inStream, cancellationToken);
            if (image.Metadata.DecodedImageFormat == JpegFormat.Instance && image.Width <= settings.MaxPortraitSize.Width && image.Height <= settings.MaxPortraitSize.Height)
            {
                inStream.Position = 0;
                return inStream;
            }
            image.Mutate(op => op.Resize(new ResizeOptions()
            {
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Bicubic,
                Size = settings.MaxPortraitSize,
            }));
            MemoryStream outStream = new();
            await image.SaveAsJpegAsync(outStream, cancellationToken);
            outStream.Position = 0;
            return outStream;
        }
        catch (ImageFormatException) { return null; }
        catch (ImageProcessingException) { return null; }
    }

    private async Task<bool> DeletePortraitAsync(long identityKey, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.DeleteAsync($"users/{identityKey}/portrait", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound) { return false; }
        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<ManagedUser?> ExecuteQueryAsync(string userName, string password, CancellationToken cancellationToken)
    {
        using SqlConnection connection = new(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using SqlCommand command = new(settings.LogonUserQuery, connection);
        command.Parameters.AddWithValue("@UserName", userName);
        command.Parameters.AddWithValue("@Password", password);
        using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) { return null; }
        object portrait = reader[nameof(ManagedUser.Portrait)];
        return new()
        {
            IdentityKey = 0,
            ExternalId = (string)reader[nameof(ManagedUser.ExternalId)],
            Login = (string)reader[nameof(ManagedUser.Login)],
            FirstName = (string)reader[nameof(ManagedUser.FirstName)],
            LastName = (string)reader[nameof(ManagedUser.LastName)],
            Email = (string)reader[nameof(ManagedUser.Email)],
            UserName = userName,
            Portrait = portrait == DBNull.Value
                ? null
                : await CheckPortraitAsync((byte[])portrait, cancellationToken),
        };
    }

    private async Task<ManagedUser?> GetUserAsync(string externalId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync($"users?externalId={Uri.EscapeDataString(externalId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ManagedUser[]>(cancellationToken))?.SingleOrDefault();
    }

    private async Task PostAuthenticationAsync(long identityKey, string provider, string authUsername, string? credential, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync($"users/{identityKey}/authentications", new { identityKey, provider, authUsername, credential }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostPortraitAsync(long identityKey, Stream portrait, CancellationToken cancellationToken)
    {
        using StreamContent imageContent = new(portrait);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using MultipartFormDataContent postContent = new() { { imageContent, "portrait", "portrait.jpg" } };
        using HttpResponseMessage response = await client.PostAsync($"users/{identityKey}/portrait", postContent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostUserAsync(ManagedUser user, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync($"users/{user.IdentityKey}", user, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<long> PutUserAsync(ManagedUser user, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync("users", user, cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(json).RootElement.GetProperty("key").GetInt64();
    }

    private async Task UpsertUserAsync(ManagedUser user, CancellationToken cancellationToken)
    {
        ManagedUser? existingUser = await GetUserAsync(user.ExternalId, cancellationToken);
        if (existingUser is null)
        {
            user.IdentityKey = await PutUserAsync(user, cancellationToken);
        }
        else
        {
            user.IdentityKey = existingUser.IdentityKey;
            if (existingUser.NeedsUpdate(user))
            {
                await PostUserAsync(user, cancellationToken);
            }
        }
        await PostAuthenticationAsync(user.IdentityKey, settings.AuthProvider, user.UserName, credential: null, cancellationToken);
        if (user.Portrait is null)
        {
            await DeletePortraitAsync(user.IdentityKey, cancellationToken);
        }
        else
        {
            await PostPortraitAsync(user.IdentityKey, user.Portrait, cancellationToken);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        client.Dispose();
    }

    [HttpPost("/openolat/authenticate")]
    public async Task<IResult> PostAuthenticateAsync()
    {
        string? userName = Request.Form["username"];
        string? password = Request.Form["password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return Results.BadRequest();
        }
        ManagedUser? user = await ExecuteQueryAsync(userName, password, HttpContext.RequestAborted);
        if (user is null)
        {
            return Results.Unauthorized();
        }
        await UpsertUserAsync(user, HttpContext.RequestAborted);
        return Results.Ok(new { success = true });
    }
}
