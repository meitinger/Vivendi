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

using System.IO;
using System.Text.Json;

namespace AufBauWerk.Vivendi.Launcher;

public class Settings
{
    private static Settings? _instance;
    public static Settings Instance => _instance ?? throw new InvalidOperationException();

    public static void Load()
    {
        using FileStream jsonFile = new(Path.Combine(AppContext.BaseDirectory, "Launcher.settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        _instance = JsonSerializer.Deserialize<Settings>(jsonFile);
    }

    public string VivendiPath { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public Uri? BaseUri { get; set; } = null;
    public DatabaseSettings Database { get; set; } = new();
}

public class DatabaseSettings
{
    public string HostName { get; set; } = "";
    public int Port { get; set; }
}
