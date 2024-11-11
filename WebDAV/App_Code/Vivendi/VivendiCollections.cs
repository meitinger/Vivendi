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
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace Aufbauwerk.Tools.Vivendi
{
    public abstract class VivendiCollection : VivendiResource
    {
        private const string DesktopIni = "desktop.ini";

        public static VivendiCollection CreateStaticRoot(FileAttributes attributes = FileAttributes.Normal) => new VivendiStaticCollection(null, string.Empty, attributes);

        private readonly DateTime? _creationDate;
        private readonly DateTime? _lastModified;
        private readonly IDictionary<string, VivendiResource> _resources = new Dictionary<string, VivendiResource>(Vivendi.PathComparer);
        private bool _showAll = false;

        internal VivendiCollection(VivendiCollection? parent, string name, DateTime? creationDate = null, DateTime? lastModified = null, string? localizedName = null)
        : base(parent, name, localizedName)
        {
            _creationDate = creationDate;
            _lastModified = lastModified;
        }

        public override FileAttributes Attributes => base.Attributes | FileAttributes.Directory | FileAttributes.ReadOnly;

        public IEnumerable<VivendiResource> Children
        {
            get
            {
                // return all readable children and build the desktop.ini file in the process
                EnsureCanRead();
                var children = new Dictionary<string, string>();
                foreach (var child in GetAllChildren())
                {
                    yield return child;
                    if (!(child is VivendiCollection) && child.LocalizedName != null)
                    {
                        children.Add(child.Name, child.LocalizedName);
                    }
                }
                yield return BuildDesktopIni(children);
            }
        }

        public override DateTime CreationDate
        {
            get
            {
                EnsureCanRead();
                return _creationDate ?? Parent?.CreationDate ?? DateTime.Now;
            }
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override string DisplayName
        {
            get
            {
                EnsureCanRead();
                return LocalizedName ?? Name;
            }
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override DateTime LastModified
        {
            get
            {
                EnsureCanRead();
                return _lastModified ?? Parent?.LastModified ?? CreationDate;
            }
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public bool ShowAll
        {
            get => SelfAndAncestors.OfType<VivendiCollection>().Any(c => c._showAll);
            set => _showAll = value;
        }

        private T Add<T>(T resource) where T : VivendiResource
        {
            // add and return the resource
            _resources.Add(resource.Name, resource);

            return resource;
        }

        public VivendiCollection AddBereiche(string name = "Bereich", string nullInstanceName = "(Alle Bereiche)") => Add(new VivendiObjectTypeCollection
        (
            parent: this,
            type: 4,
            displayName: name,
            nullInstanceName: nullInstanceName,
            allowInheritedAccess: true,
            protocolType: 0,
            queryId: @"[dbo].[MANDANT].[Z_MA]",
            queryWithoutSelect:
@"
    [Z_MA] AS [ID],
    [Bezeichnung] AS [Name],
    STR([Z_MA]) AS [Sections]
FROM [dbo].[MANDANT]
WHERE
    (@ID IS NULL OR @ID = [Z_MA])
    AND
    (@ShowAll = 1 OR [BegrenztBis] IS NULL OR [BegrenztBis] >= @Today)
"
        ));

        public VivendiCollection AddKlienten(string name = "Klient") => Add(new VivendiObjectTypeCollection
        (
            parent: this,
            type: 1,
            displayName: name,
            protocolType: 3,
            queryId: @"[dbo].[PFLEGEBED].[Z_PF]",
            queryWithoutSelect:
@"
    [dbo].[PFLEGEBED].[Z_PF] AS [ID],
    ISNULL([dbo].[PERSONEN].[Name], N'') + N', ' + ISNULL([dbo].[PERSONEN].[Vorname], N'') AS [Name],
    STRING_AGG([dbo].[MANDANTENZUORDNUNG].[iMandant], N', ') AS [Sections]
FROM
    [dbo].[PFLEGEBED]
    JOIN
    [dbo].[PERSONEN] ON [dbo].[PFLEGEBED].[iName] = [dbo].[PERSONEN].[Z_PE]
    JOIN
    [dbo].[MANDANTENZUORDNUNG] ON [dbo].[PFLEGEBED].[Z_PF] = [dbo].[MANDANTENZUORDNUNG].[iPflegebed]
WHERE
    (@ID IS NULL OR @ID = [dbo].[PFLEGEBED].[Z_PF])
    AND
    (
        @ShowAll = 1 OR
        (
            ([dbo].[PFLEGEBED].[Eintritt] IS NULL OR [dbo].[PFLEGEBED].[Eintritt] <= @Today)
            AND
            ([dbo].[PFLEGEBED].[Austritt] IS NULL OR [dbo].[PFLEGEBED].[Austritt] >= @Today)
            AND
            ([dbo].[MANDANTENZUORDNUNG].[AbDatum] IS NULL OR [dbo].[MANDANTENZUORDNUNG].[AbDatum] <= @Today)
            AND
            ([dbo].[MANDANTENZUORDNUNG].[BisDatum] IS NULL OR [dbo].[MANDANTENZUORDNUNG].[BisDatum] >= @Today)
        )
    )
GROUP BY
    [dbo].[PFLEGEBED].[Z_PF], [dbo].[PERSONEN].[Name], [dbo].[PERSONEN].[Vorname]
"
        ));

        public VivendiCollection AddMitarbeiter(string name = "Mitarbeiter") => Add(new VivendiObjectTypeCollection
        (
            parent: this,
            type: 0,
            displayName: name,
            allowInheritedAccess: true,
            protocolType: 1,
            queryId: @"[dbo].[MITARBEITER].[Z_MI]",
            queryWithoutSelect:
@"
    CONVERT(int, [dbo].[MITARBEITER].[Z_MI]) AS [ID],
    ISNULL([dbo].[PERSONEN].[Name], N'') + N', ' + ISNULL([dbo].[PERSONEN].[Vorname], N'') AS [Name],
    STRING_AGG([dbo].[MITARBEITER_BEREICH].[iMandant], N', ') AS [Sections]
FROM
    [dbo].[MITARBEITER]
    JOIN
    [dbo].[PERSONEN] ON [dbo].[MITARBEITER].[iName] = [dbo].[PERSONEN].[Z_PE]
    JOIN
    [dbo].[MITARBEITER_BEREICH] ON [dbo].[MITARBEITER].[Z_MI] = [dbo].[MITARBEITER_BEREICH].[iMitarbeiter]
WHERE
    (@ID IS NULL OR @ID = [dbo].[MITARBEITER].[Z_MI])
    AND
    (
        @ShowAll = 1 OR
        (
            ([dbo].[MITARBEITER].[Eintritt] IS NULL OR [dbo].[MITARBEITER].[Eintritt] <= @Today)
            AND
            ([dbo].[MITARBEITER].[Austritt] IS NULL OR [dbo].[MITARBEITER].[Austritt] >= @Today)
            AND
            ([dbo].[MITARBEITER_BEREICH].[AbDatum] IS NULL OR [dbo].[MITARBEITER_BEREICH].[AbDatum] <= @Today)
            AND
            ([dbo].[MITARBEITER_BEREICH].[BisDatum] IS NULL OR [dbo].[MITARBEITER_BEREICH].[BisDatum] >= @Today)
        )
    )
GROUP BY
    [dbo].[MITARBEITER].[Z_MI], [dbo].[PERSONEN].[Name], [dbo].[PERSONEN].[Vorname]
"
        ));

        public VivendiCollection AddStaticCollection(string name, FileAttributes attributes = FileAttributes.Normal) => Add(new VivendiStaticCollection(this, name, attributes));

        public VivendiDocument AddStaticDocument(string name, byte[] data, FileAttributes attributes = FileAttributes.Normal) => Add(new VivendiStaticDocument(this, name, attributes, data));

        public Vivendi AddVivendi(string name, string userName, IDictionary<VivendiSource, string> connectionStrings) => Add(new Vivendi(this, name, userName, connectionStrings));

        private VivendiDocument BuildDesktopIni(IDictionary<string, string> localizedNames)
        {
            // translate the segment into a name
            var builder = new StringBuilder();
            builder.Append("[LocalizedFileNames]").AppendLine();
            foreach (var localizedName in localizedNames)
            {
                builder.Append(localizedName.Key).Append("=").Append(localizedName.Value).AppendLine();
            }

            // translate the collection itself and return the document
            if (LocalizedName != null)
            {
                builder.Append("[.ShellClassInfo]").AppendLine();
                builder.Append("LocalizedResourceName=").Append(LocalizedName).AppendLine();
            }
            return new VivendiStaticDocument
            (
                parent: this,
                name: DesktopIni,
                attributes: FileAttributes.Hidden | FileAttributes.System,
                data: Encoding.Unicode.GetBytes(builder.ToString())
            );
        }

        private IEnumerable<VivendiResource> GetAllChildren(bool excludeCollections = false)
        {
            // filter out collections (if requested) and only include accessible resources
            var result = _resources.Values.AsEnumerable();
            if (excludeCollections)
            {
                result = result.Where(child => !(child is VivendiCollection));
            }
            return result.Concat(GetChildren(excludeCollections)).Where(r => r.InCollection);
        }

        public VivendiResource? GetChild(string name)
        {
            // ensure the collection is readable
            EnsureCanRead();

            // handle the desktop.ini first
            if (string.Equals(name, DesktopIni, Vivendi.PathComparison))
            {
                var children = new Dictionary<string, string>();
                foreach (var child in GetAllChildren(excludeCollections: true))
                {
                    if (child.LocalizedName != null)
                    {
                        children.Add(child.Name, child.LocalizedName);
                    }
                }
                return BuildDesktopIni(children);
            }

            // try to locate the resource
            if (!_resources.TryGetValue(name, out var resource))
            {
                if (TryParseTypeAndID(name, out var type, out var id))
                {
                    resource = GetChildByID(type, id);

                    // ensure the resource has the same extension
                    if (resource != null && !string.Equals(name, resource.Name, Vivendi.PathComparison))
                    {
                        resource = null;
                    }
                }
                else
                {
                    resource = GetChildByName(name);
                }
            }

            // ensure the resource is in the collection
            return resource?.InCollection ?? false ? resource : null;
        }

        protected virtual VivendiResource? GetChildByID(VivendiResourceType type, int id) => null;

        protected virtual VivendiResource? GetChildByName(string name) => null;

        protected virtual IEnumerable<VivendiResource> GetChildren(bool excludeCollections = false) => Enumerable.Empty<VivendiResource>();

        public virtual VivendiDocument NewDocument(string name, DateTime creationDate, DateTime lastModified, byte[] data) => throw VivendiException.DocumentNotAllowedInCollection();
    }

    internal sealed class VivendiStaticCollection : VivendiCollection
    {
        private readonly FileAttributes _attributes;
        private readonly DateTime _buildTime;

        internal VivendiStaticCollection(VivendiCollection? parent, string name, FileAttributes attributes)
        : base(parent, name)
        {
            _attributes = FileAttributes.ReadOnly | FileAttributes.Directory | (attributes & ~FileAttributes.Normal);
            _buildTime = DateTime.Now;
        }

        public override FileAttributes Attributes => _attributes;

        public override DateTime CreationDate => _buildTime;

        internal override bool InCollection => true;

        public override DateTime LastModified => _buildTime;
    }

    internal sealed class VivendiObjectInstanceCollection : VivendiCollection
    {
        internal VivendiObjectInstanceCollection(VivendiCollection parent, int? id, string displayName, DateTime? creationDate, DateTime? lastModified, IEnumerable<int>? sections)
        : base
        (
              parent: parent,
              name: FormatTypeAndId(VivendiResourceType.ObjectInstanceCollection, id ?? 0),
              creationDate: creationDate,
              lastModified: lastModified,
              localizedName: displayName
        )
        {
            SetObjectInstance(id, displayName);
            Sections = sections;
        }

        protected override VivendiResource? GetChildByID(VivendiResourceType type, int id) => type == VivendiResourceType.StoreCollection ? VivendiStoreCollection.QueryOne(this, id) : null;

        protected override IEnumerable<VivendiResource> GetChildren(bool excludeCollections) => excludeCollections ? Enumerable.Empty<VivendiResource>() : VivendiStoreCollection.QueryAll(this);
    }

    internal sealed class VivendiObjectTypeCollection : VivendiCollection
    {
        private readonly bool _allowInheritedAccess;
        private readonly VivendiObjectInstanceCollection? _nullInstance;
        private readonly int _protocolType;
        private readonly string _query;

        internal VivendiObjectTypeCollection(VivendiCollection parent, int type, string displayName, int protocolType, string queryId, string queryWithoutSelect, bool allowInheritedAccess = true, string? nullInstanceName = null)
        : base
        (
              parent: parent,
              name: FormatTypeAndId(VivendiResourceType.ObjectTypeCollection, type),
              localizedName: displayName
        )
        {
            ObjectType = type;
            _allowInheritedAccess = allowInheritedAccess;
            if (nullInstanceName != null)
            {
                _nullInstance = new VivendiObjectInstanceCollection
                (
                    parent: this,
                    id: null,
                    displayName: nullInstanceName,
                    creationDate: null,
                    lastModified: null,
                    sections: null
                );
            }
            _protocolType = protocolType;
            var middlePart = Invariant($@" FROM [dbo].[PROTOKOLL] WHERE [ZielTabelle] = {protocolType} AND [ZielIndex] = {queryId} AND [Vorgang] = ");
            _query = Invariant
            (
$@"
SELECT
    (SELECT MIN([Systemzeit]){middlePart}0) AS [CreationDate],
    (SELECT MAX([Systemzeit]){middlePart}1) AS [LastModified],{queryWithoutSelect}
"
            );
        }

        public override DateTime CreationDate
        {
            get
            {
                EnsureCanRead();
                return QueryProtocol("MIN", 0) ?? base.CreationDate;
            }
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override DateTime LastModified
        {
            get
            {
                EnsureCanRead();
                return QueryProtocol("MAX", 1) ?? base.LastModified;
            }
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        protected override VivendiResource? GetChildByID(VivendiResourceType type, int id) => type == VivendiResourceType.ObjectInstanceCollection ? id == 0 ? _nullInstance : Query(id).SingleOrDefault() : null;

        protected override IEnumerable<VivendiResource> GetChildren(bool excludeCollections)
        {
            if (excludeCollections)
            {
                return Enumerable.Empty<VivendiResource>();
            }

            // fetch all objects of this collection's type and optionally append the any instance
            var children = Query();
            if (_nullInstance != null)
            {
                children = children.Append(_nullInstance);
            }
            return children;
        }

        private IEnumerable<VivendiObjectInstanceCollection> Query(int? id = null)
        {
            using var reader = Vivendi.ExecuteReader
            (
                VivendiSource.Data,
                _query,
                new SqlParameter("ID", (object?)id ?? DBNull.Value),
                new SqlParameter("ShowAll", ShowAll),
                new SqlParameter("Today", DateTime.Today)
            );
            while (reader.Read())
            {
                var sections = reader.GetIDs("Sections");
                if (_allowInheritedAccess)
                {
                    sections = sections.SelectMany(section => Vivendi.ExpandSection(section));
                }
                yield return new VivendiObjectInstanceCollection
                (
                    parent: this,
                    id: reader.GetInt32("ID"),
                    displayName: reader.GetString("Name"),
                    creationDate: reader.GetDateTimeOptional("CreationDate"),
                    lastModified: reader.GetDateTimeOptional("LastModified"),
                    sections: sections
                );
            }
        }

        private DateTime? QueryProtocol(string aggregate, int operation)
        {
            var dt = Vivendi.ExecuteScalar(VivendiSource.Data, Invariant($@"SELECT {aggregate}([Systemzeit]) FROM [dbo].[PROTOKOLL] WHERE [ZielTabelle] = {_protocolType} AND [Vorgang] = {operation}"));
            return dt != DBNull.Value ? (DateTime?)dt : null;
        }
    }

    internal sealed class VivendiStoreCollection : VivendiCollection
    {
        private static IEnumerable<VivendiStoreCollection> Query(VivendiCollection parent, int? id = null)
        {
            if (!parent.HasObjectInstance)
            {
                throw new ArgumentException("No object instance has been set on the parent collection.", nameof(parent));
            }
            using var reader = parent.Vivendi.ExecuteReader
            (
                VivendiSource.Store,
@"
WITH [HIERARCHY]([ID], [Parent]) AS
(
    SELECT [Z_DAT], [Z_Parent_DAT]
    FROM [dbo].[DATEI_ABLAGE_TYP]
    UNION ALL
    SELECT [dbo].[DATEI_ABLAGE_TYP].[Z_DAT], [HIERARCHY].[Parent]
    FROM [dbo].[DATEI_ABLAGE_TYP] JOIN [HIERARCHY] ON [dbo].[DATEI_ABLAGE_TYP].[Z_Parent_DAT] = [HIERARCHY].[ID]
)
SELECT
    [Z_DAT] AS [ID],
    [Bezeichnung] AS [Name],
    (
        SELECT MAX(ISNULL([dbo].[DATEI_ABLAGE].[GeaendertDatum],[dbo].[DATEI_ABLAGE].[Dateidatum]))
        FROM [dbo].[DATEI_ABLAGE]
        WHERE
            [dbo].[DATEI_ABLAGE].[ZielTabelle1] = @TargetTable AND
            (@TargetIndex IS NULL AND [dbo].[DATEI_ABLAGE].[ZielIndex1] IS NULL OR [dbo].[DATEI_ABLAGE].[ZielIndex1] = @TargetIndex) AND
            (
                [dbo].[DATEI_ABLAGE].[iDokumentArt] = [dbo].[DATEI_ABLAGE_TYP].[Z_DAT] OR
                [dbo].[DATEI_ABLAGE].[iDokumentArt] IN (SELECT [ID] FROM [HIERARCHY] WHERE [HIERARCHY].[Parent] = [dbo].[DATEI_ABLAGE_TYP].[Z_DAT])
            )
    ) AS [LastModified],
    [Bereiche] AS [Sections],
    [lMaxSize] * 1024 AS [MaxDocumentSize],
    [lRechteLevel] AS [AccessLevel],
    CASE WHEN [SperrfristActive] = 1 THEN [Sperrfrist] ELSE NULL END AS [LockAfterMonths],
    CONVERT(bit, [bRevisionssicher]) AS [RetainRevisions]
FROM [dbo].[DATEI_ABLAGE_TYP]
WHERE
    (@ID IS NULL OR [Z_DAT] = @ID) AND                                                   -- query all or just a single collection
    ([Z_Parent_DAT] IS NULL AND @Parent IS NULL OR [Z_Parent_DAT] = @Parent) AND         -- query the root or sub collections
    [Zielanwendung] = 0 AND                                                              -- Vivendi NG
    ([ZielTabelle] = -2 OR [ZielTabelle] = @TargetTable) AND                             -- query the proper type
    [SeriendruckVorlage] IS NULL AND                                                     -- no reports
    [bZippen] = 0                                                                        -- no zipped collections (because not reproducable in Vivendi)
",
                new SqlParameter("ID", (object?)id ?? DBNull.Value),
                new SqlParameter("TargetTable", parent.ObjectType),
                new SqlParameter("TargetIndex", (object?)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("Parent", parent is VivendiStoreCollection storeCollection ? (object?)storeCollection.ID : DBNull.Value)
            );
            while (reader.Read())
            {
                yield return new VivendiStoreCollection
                (
                    parent: parent,
                    id: reader.GetInt32("ID"),
                    displayName: reader.GetString("Name"),
                    lastModified: reader.GetDateTimeOptional("LastModified"),
                    sections: reader.GetIDsOptional("Sections"),
                    maxDocumentSize: reader.GetInt32Optional("MaxDocumentSize"),
                    accessLevel: reader.GetInt32Optional("AccessLevel") ?? 0,
                    lockAfterMonths: reader.GetInt32Optional("LockAfterMonths"),
                    retainRevisions: reader.GetBoolean("RetainRevisions")
                );
            }
        }

        internal static IEnumerable<VivendiStoreCollection> QueryAll(VivendiCollection parent) => Query(parent);

        internal static VivendiStoreCollection QueryOne(VivendiCollection parent, int id) => Query(parent, id).SingleOrDefault();

        private VivendiStoreCollection(VivendiCollection parent, int id, string displayName, DateTime? lastModified, IEnumerable<int>? sections, int? maxDocumentSize, int accessLevel, int? lockAfterMonths, bool retainRevisions)
        : base
        (
              parent: parent,
              name: FormatTypeAndId(VivendiResourceType.StoreCollection, id),
              lastModified: lastModified,
              localizedName: displayName
        )
        {
            ID = id;
            LockAfterMonths = lockAfterMonths;
            MaxDocumentSize = maxDocumentSize == 0 ? null : maxDocumentSize;
            RequiredAccessLevel = accessLevel;
            RetainRevisions = retainRevisions;
            if (sections != null)
            {
                var result = new HashSet<int>();
                foreach (var section in sections)
                {
                    if (section < 0)
                    {
                        result.ExceptWith(Vivendi.ExpandSection(-section));
                    }
                    else
                    {
                        result.UnionWith(Vivendi.ExpandSection(section));
                    }
                }
                Sections = result;
            }
        }

        public int ID { get; }

        public int? LockAfterMonths { get; }

        public int? MaxDocumentSize { get; }

        public bool RetainRevisions { get; }

        protected override VivendiResource? GetChildByID(VivendiResourceType type, int id) => type switch
        {
            VivendiResourceType.StoreDocument => VivendiStoreDocument.QueryByID(this, id),
            VivendiResourceType.StoreCollection => VivendiStoreCollection.QueryOne(this, id),
            _ => null,
        };

        protected override VivendiResource? GetChildByName(string name) => VivendiStoreDocument.QueryByName(this, name);

        protected override IEnumerable<VivendiResource> GetChildren(bool excludeCollections)
        {
            var result = Enumerable.Empty<VivendiResource>();
            if (!excludeCollections)
            {
                result = result.Concat(VivendiStoreCollection.QueryAll(this));
            }
            return result.Concat(VivendiStoreDocument.QueryAll(this));
        }

        public override VivendiDocument NewDocument(string name, DateTime creationDate, DateTime lastModified, byte[] data) => VivendiStoreDocument.Create(this, name, creationDate, lastModified, data);
    }
}
