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

using SixLabors.ImageSharp;
using System.Net;

namespace AufBauWerk.Vivendi.OlatAuth;

public class Settings
{
    public string AuthProvider { get; set; } = "TOCCO";
    public required string ConnectionString { get; set; }
    public required NetworkCredential Credentials { get; set; }
    public required string LogonUserQuery { get; set; }
    public Size MaxPortraitSize { get; set; } = new(100, 100);
    public required Uri RestApiEndpoint { get; set; }
}
