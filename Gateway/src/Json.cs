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

using System.Text.Json.Serialization;

namespace AufBauWerk.Vivendi.Gateway;

[JsonSerializable(typeof(RemoteAppRequest))]
[JsonSerializable(typeof(RemoteAppResponse))]
[JsonSerializable(typeof(ExternalUser))]
[JsonSerializable(typeof(WindowsUser))]
internal partial class SerializerContext : JsonSerializerContext { }

public class RemoteAppRequest
{
    public Dictionary<Guid, string> KnownPaths { get; } = [];
}

public class RemoteAppResponse
{
    public static RemoteAppResponse Build(WindowsUser windowsUser, byte[] rdpFileContent) => new()
    {
        UserName = windowsUser.UserName,
        Password = windowsUser.Password,
        RdpFileContent = rdpFileContent,
    };

    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required byte[] RdpFileContent { get; set; }
}

public class ExternalUser
{
    public required string UserName { get; set; }
}

public class WindowsUser
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
}
