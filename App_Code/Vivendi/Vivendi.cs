/* Copyright (C) 2019-2021, Manuel Meitinger
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 2 of the License, or
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

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace Aufbauwerk.Tools.Vivendi
{
    public enum VivendiSource
    {
        Data,
        Store
    }

    public sealed class Vivendi : VivendiCollection
    {
        public static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        public static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

        public static Vivendi CreateRoot(string userName, IDictionary<VivendiSource, string> connectionStrings, string displayName = "(Hauptgruppe)") => new Vivendi(null, string.Empty, userName, connectionStrings, displayName);

        private readonly IDictionary<VivendiSource, string> _connectionStrings;
        private short _maxReadAccessLevel;
        private short _maxWriteAccessLevel;
        private readonly IDictionary<int, ISet<int>> _readableSectionsByObjectType = new Dictionary<int, ISet<int>>();
        private readonly IDictionary<int, short> _readAccessLevels = new Dictionary<int, short>();
        private readonly IDictionary<int, ISet<int>> _sectionMap = new Dictionary<int, ISet<int>>();
        private readonly IDictionary<int, ISet<int>> _writableSectionsByObjectType = new Dictionary<int, ISet<int>>();
        private readonly IDictionary<int, short> _writeAccessLevels = new Dictionary<int, short>();

        internal Vivendi(VivendiCollection? parent, string name, string userName, IDictionary<VivendiSource, string> connectionStrings, string? displayName = null)
        : base
        (
              parent: parent,
              name: name,
              creationDate: DateTime.Now,
              localizedName: displayName
        )
        {
            // set all properties
            UserName = userName;
            _connectionStrings = connectionStrings;

            // query the permissions
            InitializePermissions();
        }

        public string UserName { get; }

        private SqlCommand BuildCommand(SqlConnection connection, string commandText, SqlParameter[] parameters)
        {
            // create the command object
            var command = new SqlCommand(commandText, connection);
            command.Parameters.AddRange(parameters);
            return command;
        }

        internal int ExecuteNonQuery(VivendiSource source, string commandText, params SqlParameter[] parameters)
        {
            // run a INSERT, UPDATE or DELETE command and return the affected rows
            using var connection = OpenConnection(source);
            return BuildCommand(connection, commandText, parameters).ExecuteNonQuery();
        }

        internal SqlDataReader ExecuteReader(VivendiSource source, string commandText, params SqlParameter[] parameters) =>
            // return a reader that closes the connection once it's disposed
            BuildCommand(OpenConnection(source), commandText, parameters).ExecuteReader(CommandBehavior.CloseConnection);

        internal object ExecuteScalar(VivendiSource source, string commandText, params SqlParameter[] parameters)
        {
            // run a simple query
            using var connection = OpenConnection(source);
            return BuildCommand(connection, commandText, parameters).ExecuteScalar();
        }

        internal IEnumerable<int> ExpandSection(int section) => (_sectionMap.TryGetValue(section, out var subSections) ? subSections : Enumerable.Empty<int>()).Prepend(section);

        private int GetAccessLevel(IEnumerable<int>? sections, int maxAccessLevel, IDictionary<int, short> accessLevels) => sections == null ? maxAccessLevel : !sections.Any() ? 0 : sections.Max(s => accessLevels.TryGetValue(s, out var level) ? level : 0);

        internal IEnumerable<int> GetReadableSections(int objectType) => _readableSectionsByObjectType.TryGetValue(objectType, out var sections) ? sections : Enumerable.Empty<int>();

        internal int GetReadAccessLevel(IEnumerable<int>? sections) => GetAccessLevel(sections, _maxReadAccessLevel, _readAccessLevels);

        internal IEnumerable<int> GetWritableSections(int objectType) => _writableSectionsByObjectType.TryGetValue(objectType, out var sections) ? sections : Enumerable.Empty<int>();

        internal int GetWriteAccessLevel(IEnumerable<int>? sections) => GetAccessLevel(sections, _maxWriteAccessLevel, _writeAccessLevels);

        private void InitializePermissions()
        {
            // query the user's permissions
            using var reader = ExecuteReader
            (
                VivendiSource.Data,
@"
WITH [HIERARCHY]([ID], [Parent]) AS
(
    SELECT [Z_MA], [Z_Parent_MA]
    FROM [dbo].[MANDANT]
    UNION ALL
    SELECT [dbo].[MANDANT].Z_MA, [HIERARCHY].[Parent]
    FROM [dbo].[MANDANT] JOIN [HIERARCHY] ON [dbo].[MANDANT].[Z_Parent_MA] = [HIERARCHY].[ID]
)
SELECT [Parent], STRING_AGG([ID], ', ') AS [IDs]
FROM [HIERARCHY]
GROUP BY [Parent];
SELECT
    [dbo].[GRUPPENZUORDNUNG].[iMandant] AS [Section],
    [dbo].[BERECHTIGUNGEN].[Vorgang] AS [Permission],
    [dbo].[BERECHTIGUNGEN].[Auflisten] AS [Read],
    [dbo].[BERECHTIGUNGEN].[Schreiben] AS [Write]
FROM
    [dbo].[BENUTZER]
    JOIN
    [dbo].[GRUPPENZUORDNUNG] ON [dbo].[BENUTZER].[Z_BN] = [dbo].[GRUPPENZUORDNUNG].[iBenutzer]
    JOIN
    [dbo].[BERECHTIGUNGEN] ON [dbo].[GRUPPENZUORDNUNG].[iGruppe] = [dbo].[BERECHTIGUNGEN].[iGruppe]
WHERE
    [dbo].[BENUTZER].[Benutzer] = @UserName AND
    [dbo].[BENUTZER].[Inaktiv]  = 0 AND
    ([dbo].[BENUTZER].[AbDatum]  IS NULL OR [dbo].[BENUTZER].[AbDatum]  <= @Today) AND
    ([dbo].[BENUTZER].[BisDatum] IS NULL OR [dbo].[BENUTZER].[BisDatum] >= @Today) AND
    ([dbo].[GRUPPENZUORDNUNG].[dtGueltigAb]  IS NULL OR [dbo].[GRUPPENZUORDNUNG].[dtGueltigAb]  <= @Today) AND
    ([dbo].[GRUPPENZUORDNUNG].[dtGueltigBis] IS NULL OR [dbo].[GRUPPENZUORDNUNG].[dtGueltigBis] >= @Today) AND
    [dbo].[BERECHTIGUNGEN].[Vorgang] IN (71, 1, 3) AND
    ([dbo].[BERECHTIGUNGEN].[Auflisten] > 0 OR [dbo].[BERECHTIGUNGEN].[Schreiben] > 0)
",
                new SqlParameter("UserName", UserName),
                new SqlParameter("Today", DateTime.Today)
            );

            // build a map of sections and all sub sections
            while (reader.Read())
            {
                _sectionMap.Add(reader.GetInt32("Parent"), reader.GetIDs("IDs").ToHashSet());
            }

            // get to the next query result
            if (!reader.NextResult())
            {
                return;
            }

            // parse all permissions
            while (reader.Read())
            {
                var section = reader.GetInt32Optional("Section") ?? 0;
                addAccessibleSection(_readableSectionsByObjectType, 4, section); // Bereich
                int objectType;
                switch (reader.GetInt32("Permission"))
                {
                    case 71: // Dateiablage
                        addAccessLevel(_readAccessLevels, reader.GetInt16("Read"), ref _maxReadAccessLevel, section);
                        addAccessLevel(_writeAccessLevels, reader.GetInt16("Write"), ref _maxWriteAccessLevel, section);
                        continue;
                    case 1: // Mitarbeiter
                        objectType = 0;
                        break;
                    case 3: // Klient
                        objectType = 1;
                        break;
                    default:
                        continue;
                }
                if (reader.GetInt16("Read") != 0)
                {
                    addAccessibleSection(_readableSectionsByObjectType, objectType, section);
                }
                if (reader.GetInt16("Write") != 0)
                {
                    addAccessibleSection(_writableSectionsByObjectType, objectType, section);
                }
            }

            void addAccessibleSection(IDictionary<int, ISet<int>> sectionsByObjectType, int objectType, int section)
            {
                // create the section set for the given object type if necessary
                if (!sectionsByObjectType.TryGetValue(objectType, out var sections))
                {
                    sectionsByObjectType.Add(objectType, sections = new HashSet<int>());
                }

                // add the section and all its children
                sections.UnionWith(ExpandSection(section));
            }

            void addAccessLevel(IDictionary<int, short> map, short accessLevel, ref short maxAccessLevel, int section)
            {
                // determine and set the maximum access level for the given section and globally
                foreach (var sectionOrSubSection in ExpandSection(section))
                {
                    if (!map.TryGetValue(sectionOrSubSection, out var maxAccessLevelForSection) || accessLevel > maxAccessLevelForSection)
                    {
                        map[sectionOrSubSection] = accessLevel;
                    }
                    if (accessLevel > maxAccessLevel)
                    {
                        maxAccessLevel = accessLevel;
                    }
                }
            }
        }

        private SqlConnection OpenConnection(VivendiSource source)
        {
            // get the corresponding connection string and open the connection
            if (!_connectionStrings.TryGetValue(source, out var connectionString))
            {
                throw new InvalidEnumArgumentException(nameof(source), (int)source, typeof(VivendiSource));
            }
            var connection = new SqlConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
