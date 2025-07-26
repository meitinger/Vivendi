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

namespace AufBauWerk.Vivendi.RemoteApp;

internal static class KnownFolders
{
    private static readonly HashSet<Guid> AllowedIds =
    [
        new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), //Documents
        new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), //Desktop
        new("374DE290-123F-4565-9164-39C4925E467B"), //Downloads
        new("4BD8D571-6D19-48D3-BE97-422220080E43"), //Music
        new("33E28130-4E1E-4676-835A-98395C3BC3BB"), //Pictures
        new ("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), //Videos
    ];

    public static Dictionary<Guid, string> GetPaths()
    {
        Dictionary<Guid, string> paths = [];
        foreach (Guid knownFolderId in AllowedIds)
        {
            if (Win32.TryGetKnownFolderPath(knownFolderId, out string? path) && Path.IsPathFullyQualified(path))
            {
                paths.Add(knownFolderId, path);
            }
        }
        return paths;
    }
}
