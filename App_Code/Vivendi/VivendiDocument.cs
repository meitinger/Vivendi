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
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Web;

namespace Aufbauwerk.Tools.Vivendi
{
    public abstract class VivendiDocument : VivendiResource
    {
        internal VivendiDocument(VivendiCollection parent, VivendiResourceType type, int id, string name)
        : base(parent, type, id, name)
        { }

        public virtual string ContentType => MimeMapping.GetMimeMapping(Name);

        public abstract byte[] Data { get; set; }

        public abstract int Size { get; }

        public abstract void Delete();

        public abstract void MoveTo(VivendiCollection destCollection, string destName);
    }

    internal sealed class VivendiStaticDocument : VivendiDocument
    {
        private readonly FileAttributes _attributes;
        private readonly DateTime _buildTime;
        private readonly byte[] _data;

        internal VivendiStaticDocument(VivendiCollection parent, string name, FileAttributes attributes, byte[] data)
        : base(parent, VivendiResourceType.Named, 0, name)
        {
            _buildTime = DateTime.Now;
            _attributes = FileAttributes.ReadOnly | (attributes & ~FileAttributes.Normal);
            _data = data ?? throw new ArgumentNullException(nameof(data));
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

        public override byte[] Data
        {
            get => _data;
            set => throw VivendiException.ResourceIsStatic();
        }

        public override string DisplayName
        {
            get => Name;
            set => throw VivendiException.ResourceIsStatic();
        }

        internal override bool InCollection => true;

        public override DateTime LastModified
        {
            get => _buildTime;
            set => throw VivendiException.ResourceIsStatic();
        }

        public override int Size => _data.Length;

        public override void Delete() => throw VivendiException.ResourceIsStatic();

        public override void MoveTo(VivendiCollection destCollection, string destName) => throw VivendiException.ResourceIsStatic();
    }

    internal sealed class VivendiStoreDocument : VivendiDocument
    {
        private const string WEBDAV_PREFIX = "webdav:v1:";

        private struct Template
        {
            public Template(VivendiStoreCollection parent, int id, bool additionalTargets, int? section, SecurityIdentifier owner, string displayName, DateTime creationDate, DateTime lastModified, int size, DateTime? lockDate, bool signed)
            {
                // initialize the template
                Parent = parent;
                ID = id;
                AdditionalTargets = additionalTargets;
                Section = section;
                Owner = owner;
                DisplayName = displayName;
                CreationDate = creationDate;
                LastModified = lastModified;
                Size = size;
                LockDate = lockDate;
                Signed = signed;
            }

            public readonly VivendiStoreCollection Parent;
            public readonly int ID;
            public readonly bool AdditionalTargets;
            public readonly int? Section;
            public readonly SecurityIdentifier Owner;
            public readonly string DisplayName;
            public readonly DateTime CreationDate;
            public readonly DateTime LastModified;
            public readonly int Size;
            public readonly DateTime? LockDate;
            public readonly bool Signed;
        }

