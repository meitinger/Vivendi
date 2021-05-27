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
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web;

namespace Aufbauwerk.Tools.Vivendi
{
    public abstract class VivendiDocument : VivendiResource
    {
        internal VivendiDocument(VivendiCollection parent, string name, string? localizedName = null)
        : base(parent, name, localizedName)
        {
        }

        public virtual string ContentType => MimeMapping.GetMimeMapping(Name);

        public abstract byte[] Data { get; set; }

        public string NameWithoutExtension
        {
            get
            {
                var dot = Name.IndexOf('.');
                return dot > -1 ? Name.Substring(0, dot) : Name;
            }
        }

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
        : base(parent, name)
        {
            _buildTime = DateTime.Now;
            _attributes = FileAttributes.ReadOnly | (attributes & ~(FileAttributes.Normal | FileAttributes.Directory));
            _data = data;
        }

        public override FileAttributes Attributes => _attributes;

        public override DateTime CreationDate
        {
            get => _buildTime;
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override byte[] Data
        {
            get => _data;
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override string DisplayName
        {
            get => Name;
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        internal override bool InCollection => true;

        public override DateTime LastModified
        {
            get => _buildTime;
            set => throw VivendiException.ResourcePropertyIsReadonly();
        }

        public override int Size => _data.Length;

        public override void Delete() => throw VivendiException.ResourceIsStatic();

        public override void MoveTo(VivendiCollection destCollection, string destName) => throw VivendiException.ResourceIsStatic();
    }

    internal sealed class VivendiStoreDocument : VivendiDocument
    {
        private const string GetDataCommandPart = @"ISNULL(ISNULL((SELECT [pDateiBlob] FROM [dbo].[DATEI_ABLAGE_BLOBS] WHERE [Z_DAB] = (SELECT TOP (1) [iBlobs] FROM [dbo].[DATEI_ABLAGE_BLOBS_ZUORD] WHERE [iDateiablage] = [Z_DA] ORDER BY [Revision] DESC)), [pDateiBlob]), 0x)";

        private const string InsertRevisionCommand =
@"
DECLARE @iBlobs AS int;
SELECT @iBlobs = ISNULL(MAX([Z_DAB]), 0) + 1
FROM [dbo].[DATEI_ABLAGE_BLOBS];
INSERT INTO [dbo].[DATEI_ABLAGE_BLOBS]
(
    Z_DAB,
    pDateiBlob
)
VALUES
(
    @iBlobs,
    @Blob
);

DECLARE @iZuord AS int;
DECLARE @iRevision AS int;
SELECT @iZuord = ISNULL(MAX([Z_DR]), 0) + 1
FROM [dbo].[DATEI_ABLAGE_BLOBS_ZUORD];
SELECT @iRevision = ISNULL(MAX([Revision]) + 1, 0)
FROM [dbo].[DATEI_ABLAGE_BLOBS_ZUORD]
WHERE [iDateiablage] = @ID;
INSERT INTO [dbo].[DATEI_ABLAGE_BLOBS_ZUORD]
(
    Z_DR,
    iDateiablage,
    iBlobs,
    GeaendertDatum,
    GeaendertVon,
    Revision
)
VALUES
(
    @iZuord,
    @ID,
    @iBlobs,
    @LastModified,
    @UserName,
    @iRevision
);
";

        private const string WebDAVPrefix = "webdav:v2:";

        internal static VivendiStoreDocument Create(VivendiStoreCollection parent, string name, DateTime creationDate, DateTime lastModified, byte[] data)
        {
            EnsureValidName(parent, ref name);
            parent.EnsureCanWrite();
            EnsureSize(parent.MaxDocumentSize, data.Length);
            var lockDate = !parent.LockAfterMonths.HasValue ? (DateTime?)null : DateTime.Now.Date.AddMonths(parent.LockAfterMonths.Value);
            var id = new SqlParameter("ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            const string command =
@"
SELECT @ID = ISNULL(MAX([Z_DA]), 0) + 1
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
    [GeaendertVon],
    [BelegDatum],
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
    @LastModified,
    @UserName,
    CONVERT(date, @CreationDate),
    @LockDate,
    0,
    -2,
    0
);
";
            parent.Vivendi.ExecuteNonQuery
            (
                VivendiSource.Store,
                data.Length == 0 ? command : command + InsertRevisionCommand,
                id,
                new SqlParameter("Parent", parent.ID),
                new SqlParameter("TargetIndex", (object?)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("TargetTable", parent.ObjectType),
                new SqlParameter("Location", WebDAVPrefix + name),
                new SqlParameter("Name", name),
                new SqlParameter("TargetDescription", parent.ObjectName),
                new SqlParameter("CreationDate", creationDate),
                new SqlParameter("LastModified", lastModified),
                new SqlParameter("UserName", parent.Vivendi.UserName),
                new SqlParameter("LockDate", (object?)lockDate ?? DBNull.Value),
                new SqlParameter("Blob", data)
            );
            return new VivendiStoreDocument
            (
                parent: parent,
                id: (int)id.Value,
                additionalTargets: false,
                section: null,
                name: name,
                displayName: null,
                creationDate: creationDate,
                lastModified: lastModified,
                data: data,
                size: data.Length,
                lockDate: lockDate,
                signed: false
            );
        }

        private static void EnsureNameLength(string name)
        {
            // make sure the name is not empty or too long
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

        private static void EnsureValidName(VivendiStoreCollection parent, ref string name)
        {
            // remove trailing dots, check the length, characters and special format
            name = name.TrimEnd('.');
            EnsureNameLength(name);
            if (!IsValidName(name) || TryParseTypeAndID(name, out _, out _))
            {
                throw VivendiException.ResourceNameIsInvalid();
            }
            if
            (
                (bool)parent.Vivendi.ExecuteScalar
                (
                    VivendiSource.Store,
@"
EXISTS
(
    SELECT *
    FROM [dbo].[DATEI_ABLAGE]
    WHERE
        [iDokumentArt] = @Parent AND
        ([ZielIndex1] IS NULL AND @TargetIndex IS NULL OR [ZielIndex1] = @TargetIndex) AND
        [ZielTabelle1] = @TargetTable AND
        [Speicherort] = @Location
)
",
                    new SqlParameter("Parent", parent.ID),
                    new SqlParameter("TargetIndex", (object?)parent.ObjectID ?? DBNull.Value),
                    new SqlParameter("TargetTable", parent.ObjectType),
                    new SqlParameter("Location", WebDAVPrefix + name)
                )
            )
            {
                throw VivendiException.DocumentAlreadyExists();
            }
        }

        private static IEnumerable<VivendiStoreDocument> Query(VivendiStoreCollection parent, int? lookupId = null, string? lookupName = null)
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
    CONVERT(int, DATALENGTH(" + GetDataCommandPart + @")) AS [Size],
    [Sperrdatum] AS [LockDate],
    CONVERT(bit, [bUnterschrieben]) AS [Signed]
FROM [dbo].[DATEI_ABLAGE]
WHERE
    (@ID IS NULL OR [Z_DA] = @ID) AND                                                  -- match the ID if one is given
    [iDokumentArt] = @Parent AND                                                       -- query within the parent collection
    [iSeriendruck] IS NULL AND [iSerieDatensatz] IS NULL AND                           -- no reports
    ([ZielIndex1] IS NULL AND @TargetIndex IS NULL OR [ZielIndex1] = @TargetIndex) AND -- query for a object instance
    [ZielTabelle1] = @TargetTable AND                                                  -- query for a object type
    (@Location IS NULL OR [Speicherort] = @Location) AND                               -- match the name if one is given
    [bGeZippt] = 0                                                                     -- no zipped docs (because not reproducible in Vivendi)
",
                new SqlParameter("ID", (object?)lookupId ?? DBNull.Value),
                new SqlParameter("Parent", parent.ID),
                new SqlParameter("TargetIndex", (object?)parent.ObjectID ?? DBNull.Value),
                new SqlParameter("TargetTable", parent.ObjectType),
                new SqlParameter("Location", lookupName != null ? WebDAVPrefix + lookupName : (object)DBNull.Value)
            );
            while (reader.Read())
            {
                // handle names based on whether the document was created by WebDAV
                var location = reader.GetString("Location");
                var id = reader.GetInt32("ID");
                var displayName = reader.GetString("DisplayName");
                string name;
                if (location.StartsWith(WebDAVPrefix))
                {
                    name = location.Substring(WebDAVPrefix.Length);
                    if (string.Equals(displayName, name, Vivendi.PathComparison))
                    {
                        // no display name needed
                        displayName = null;
                    }
                    else
                    {
                        var dot = name.LastIndexOf('.');
                        if (dot > -1)
                        {
                            var extension = name.Substring(dot);
                            if (displayName.EndsWith(extension, Vivendi.PathComparison))
                            {
                                // remove the common extension
                                displayName = displayName.Substring(0, displayName.Length - extension.Length);
                            }
                        }
                    }
                }
                else
                {
                    // use an type/ID string together with the extension if the latter is valid
                    var extension = string.Empty;
                    var dot = displayName.LastIndexOf('.');
                    if (dot > -1)
                    {
                        var extensionTest = displayName.Substring(dot);
                        if (IsValidName(extension))
                        {
                            displayName = displayName.Substring(0, dot);
                            extension = extensionTest;
                        }
                    }
                    name = FormatTypeAndId(VivendiResourceType.StoreDocument, id, extension);
                }

                // create the document
                yield return new VivendiStoreDocument
                (
                    parent: parent,
                    id: id,
                    additionalTargets: reader.GetBoolean("AdditionalTargets"),
                    section: reader.GetInt32Optional("Section"),
                    name: name,
                    displayName: displayName,
                    creationDate: reader.GetDateTime("CreationDate"),
                    lastModified: reader.GetDateTime("LastModified"),
                    data: null,
                    size: reader.GetInt32("Size"),
                    lockDate: reader.GetDateTimeOptional("LockDate"),
                    signed: reader.GetBoolean("Signed")
                );
            }
        }

        internal static IEnumerable<VivendiStoreDocument> QueryAll(VivendiStoreCollection parent) => Query(parent);

        internal static VivendiStoreDocument QueryByID(VivendiStoreCollection parent, int id) => Query(parent, lookupId: id).SingleOrDefault();

        internal static VivendiStoreDocument QueryByName(VivendiStoreCollection parent, string name) => Query(parent, lookupName: name).SingleOrDefault();

        private readonly bool _additionalTargets;
        private DateTime _creationDate;
        private byte[]? _data;
        private bool _isDeletedOrMoved;
        private DateTime _lastModified;
        private readonly DateTime? _lockDate;
        private readonly VivendiStoreCollection _parent;
        private readonly bool _signed;
        private int _size;

        private VivendiStoreDocument(VivendiStoreCollection parent, int id, bool additionalTargets, int? section, string name, string? displayName, DateTime creationDate, DateTime lastModified, byte[]? data, int size, DateTime? lockDate, bool signed)
        : base(parent, name, displayName)
        {
            _parent = parent;
            _additionalTargets = additionalTargets;
            _creationDate = creationDate;
            _data = data;
            _isDeletedOrMoved = false;
            _lastModified = lastModified;
            _lockDate = lockDate;
            _signed = signed;
            _size = size;
            ID = id;
            if (section.HasValue)
            {
                Sections = Enumerable.Repeat(section.Value, 1);
            }
        }

        public override FileAttributes Attributes
        {
            get
            {
                EnsureNotDeletedOrMoved();
                return base.Attributes | (!CheckWrite(true) ? FileAttributes.ReadOnly : 0);
            }
            set
            {
                EnsureNotDeletedOrMoved();
                base.Attributes = value;
            }
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
SELECT " + GetDataCommandPart + @"
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
                // ensure all necessary conditions for a write operation
                EnsureNotDeletedOrMoved();
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
    [GeaendertDatum] = @LastModified,
    [GeaendertVon] = @UserName
WHERE [Z_DA] = @ID;
" + InsertRevisionCommand,
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
                return LocalizedName ?? NameWithoutExtension;
            }
            set
            {
                // check the value and ensure the document still exists
                EnsureNameLength(value);
                EnsureNotDeletedOrMoved();

                // update the document if the name has changed
                if (value != (LocalizedName ?? NameWithoutExtension))
                {
                    EnsureCanWrite();
                    var dot = Name.IndexOf('.');
                    var displayName = dot > -1 ? value + Name.Substring(dot) : value;
                    Vivendi.ExecuteNonQuery
                    (
                        VivendiSource.Store,
@"
UPDATE [dbo].[DATEI_ABLAGE]
SET [Dateiname] = @DisplayName
WHERE [Z_DA] = @ID
",
                        new SqlParameter("DisplayName", displayName),
                        new SqlParameter("ID", ID)
                    );
                    LocalizedName = value;
                }
            }
        }

        public int ID { get; }

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

            // document is writable
            return true;
        }

        public override void Delete()
        {
            // delete the document if it still exists and the user is allowed to do so
            EnsureNotDeletedOrMoved();
            EnsureCanWrite();
            EnsureNoAdditionalTargets();
            EnsureNoRetainedRevisions();

            Vivendi.ExecuteNonQuery
            (
                VivendiSource.Store,
@"
DELETE FROM [dbo].[DATEI_ABLAGE_BLOBS]
WHERE [Z_DAB] IN
(
    SELECT [iBlobs]
    FROM [dbo].[DATEI_ABLAGE_BLOBS_ZUORD]
    WHERE [iDateiablage] = @ID
);
DELETE FROM [dbo].[DATEI_ABLAGE_BLOBS_ZUORD]
WHERE [iDateiablage] = @ID;
DELETE FROM [dbo].[DATEI_ABLAGE]
WHERE [Z_DA] = @ID;
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

        private void EnsureNoRetainedRevisions()
        {
            // make sure that the document type does not enforce to retain revisions
            if (_parent.RetainRevisions)
            {
                throw VivendiException.DocumentHasRevisions();
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
            // check the values and that the document still exists
            var destParent = destCollection as VivendiStoreCollection ?? throw VivendiException.DocumentNotAllowedInCollection();
            EnsureValidName(destParent, ref destName);
            EnsureNotDeletedOrMoved();

            // check all prerequisites to move the document
            destParent.EnsureCanWrite();
            EnsureCanWrite();
            EnsureNoAdditionalTargets();
            if (!destParent.RetainRevisions)
            {
                EnsureNoRetainedRevisions();
            }

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
    [Speicherort] = @Location,
    [Dateiname] = @Name
WHERE [Z_DA] = @ID
",
                new SqlParameter("Parent", destParent.ID),
                new SqlParameter("TargetIndex", (object?)destParent.ObjectID ?? DBNull.Value),
                new SqlParameter("TargetTable", destParent.ObjectType),
                new SqlParameter("TargetDescription", destParent.ObjectName),
                new SqlParameter("Location", WebDAVPrefix + destName),
                new SqlParameter("Name", destName),
                new SqlParameter("ID", ID)
            );
            _isDeletedOrMoved = true;
        }
    }
}
