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

namespace AufBauWerk.Vivendi.Launcher;

[JsonSerializable(typeof(Credential))]
[JsonSerializable(typeof(Message))]
internal partial class SerializerContext : JsonSerializerContext { }

public class Credential
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
}

internal class Message
{
    public required bool Failed { get; set; }
    public required Credential? Credential { get; set; }
}