        internal static VivendiStoreDocument Create(VivendiStoreCollection parent, string name, DateTime creationDate, DateTime lastModified, byte[] data)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            EnsureValidName(ref name);
            parent.EnsureCanWrite();
            EnsureSize(parent.MaxDocumentSize, data.Length);
            var owner = parent.Vivendi.UserSid;
            var lockDate = !parent.LockAfterMonths.HasValue ? (DateTime?)null : DateTime.Now.Date.AddMonths(parent.LockAfterMonths.Value);
            var id = new SqlParameter("ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            parent.Vivendi.ExecuteNonQuery
            (
                VivendiSource.Store,
@"
SELECT @ID = MAX([Z_DA]) + 1
FROM [dbo].[DATEI_ABLAGE];
INSERT INTO [dbo].[DATEI_ABLAGE]
(
    [Z_DA],
    [iDokumentArt],
    [ZielIndex1],
    [ZielTabelle1],
    [ZielTabelle2],
    [ZielTabelle3],
    [Speicherort],
    [Dateiname],
    [ZielBeschreibung],
    [Dateidatum],
    [ErstelltVon],
    [bGeZippt],
    [GeaendertDatum],
    [BelegDatum],
    [pDateiBlob],
    [Sperrdatum],
    [bUnterschrieben],
    [CloudTyp],
    [bImportiert]
)
VALUES
(
    @ID,
    @Parent,
    @TargetIndex,
    @TargetTable,
    -2,
    -2,
    @Location,
    @Name,
    @TargetDescription,
    @CreationDate,
    @UserName,
    0,
    CASE WHEN @CreationDate = @LastModified THEN NULL ELSE @LastModified END,
    CAST(@CreationDate AS date),
    @Blob,
    @LockDate,
    0,
    -2,
    0
)
",
                id,
                new SqlParameter("Parent", parent.ID),
                new SqlParameter("TargetIndex", (object)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("TargetTable", parent.ObjectType),
                new SqlParameter("Location", FormatOwner(owner)),
                new SqlParameter("Name", name),
                new SqlParameter("TargetDescription", parent.ObjectName),
                new SqlParameter("CreationDate", creationDate),
                new SqlParameter("LastModified", lastModified),
                new SqlParameter("UserName", parent.Vivendi.UserName),
                new SqlParameter("Blob", data),
                new SqlParameter("LockDate", (object)lockDate ?? DBNull.Value)
            );
            return new VivendiStoreDocument(parent, (int)id.Value, owner, name, creationDate, lastModified, data, lockDate);
        }

        private static void EnsureNameLength(string name)
        {
            // make sure the name is not empty of too long
            const int MaxLength = 255;
            if (name.Length == 0)
            {
                throw VivendiException.ResourceNameIsInvalid();
            }
            if (name.Length > MaxLength)
            {
                throw VivendiException.ResourceNameExceedsRange(MaxLength);
            }
        }

        private static void EnsureSize(int? maxSize, int documentSize)
        {
            // make sure the document doesn't exceed the collection's size limit
            if (maxSize.HasValue && documentSize > maxSize.Value)
            {
                throw VivendiException.DocumentIsTooLarge(maxSize.Value);
            }
        }

        private static void EnsureValidName(ref string name)
        {
            // remove trailing dots, check the length and characters
            name = name.TrimEnd('.');
            EnsureNameLength(name);
            if (!IsValidName(name))
            {
                throw VivendiException.ResourceNameIsInvalid();
            }
        }

        private static string FormatOwner(SecurityIdentifier owner) => WEBDAV_PREFIX + owner;

        private static IEnumerable<VivendiStoreDocument> FromNamedTemplates(IEnumerable<Template> templates, SecurityIdentifier owner, bool allowTyped)
        {
            using var enumerator = templates.GetEnumerator();

            // return nothing if there are no documents
            if (!enumerator.MoveNext())
            {
                yield break;
            }

            // check if there is more than one document
            var template = enumerator.Current;
            if (enumerator.MoveNext())
            {
                // let's see if there is just one document owned by the current user
                var hasOwned = template.Owner == owner;

                // return the first document as typed if we allow typed documents and it's not owned
                if (allowTyped && !hasOwned)
                {
                    yield return new VivendiStoreDocument(false, template);
                }

                // check the owner of the remaining documents
                do
                {
                    var current = enumerator.Current;
                    if (current.Owner == owner)
                    {
                        // check if this is the second document owned by the user
                        if (hasOwned)
                        {
                            // return all remaining documents as typed if allowed and exit
                            if (allowTyped)
                            {
                                yield return new VivendiStoreDocument(false, template);
                                yield return new VivendiStoreDocument(false, current);
                                while (enumerator.MoveNext())
                                {
                                    yield return new VivendiStoreDocument(false, enumerator.Current);
                                }
                            }
                            yield break;
                        }

                        // store the template
                        template = current;
                        hasOwned = true;
                    }
                    else
                    {
                        // not owned, return the document as typed if allowed
                        if (allowTyped)
                        {
                            yield return new VivendiStoreDocument(false, current);
                        }
                    }
                } while (enumerator.MoveNext());

                // there are more documents but none is owned by the user, exit
                if (!hasOwned)
                {
                    yield break;
                }
            }

            // return the named document
            yield return new VivendiStoreDocument(true, template);
        }

        private static SecurityIdentifier ParseOwner(string location)
        {
            // check the prefix and parse the sid
            if (!location.StartsWith(WEBDAV_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var sddl = location.Substring(WEBDAV_PREFIX.Length);
            try { return new SecurityIdentifier(sddl); }
            catch (ArgumentException) { return null; }
        }

        private static IEnumerable<Template> Query(VivendiStoreCollection parent, object id)
        {
            using var reader = parent.Vivendi.ExecuteReader
            (
                VivendiSource.Store,
@"
SELECT
    [Z_DA] AS [ID],
    CONVERT(bit, CASE WHEN [ZielTabelle2] = -2 AND [ZielTabelle3] = -2 THEN 0 ELSE 1 END) AS [AdditionalTargets],
    CASE WHEN [ZielTabelle3] = -2 THEN NULL ELSE [ZielTabelle3] END AS [Section],
    [Speicherort] AS [Location],
    [Dateiname] AS [DisplayName],
    [Dateidatum] AS [CreationDate],
    ISNULL([GeaendertDatum], [Dateidatum]) AS [LastModified],
    DATALENGTH(pDateiBlob) AS [Size],
    [Sperrdatum] AS [LockDate],
    CONVERT(bit, [bUnterschrieben]) AS [Signed]
FROM [dbo].[DATEI_ABLAGE]
WHERE
    (@ID IS NULL OR [Z_DA] = @ID) AND                                                  -- match the ID if one is given
    [iDokumentArt] = @Parent AND                                                       -- query within the parent collection
    [iSeriendruck] IS NULL AND                                                         -- no reports
    ([ZielIndex1] IS NULL AND @TargetIndex IS NULL OR [ZielIndex1] = @TargetIndex) AND -- query for a object instance
    [ZielTabelle1] = @TargetTable AND                                                  -- query for a object type
    [bGeZippt] = 0 AND                                                                 -- no zipped docs (because not reproducable in Vivendi)
    [CloudTyp] = -2                                                                    -- no cloud documents (also not found in UI)
",
                new SqlParameter("ID", id),
                new SqlParameter("Parent", parent.ID),
                new SqlParameter("TargetIndex", (object)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("TargetTable", parent.ObjectType)
            );
            while (reader.Read())
            {
                yield return new Template
                (
                    parent,
                    reader.GetInt32("ID"),
                    reader.GetBoolean("AdditionalTargets"),
                    reader.GetInt32Optional("Section"),
                    ParseOwner(reader.GetStringOptional("Location")),
                    reader.GetString("DisplayName"),
                    reader.GetDateTime("CreationDate"),
                    reader.GetDateTime("LastModified"),
                    reader.GetInt32("Size"),
                    reader.GetDateTimeOptional("LockDate"),
                    reader.GetBoolean("Signed")
                );
            }
        }

        internal static IEnumerable<VivendiStoreDocument> QueryAll(VivendiStoreCollection parent)
        {
            // cache the owner and return all documents
            var owner = (parent ?? throw new ArgumentNullException(nameof(parent))).Vivendi.UserSid;
            return Query(parent, DBNull.Value)
                .ToLookup(t => t.DisplayName, Vivendi.PathComparer)
                .SelectMany
                (
                    // only use the named logic if the document name is valid and not a reserved type/id string
                    ts => ts.Key.Length > 0 && ts.Key[ts.Key.Length - 1] != '.' && IsValidName(ts.Key) && !TryParseTypeAndID(ts.Key, out _, out _)
                    ? FromNamedTemplates(ts, owner, true)
                    : ts.Select(t => new VivendiStoreDocument(false, t))
                );
        }

        internal static VivendiStoreDocument QueryByID(VivendiStoreCollection parent, int id) => Query(parent ?? throw new ArgumentNullException(nameof(parent)), id).Select(t => new VivendiStoreDocument(false, t)).SingleOrDefault();

        internal static VivendiStoreDocument QueryByName(VivendiStoreCollection parent, string name)
        {
            // check the arguments and return the document with the given name, if there is just one (or just one owned by the current user)
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            return FromNamedTemplates(Query(parent, DBNull.Value).Where(t => string.Equals(t.DisplayName, name, Vivendi.PathComparison)), parent.Vivendi.UserSid, false).SingleOrDefault();
        }

        private readonly bool _additionalTargets;
        private DateTime _creationDate;
        private byte[] _data;
        private string _displayName;
        private bool _isDeletedOrMoved;
        private DateTime _lastModified;
        private readonly DateTime? _lockDate;
        private readonly SecurityIdentifier _owner;
        private readonly VivendiStoreCollection _parent;
        private readonly bool _signed;
        private int _size;

        private VivendiStoreDocument(bool isNamed, Template template)
        : base(template.Parent, isNamed ? VivendiResourceType.Named : VivendiResourceType.StoreDocument, template.ID, template.DisplayName)
        {
            _parent = template.Parent;
            _additionalTargets = template.AdditionalTargets;
            if (template.Section.HasValue)
            {
                Sections = Enumerable.Repeat(template.Section.Value, 1);
            }
            _owner = template.Owner;
            _displayName = template.DisplayName;
            _creationDate = template.CreationDate;
            _lastModified = template.LastModified;
            _data = null;
            _size = template.Size;
            _lockDate = template.LockDate;
            _signed = template.Signed;
        }

        private VivendiStoreDocument(VivendiStoreCollection parent, int id, SecurityIdentifier owner, string displayName, DateTime creationDate, DateTime lastModified, byte[] data, DateTime? lockDate)
        : base(parent, VivendiResourceType.StoreDocument, id, displayName)
        {
            _parent = parent;
            _additionalTargets = false;
            _owner = owner;
            _displayName = displayName;
            _creationDate = creationDate;
            _lastModified = lastModified;
            _data = data;
            _size = data.Length;
            _lockDate = lockDate;
        }

        public override FileAttributes Attributes
        {
            get => base.Attributes | (!CheckWrite(true) ? FileAttributes.ReadOnly : 0);
            set => base.Attributes = value;
        }

        public override DateTime CreationDate
        {
            get
            {
                EnsureNotDeletedOrMoved();
                EnsureCanRead();
                return _creationDate;
            }
            set
            {
                // ensure the document still exists and that the date has changed
                EnsureNotDeletedOrMoved();
                if (value != _creationDate)
                {
                    EnsureCanWrite();
                    Vivendi.ExecuteNonQuery
                    (
                        VivendiSource.Store,
@"
UPDATE [dbo].[DATEI_ABLAGE]
SET [Dateidatum] = @CreationDate
WHERE [Z_DA] = @ID
",
                        new SqlParameter("CreationDate", value),
                        new SqlParameter("ID", ID)
                    );
                    _creationDate = value;
                }
            }
        }

        public override byte[] Data
        {
            get
            {
                // fetch the blob if necessary and return it
                EnsureNotDeletedOrMoved();
                EnsureCanRead();
                if (_data == null)
                {
                    _data = (byte[])Vivendi.ExecuteScalar
                    (
                        VivendiSource.Store,
@"
SELECT [pDateiBlob]
FROM [dbo].[DATEI_ABLAGE]
WHERE [Z_DA] = @ID
",
                        new SqlParameter("ID", ID)
                    );
                }
                return _data;
            }
            set
            {
                // check the value and whether the document still exists
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(Data));
                }
                EnsureNotDeletedOrMoved();

                // ensure all necessary conditions for a write operation
                EnsureCanWrite();
                EnsureSize(_parent.MaxDocumentSize, value.Length);

                // update the blob and meta data
                var lastModified = DateTime.Now;
                Vivendi.ExecuteNonQuery
                (
                    VivendiSource.Store,
@"
UPDATE [dbo].[DATEI_ABLAGE]
SET
    [pDateiBlob] = @Blob,
    [GeaendertDatum] = @LastModified,
    [GeaendertVon] = @UserName
WHERE [Z_DA] = @ID
",
                    new SqlParameter("Blob", value),
                    new SqlParameter("UserName", Vivendi.UserName),
                    new SqlParameter("LastModified", lastModified),
                    new SqlParameter("ID", ID)
                );
                _size = value.Length;
                _lastModified = lastModified;
                _data = value;
            }
        }

        public override string DisplayName
        {
            get
            {
                EnsureNotDeletedOrMoved();
                EnsureCanRead();
                return _displayName;
            }
            set
            {
                // check the value and ensure the document still exists
                EnsureNameLength(value ?? throw new ArgumentNullException(nameof(DisplayName)));
                EnsureNotDeletedOrMoved();

                // update the document if the name has changed
                if (value != _displayName)
                {
                    EnsureCanWrite();
                    Vivendi.ExecuteNonQuery
                    (
                        VivendiSource.Store,
@"
UPDATE [dbo].[DATEI_ABLAGE]
SET [Dateiname] = @DisplayName
WHERE [Z_DA] = @ID
",
                        new SqlParameter("DisplayName", value),
                        new SqlParameter("ID", ID)
                    );
                    _displayName = value;
                }
            }
        }

        internal override bool InCollection => !_isDeletedOrMoved && base.InCollection;

        public override DateTime LastModified
        {
            get
            {
                EnsureNotDeletedOrMoved();
                EnsureCanRead();
                return _lastModified;
            }
            set
            {
                // ensure the document still exists and that the date has changed
                EnsureNotDeletedOrMoved();
                if (value != _lastModified)
                {
                    EnsureCanWrite();
                    Vivendi.ExecuteNonQuery
                    (
                        VivendiSource.Store,
@"
UPDATE [dbo].[DATEI_ABLAGE]
SET
    [GeaendertDatum] = @LastModified,
    [GeaendertVon] = ISNULL([GeaendertVon], [ErstelltVon])
WHERE [Z_DA] = @ID
",
                        new SqlParameter("LastModified", value),
                        new SqlParameter("ID", ID)
                    );
                    _lastModified = value;
                }
            }
        }

        public override int Size
        {
            get
            {
                EnsureNotDeletedOrMoved();
                EnsureCanRead();
                return _size;
            }
        }

        private bool CheckWrite(bool dontThrow)
        {
            // check if the document is locked
            if (_lockDate.HasValue && DateTime.Now >= _lockDate.Value)
            {
                return dontThrow ? false : throw VivendiException.DocumentIsLocked(_lockDate.Value);
            }

            // check if signed
            if (_signed)
            {
                return dontThrow ? false : throw VivendiException.DocumentIsSigned();
            }

            // check if the file was uploaded by WebDAV
            if (_owner == null)
            {
                if (Vivendi.AllowModificationOfVivendiResource?.Invoke(this) == true)
                {
                    return true;
                }
                return dontThrow ? false : throw VivendiException.DocumentIsNotWebDAV();
            }

            // check if it's the same owner or the owner's manager
            if (_owner != Vivendi.UserSid && Vivendi.AllowModificationOfOwnedResource?.Invoke(this, _owner) != true)
            {
                return dontThrow ? false : throw VivendiException.DocumentHasDifferentOwner();
            }

            // document is writable
            return true;
        }

        public override void Delete()
        {
            // delete the document if it still exists and the user is allowed to do so
            EnsureNotDeletedOrMoved();
            EnsureCanWrite();
            EnsureNoAdditionalTargets();
            Vivendi.ExecuteNonQuery
            (
                VivendiSource.Store,
@"
DELETE FROM [dbo].[DATEI_ABLAGE]
WHERE [Z_DA] = @ID
",
                new SqlParameter("ID", ID)
            );
            _isDeletedOrMoved = true;
        }

        internal override void EnsureCanWrite()
        {
            // extend the write checks
            base.EnsureCanWrite();
            CheckWrite(false);
        }

        private void EnsureNoAdditionalTargets()
        {
            // make sure that the document doesn't contain additional references like parents, sessions, etc.
            if (_additionalTargets)
            {
                throw VivendiException.DocumentContainsAdditionalLinks();
            }
        }

        private void EnsureNotDeletedOrMoved()
        {
            if (_isDeletedOrMoved)
            {
                throw new InvalidOperationException("The document has been deleted or moved.");
            }
        }

        public override void MoveTo(VivendiCollection destCollection, string destName)
        {
            // check the value and that the document still exists
            if (destCollection == null)
            {
                throw new ArgumentNullException(nameof(destCollection));
            }
            if (destName == null)
            {
                throw new ArgumentNullException(nameof(destName));
            }
            var parent = destCollection as VivendiStoreCollection ?? throw VivendiException.DocumentNotAllowedInCollection();
            EnsureValidName(ref destName);
            EnsureNotDeletedOrMoved();

            // check all prerequisites to move the document
            parent.EnsureCanWrite();
            EnsureCanWrite();
            EnsureNoAdditionalTargets();

            // move the document
            Vivendi.ExecuteNonQuery
            (
                VivendiSource.Store,
@"
UPDATE [dbo].[DATEI_ABLAGE]
SET
    [iDokumentArt] = @Parent,
    [ZielIndex1] = @TargetIndex,
    [ZielTabelle1] = @TargetTable,
    [ZielBeschreibung] = @TargetDescription,
    [Dateiname] = @Name
WHERE [Z_DA] = @ID
",
                new SqlParameter("Parent", parent.ID),
                new SqlParameter("TargetIndex", (object)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("TargetTable", parent.ObjectType),
                new SqlParameter("TargetDescription", parent.ObjectName),
                new SqlParameter("Name", destName),
                new SqlParameter("ID", ID)
            );
            _isDeletedOrMoved = true;
        }
    }
}
