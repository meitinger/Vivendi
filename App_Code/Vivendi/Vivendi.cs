/* Copyright (C) 2019, Manuel Meitinger
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;

namespace Aufbauwerk.Tools.Vivendi
{
    public enum VivendiSource
    {
        Data,
        Store
    }

    public sealed class Vivendi : VivendiCollection
    {
        public static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
        public static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        readonly IDictionary<VivendiSource, string> _connectionStrings;
        short _maxReadAccessLevel;
        short _maxWriteAccessLevel;
        readonly IDictionary<int, short> _readAccessLevels = new Dictionary<int, short>();
        readonly IDictionary<int, ISet<int>> _sections = new Dictionary<int, ISet<int>>();
        readonly IDictionary<int, short> _writeAccessLevels = new Dictionary<int, short>();

        internal Vivendi(VivendiCollection parent, string name, string userName, SecurityIdentifier userSid, IDictionary<VivendiSource, string> connectionStrings)
        : this(parent, VivendiResourceType.Named, name, userName, userSid, connectionStrings)
        { }

        public Vivendi(string userName, SecurityIdentifier userSid, IDictionary<VivendiSource, string> connectionStrings)
        : this("(Hauptgruppe)", userName, userSid, connectionStrings)
        { }

        public Vivendi(string name, string userName, SecurityIdentifier userSid, IDictionary<VivendiSource, string> connectionStrings)
        : this(null, VivendiResourceType.Root, name, userName, userSid, connectionStrings)
        { }

        Vivendi(VivendiCollection parent, VivendiResourceType type, string name, string userName, SecurityIdentifier userSid, IDictionary<VivendiSource, string> connectionStrings)
        : base(parent, type, 0, name)
        {
            // set all properties
            UserName = userName ?? throw new ArgumentNullException("userName");
            UserSid = userSid ?? throw new ArgumentNullException("userSid");
            _connectionStrings = connectionStrings ?? throw new ArgumentNullException("connectionStrings");

            // query the permissions
            InitializePermissions();
        }

        public Func<VivendiResource, bool> AllowModificationOfVivendiResource { internal get; set; }

        public Func<VivendiResource, SecurityIdentifier, bool> AllowModificationOfOwnedResource { internal get; set; }

        public string UserName { get; }

        public SecurityIdentifier UserSid { get; }

        void AddAccessLevel(IDictionary<int, short> map, short accessLevel, ref short maxAccessLevel, int section)
        {
            // determine and set the maximum access level for the given section and globaly
            if (!map.TryGetValue(section, out var maxAccessLevelForSection) || accessLevel > maxAccessLevelForSection)
            {
                map[section] = accessLevel;
            }
            if (accessLevel > maxAccessLevel)
            {
                maxAccessLevel = accessLevel;
            }
        }

        void AddAccessLevel(IDictionary<int, short> map, short accessLevel, ref short maxAccessLevel, int section, IEnumerable<int> children)
        {
            // set the access level of the given section and all its children
            AddAccessLevel(map, accessLevel, ref maxAccessLevel, section);
            if (children != null)
            {
                foreach (var child in children)
                {
                    AddAccessLevel(map, accessLevel, ref maxAccessLevel, child);
                }
            }
        }

        void AddSection(int objectType, int section, IEnumerable<int> children)
        {
            // create the section set for the given object type if necessary
            if (!_sections.TryGetValue(objectType, out var sections))
            {
                _sections.Add(objectType, sections = new HashSet<int>());
            }

            // add the section and all its children
            sections.Add(section);
            if (children != null)
            {
                sections.UnionWith(children);
            }
        }

        SqlCommand BuildCommand(SqlConnection connection, string commandText, SqlParameter[] parameters)
        {
            // create the command object
            var command = new SqlCommand(commandText ?? throw new ArgumentNullException("commandText"), connection);
            if (parameters != null)
            {
                command.Parameters.AddRange(parameters);
            }
            return command;
        }

        internal int ExecuteNonQuery(VivendiSource source, string commandText, params SqlParameter[] parameters)
        {
            // run a INSERT, UPDATE or DELETE command and return the affected rows
            using (var connection = OpenConnection(source))
            {
                return BuildCommand(connection, commandText, parameters).ExecuteNonQuery();
            }
        }

        internal SqlDataReader ExecuteReader(VivendiSource source, string commandText, params SqlParameter[] parameters)
        {
            // return a reader that closes the connection once it's disposed
            return BuildCommand(OpenConnection(source), commandText, parameters).ExecuteReader(CommandBehavior.CloseConnection);
        }

        internal object ExecuteScalar(VivendiSource source, string commandText, params SqlParameter[] parameters)
        {
            // run a simple query
            using (var connection = OpenConnection(source))
            {
                return BuildCommand(connection, commandText, parameters).ExecuteScalar();
            }
        }

        internal int GetReadAccessLevel(IEnumerable<int> sections) => sections == null ? _maxReadAccessLevel : sections.Max(s => _readAccessLevels.TryGetValue(s, out var level) ? level : 0);

        internal IEnumerable<int> GetReadableSections(int objectType) => _sections.TryGetValue(objectType, out var sections) ? sections : Enumerable.Empty<int>();

        internal int GetWriteAccessLevel(IEnumerable<int> sections) => sections == null ? _maxWriteAccessLevel : sections.Max(s => _writeAccessLevels.TryGetValue(s, out var level) ? level : 0);

        internal IEnumerable<int> GetWritableSections(int objectType) => GetReadableSections(objectType);

        void InitializePermissions()
        {
            // query the user's permissions
            using
            (
                var reader = ExecuteReader
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
                )
            )
            {
                // build a map with all sections containg children
                var sectionChildren = new Dictionary<int, IEnumerable<int>>();
                while (reader.Read())
                {
                    sectionChildren.Add(reader.GetInt32("Parent"), reader.GetIDs("IDs").ToHashSet());
                }

                // get to the next query result
                if (reader.NextResult())
                {
                    // parse all permissions
                    while (reader.Read())
                    {
                        var section = reader.GetInt32Optional("Section") ?? 0;
                        sectionChildren.TryGetValue(section, out var children);
                        switch (reader.GetInt32("Permission"))
                        {
                            case 71: // Dateiablage
                                AddAccessLevel(_readAccessLevels, reader.GetInt16("Read"), ref _maxReadAccessLevel, section, children);
                                AddAccessLevel(_writeAccessLevels, reader.GetInt16("Write"), ref _maxWriteAccessLevel, section, children);
                                break;
                            case 1: // Mitarbeiter
                                AddSection(0, section, children);
                                break;
                            case 3: // Klient
                                AddSection(1, section, children);
                                break;
                        }
                    }
                }
            }
        }

        SqlConnection OpenConnection(VivendiSource source)
        {
            // get the corresponding connection string and open the connection
            if (!_connectionStrings.TryGetValue(source, out var connectionString))
            {
                throw new InvalidEnumArgumentException("source", (int)source, typeof(VivendiSource));
            }
            var connection = new SqlConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}
