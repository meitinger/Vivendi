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
using System.Security.Principal;
using System.Text;
using static System.FormattableString;

namespace Aufbauwerk.Tools.Vivendi
{
    public abstract class VivendiCollection : VivendiResource
    {
        private const string DesktopIni = "desktop.ini";

        private readonly DateTime? _creationDate;
        private readonly DateTime? _lastModified;
        private readonly IDictionary<string, VivendiResource> _namedResources = new Dictionary<string, VivendiResource>(Vivendi.PathComparer);
        private bool _showAll = false;
        private readonly IDictionary<VivendiResourceType, IDictionary<int, VivendiResource>> _typedResources = new Dictionary<VivendiResourceType, IDictionary<int, VivendiResource>>();

        internal VivendiCollection(VivendiCollection? parent, VivendiResourceType type, int id, string name, DateTime? creationDate = null, DateTime? lastModified = null)
        : base(parent, type, id, name)
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
                foreach (var child in ChildrenWithoutDesktopIni)
                {
                    yield return child;
                    if (child.Type != VivendiResourceType.Named && !(child is VivendiCollection))
                    {
                        children.Add(child.Name, child.LocalizedName);
                    }
                }
                yield return BuildDesktopIni(children);
            }
        }

        private IEnumerable<VivendiResource> ChildrenWithoutDesktopIni => _namedResources.Values.Concat(_typedResources.Values.SelectMany(d => d.Values)).Concat(GetChildren()).Where(r => r.InCollection);

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
                return LocalizedName;
            }
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override DateTime LastModified
        {
            get
            {
                EnsureCanRead();
                return _lastModified ?? _creationDate ?? Parent?.LastModified ?? DateTime.Now;
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
            if (resource.Type == VivendiResourceType.Named)
            {
                _namedResources.Add(resource.Name, resource);
            }
            else
            {
                if (!_typedResources.TryGetValue(resource.Type, out var idResources))
                {
                    _typedResources.Add(resource.Type, idResources = new Dictionary<int, VivendiResource>());
                }
                idResources.Add(resource.ID, resource);
            }
            return resource;
        }

        public VivendiCollection AddBereiche(string name = "Bereich", string nullInstanceName = "(Alle Bereiche)") => Add(new VivendiObjectTypeCollection
        (
            parent: this,
            type: 4,
            name: name,
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
            name: name,
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
            name: name,
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

        public Vivendi AddNestedVivendi(string name, string userName, SecurityIdentifier userSid, IDictionary<VivendiSource, string> connectionStrings) => Add(new Vivendi(this, name, userName, userSid, connectionStrings));

        public VivendiCollection AddStaticCollection(string name, FileAttributes attributes = FileAttributes.Normal) => Add(new VivendiStaticCollection(this, name, attributes));

        public VivendiDocument AddStaticDocument(string name, byte[] data, FileAttributes attributes = FileAttributes.Normal) => Add(new VivendiStaticDocument(this, name, attributes, data));

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
            if (Type != VivendiResourceType.Named)
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

        public VivendiResource? GetChild(string name)
        {
            // ensure the collection is readable
            EnsureCanRead();

            // handle the desktop.ini first
            if (string.Equals(name, DesktopIni, Vivendi.PathComparison))
            {
                return BuildDesktopIni(ChildrenWithoutDesktopIni.Where(c => c.Type != VivendiResourceType.Named && !(c is VivendiCollection)).ToDictionary(r => r.Name, r => r.LocalizedName));
            }

            // check what type of path we have got
            VivendiResource? resource;
            if (TryParseTypeAndID(name, out var type, out var id))
            {
                // find typed resources
                if (!_typedResources.TryGetValue(type, out var idResources) || !idResources.TryGetValue(id, out resource))
                {
                    resource = GetChildByID(type, id);
                }
            }
            else
            {
                // find named resources
                if (!_namedResources.TryGetValue(name, out resource))
                {
                    resource = GetChildByName(name);
                }
            }

            // ensure the resource is in the collection and has the same extension
            return resource != null && resource.InCollection && string.Equals(name, resource.Name, Vivendi.PathComparison) ? resource : null;
        }

        protected virtual VivendiResource? GetChildByName(string name) => null;

        protected virtual VivendiResource? GetChildByID(VivendiResourceType type, int id) => null;

        protected virtual IEnumerable<VivendiResource> GetChildren() => Enumerable.Empty<VivendiResource>();

        public virtual VivendiDocument NewDocument(string name, DateTime creationDate, DateTime lastModified, byte[] data) => throw VivendiException.DocumentNotAllowedInCollection();
    }

    internal sealed class VivendiStaticCollection : VivendiCollection
    {
        private readonly FileAttributes _attributes;
        private readonly DateTime _buildTime;

        internal VivendiStaticCollection(VivendiCollection parent, string name, FileAttributes attributes)
        : base(parent, VivendiResourceType.Named, 0, name)
        {
            _buildTime = DateTime.Now;
            _attributes = FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Directory | (attributes & ~FileAttributes.Normal);
        }

        public override FileAttributes Attributes => _attributes;

        public override DateTime CreationDate
        {
            get => _buildTime;
            set => throw VivendiException.ResourceIsStatic();
        }

        internal override bool InCollection => true;

        public override DateTime LastModified
        {
            get => _buildTime;
            set => throw VivendiException.ResourceIsStatic();
        }
    }

    internal sealed class VivendiObjectInstanceCollection : VivendiCollection
    {
        internal VivendiObjectInstanceCollection(VivendiCollection parent, int? id, string name, DateTime? creationDate, DateTime? lastModified, IEnumerable<int>? sections)
        : base(parent, VivendiResourceType.ObjectInstanceCollection, id ?? 0, name, creationDate, lastModified)
        {
            SetObjectInstance(id, name);
            Sections = sections;
        }

        protected override VivendiResource? GetChildByID(VivendiResourceType type, int id) => type == VivendiResourceType.StoreCollection ? VivendiStoreCollection.QueryOne(this, id) : null;

        protected override IEnumerable<VivendiResource> GetChildren() => VivendiStoreCollection.QueryAll(this);
    }

    internal sealed class VivendiObjectTypeCollection : VivendiCollection
    {
        private readonly bool _allowInheritedAccess;
        private readonly VivendiObjectInstanceCollection? _nullInstance;
        private readonly int _protocolType;
        private readonly string _query;

        internal VivendiObjectTypeCollection(VivendiCollection parent, int type, string name, int protocolType, string queryId, string queryWithoutSelect, bool allowInheritedAccess = true, string? nullInstanceName = null)
        : base(parent, VivendiResourceType.ObjectTypeCollection, type, name)
        {
            ObjectType = type;
            _allowInheritedAccess = allowInheritedAccess;
            _protocolType = protocolType;
            if (nullInstanceName != null)
            {
                _nullInstance = new VivendiObjectInstanceCollection
                (
                    parent: this,
                    id: null,
                    name: nullInstanceName,
                    creationDate: null,
                    lastModified: null,
                    sections: null
                );
            }
            var middlePart = Invariant($@" FROM [dbo].[PROTOKOLL] WHERE [ZielTabelle] = {protocolType} AND [ZielIndex] = {queryId} AND [Vorgang] = ");
            _query = Invariant($@"SELECT
    (SELECT [Systemzeit]{middlePart}0) AS [CreationDate],
    (SELECT MAX([Systemzeit]){middlePart}1) AS [LastModified],{queryWithoutSelect}");
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

        private IEnumerable<VivendiObjectInstanceCollection> Query(object id)
        {
            using var reader = Vivendi.ExecuteReader(VivendiSource.Data, _query, new SqlParameter("ID", id), new SqlParameter("ShowAll", ShowAll), new SqlParameter("Today", DateTime.Today));
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
                    name: reader.GetString("Name"),
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

        protected override VivendiResource? GetChildByID(VivendiResourceType type, int id) => type == VivendiResourceType.ObjectInstanceCollection ? id == 0 ? _nullInstance : Query(id).SingleOrDefault() : null;

        protected override IEnumerable<VivendiResource> GetChildren()
        {
            // fetch all objects of this collection's type and optionally append the any instance
            var children = Query(DBNull.Value);
            if (_nullInstance != null)
            {
                children = children.Append(_nullInstance);
            }
            return children;
        }
    }

    internal sealed class VivendiStoreCollection : VivendiCollection
    {
        private static IEnumerable<VivendiStoreCollection> Query(VivendiCollection parent, object id)
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
                new SqlParameter("ID", id),
                new SqlParameter("TargetTable", parent.ObjectType),
                new SqlParameter("TargetIndex", (object?)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("Parent", parent is VivendiStoreCollection ? (object?)parent.ID : DBNull.Value)
            );
            while (reader.Read())
            {
                yield return new VivendiStoreCollection
                (
                    parent: parent,
                    id: reader.GetInt32("ID"),
                    name: reader.GetString("Name"),
                    lastModified: reader.GetDateTimeOptional("LastModified"),
                    sections: reader.GetIDsOptional("Sections"),
                    maxDocumentSize: reader.GetInt32Optional("MaxDocumentSize"),
                    accessLevel: reader.GetInt32Optional("AccessLevel") ?? 0,
                    lockAfterMonths: reader.GetInt32Optional("LockAfterMonths"),
                    retainRevisions: reader.GetBoolean("RetainRevisions")
                );
            }
        }

        internal static IEnumerable<VivendiStoreCollection> QueryAll(VivendiCollection parent) => Query(parent, DBNull.Value);

        internal static VivendiStoreCollection QueryOne(VivendiCollection parent, int id) => Query(parent, id).SingleOrDefault();

        private VivendiStoreCollection(VivendiCollection parent, int id, string name, DateTime? lastModified, IEnumerable<int>? sections, int? maxDocumentSize, int accessLevel, int? lockAfterMonths, bool retainRevisions)
        : base(parent, VivendiResourceType.StoreCollection, id, name, lastModified: lastModified)
        {
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
            LockAfterMonths = lockAfterMonths;
            MaxDocumentSize = maxDocumentSize == 0 ? null : maxDocumentSize;
            RequiredAccessLevel = accessLevel;
            RetainRevisions = retainRevisions;
        }

        public int? LockAfterMonths { get; }

        public int? MaxDocumentSize { get; }

        public bool RetainRevisions { get; }

        protected override VivendiResource? GetChildByName(string name) => VivendiStoreDocument.QueryByName(this, name);

        protected override VivendiResource? GetChildByID(VivendiResourceType type, int id) => type switch
        {
            VivendiResourceType.StoreDocument => VivendiStoreDocument.QueryByID(this, id),
            VivendiResourceType.StoreCollection => VivendiStoreCollection.QueryOne(this, id),
            _ => null,
        };

        protected override IEnumerable<VivendiResource> GetChildren() => Enumerable.Empty<VivendiResource>().Concat(VivendiStoreDocument.QueryAll(this)).Concat(VivendiStoreCollection.QueryAll(this));

        public override VivendiDocument NewDocument(string name, DateTime creationDate, DateTime lastModified, byte[] data) => VivendiStoreDocument.Create(this, name, creationDate, lastModified, data);
    }
}
