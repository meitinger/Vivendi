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
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace Aufbauwerk.Tools.Vivendi
{
    public abstract class VivendiCollection : VivendiResource
    {
        private const string DesktopIni = "desktop.ini";

        private readonly IDictionary<string, VivendiResource> _namedResources = new Dictionary<string, VivendiResource>(Vivendi.PathComparer);
        private bool _showAll = false;
        private readonly IDictionary<VivendiResourceType, IDictionary<int, VivendiResource>> _typedResources = new Dictionary<VivendiResourceType, IDictionary<int, VivendiResource>>();

        internal VivendiCollection(VivendiCollection parent, VivendiResourceType type, int id, string name)
        : base(parent, type, id, name)
        { }

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
            get => GetDate(@"MIN([Dateidatum])");
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
            get => GetDate(@"MAX(ISNULL([GeaendertDatum],[Dateidatum]))");
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
            this,
            4,
            name,
            @"
SELECT
    [Z_MA] AS [ID],
    [Bezeichnung] AS [Name],
    NULL AS [Sections]
FROM [dbo].[MANDANT]
WHERE
    (@ID IS NULL OR @ID = [dbo].[MANDANT].[Z_MA])
    AND
    (@ShowAll = 1 OR [BegrenztBis] IS NULL OR [BegrenztBis] >= @Today)
",
            nullInstanceName
        ));

        public VivendiCollection AddKlienten(string name = "Klient") => Add(new VivendiObjectTypeCollection
        (
            this,
            1,
            name,
            @"
SELECT
    [dbo].[PFLEGEBED].[Z_PF] AS [ID],
    [dbo].[PERSONEN].[Name] + ', ' + [dbo].[PERSONEN].[Vorname] AS [Name],
    STRING_AGG([dbo].[MANDANTENZUORDNUNG].[iMandant], ', ') AS [Sections]
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
            this,
            0,
            name,
            @"
SELECT
    CONVERT(int, [dbo].[MITARBEITER].[Z_MI]) AS [ID],
    [dbo].[PERSONEN].[Name] + ', ' + [dbo].[PERSONEN].[Vorname] AS [Name],
    STRING_AGG([dbo].[MITARBEITER_BEREICH].[iMandant], ', ') AS [Sections]
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

        public VivendiCollection AddStaticCollection(string name, FileAttributes attributes = FileAttributes.Directory) => Add(new VivendiStaticCollection(this, name, attributes));

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
            return new VivendiStaticDocument(this, DesktopIni, FileAttributes.Hidden | FileAttributes.System, Encoding.Unicode.GetBytes(builder.ToString()));
        }

        public VivendiResource GetChild(string name)
        {
            // check the name and ensure the collection is readable
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            EnsureCanRead();

            // handle the desktop.ini first
            if (string.Equals(name, DesktopIni, Vivendi.PathComparison))
            {
                return BuildDesktopIni(ChildrenWithoutDesktopIni.Where(c => c.Type != VivendiResourceType.Named && !(c is VivendiCollection)).ToDictionary(r => r.Name, r => r.LocalizedName));
            }

            // check what type of path we have got
            VivendiResource resource;
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
            return
                resource != null &&
                resource.InCollection &&
                string.Equals(name, resource.Name, Vivendi.PathComparison)
                ? resource
                : null;
        }

        protected virtual VivendiResource GetChildByName(string name) => null;

        protected virtual VivendiResource GetChildByID(VivendiResourceType type, int id) => null;

        protected virtual IEnumerable<VivendiResource> GetChildren() => Enumerable.Empty<VivendiResource>();

        private DateTime GetDate(string select)
        {
            // query a date over all documents with the given target table and index, if any
            EnsureCanRead();
            var query = new StringBuilder(@"SELECT ").Append(select).Append(@" FROM [dbo].[DATEI_ABLAGE]");
            var parameters = new List<SqlParameter>(2);
            if (HasObjectType)
            {
                query.Append(@" WHERE [ZielTabelle1] = @TargetTable");
                parameters.Add(new SqlParameter("TargetTable", (int)ObjectType));
                if (HasObjectInstance)
                {
                    query.Append(@" AND ([ZielIndex1] IS NULL AND @TargetIndex IS NULL OR [ZielIndex1] = @TargetIndex)");
                    parameters.Add(new SqlParameter("TargetIndex", (object)ObjectID ?? DBNull.Value));
                }
            }
            var lastModified = Vivendi.ExecuteScalar(VivendiSource.Store, query.ToString(), parameters.ToArray());
            return lastModified == DBNull.Value ? DateTime.MinValue : (DateTime)lastModified;
        }

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
            _attributes = FileAttributes.ReadOnly | FileAttributes.System | (attributes & ~FileAttributes.Normal);
        }

        public override FileAttributes Attributes
        {
            get => _attributes;
            set => base.Attributes = value;
        }

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
        internal VivendiObjectInstanceCollection(VivendiCollection parent, int? id, string name, IEnumerable<int> sections)
        : base(parent, VivendiResourceType.ObjectInstanceCollection, id ?? 0, name)
        {
            SetObjectInstace(id, name);
            Sections = sections;
        }

        protected override VivendiResource GetChildByID(VivendiResourceType type, int id) => type == VivendiResourceType.StoreCollection ? VivendiStoreCollection.QueryOne(this, id) : null;

        protected override IEnumerable<VivendiResource> GetChildren() => VivendiStoreCollection.QueryAll(this);
    }

    internal sealed class VivendiObjectTypeCollection : VivendiCollection
    {
        private readonly VivendiObjectInstanceCollection _nullInstance;
        private readonly string _query;

        internal VivendiObjectTypeCollection(VivendiCollection parent, int type, string name, string query)
        : base(parent, VivendiResourceType.ObjectTypeCollection, type, name)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            ObjectType = type;
        }

        internal VivendiObjectTypeCollection(VivendiCollection parent, int type, string name, string query, string nullInstanceName)
        : this(parent, type, name, query)
        {
            _nullInstance = new VivendiObjectInstanceCollection(this, null, nullInstanceName ?? throw new ArgumentNullException(nameof(nullInstanceName)), null);
        }

        private IEnumerable<VivendiObjectInstanceCollection> Query(object id)
        {
            using var reader = Vivendi.ExecuteReader(VivendiSource.Data, _query, new SqlParameter("ID", id), new SqlParameter("ShowAll", ShowAll), new SqlParameter("Today", DateTime.Today));
            while (reader.Read())
            {
                yield return new VivendiObjectInstanceCollection
                (
                    this,
                    reader.GetInt32("ID"),
                    reader.GetString("Name"),
                    reader.GetIDsOptional("Sections")
                );
            }
        }

        protected override VivendiResource GetChildByID(VivendiResourceType type, int id) => type == VivendiResourceType.ObjectInstanceCollection ? id == 0 ? _nullInstance : Query(id).SingleOrDefault() : null;

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
            if (!(parent ?? throw new ArgumentNullException(nameof(parent))).HasObjectInstance)
            {
                throw new ArgumentException("No object instance has been set on the parent collection.", nameof(parent));
            }
            using var reader = parent.Vivendi.ExecuteReader
            (
                VivendiSource.Store,
                    @"
SELECT
    [Z_DAT] AS [ID],
    [Bezeichnung] AS [Name],
    [lMaxSize] * 1024 AS [MaxDocumentSize],
    [lRechteLevel] AS [AccessLevel],
    [Bereiche] AS [Sections],
    CASE WHEN [SperrfristActive] = 1 THEN [Sperrfrist] ELSE NULL END AS [LockAfterMonths]
FROM [dbo].[DATEI_ABLAGE_TYP]
WHERE
    (@ID IS NULL OR [Z_DAT] = @ID) AND                                           -- query all or just a single collection
    ([Z_Parent_DAT] IS NULL AND @Parent IS NULL OR [Z_Parent_DAT] = @Parent) AND -- query the root or sub collections
    [Zielanwendung] = 0 AND                                                      -- Vivendi NG
    ([ZielTabelle] IS NULL OR [ZielTabelle] = @ObjectType) AND                   -- query the proper type
    [SeriendruckVorlage] IS NULL AND                                             -- no reports
    [bZippen] = 0 AND                                                            -- no zipped collections (because not reproducable in Vivendi)
    [DmsSuchkontext] IS NULL                                                     -- no external archives
",
                new SqlParameter("ID", id),
                new SqlParameter("ObjectType", parent.ObjectType),
                new SqlParameter("Parent", parent is VivendiStoreCollection ? (object)parent.ID : DBNull.Value)
            );
            while (reader.Read())
            {
                yield return new VivendiStoreCollection
                (
                    parent,
                    reader.GetInt32("ID"),
                    reader.GetString("Name"),
                    reader.GetInt32Optional("MaxDocumentSize"),
                    reader.GetInt32Optional("AccessLevel") ?? 0,
                    reader.GetIDsOptional("Sections"),
                    reader.GetInt32Optional("LockAfterMonths")
                );
            }
        }

        internal static IEnumerable<VivendiStoreCollection> QueryAll(VivendiCollection parent) => Query(parent, DBNull.Value);

        internal static VivendiStoreCollection QueryOne(VivendiCollection parent, int id) => Query(parent, id).SingleOrDefault();

        private VivendiStoreCollection(VivendiCollection parent, int id, string name, int? maxDocumentSize, int accessLevel, IEnumerable<int> sections, int? lockAfterMonths)
        : base(parent, VivendiResourceType.StoreCollection, id, name)
        {
            MaxDocumentSize = maxDocumentSize == 0 ? null : maxDocumentSize;
            RequiredAccessLevel = accessLevel;
            Sections = sections;
            LockAfterMonths = lockAfterMonths;
        }

        public int? LockAfterMonths { get; }

        public int? MaxDocumentSize { get; }

        protected override VivendiResource GetChildByName(string name) => VivendiStoreDocument.QueryByName(this, name);

        protected override VivendiResource GetChildByID(VivendiResourceType type, int id) => type switch
        {
            VivendiResourceType.StoreDocument => VivendiStoreDocument.QueryByID(this, id),
            VivendiResourceType.StoreCollection => VivendiStoreCollection.QueryOne(this, id),
            _ => null,
        };

        protected override IEnumerable<VivendiResource> GetChildren() => Enumerable.Empty<VivendiResource>().Concat(VivendiStoreDocument.QueryAll(this)).Concat(VivendiStoreCollection.QueryAll(this));

        public override VivendiDocument NewDocument(string name, DateTime creationDate, DateTime lastModified, byte[] data) => VivendiStoreDocument.Create(this, name, creationDate, lastModified, data);
    }
}
