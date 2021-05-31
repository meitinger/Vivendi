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
using System.Globalization;
using System.IO;
using System.Linq;
using static System.FormattableString;

namespace Aufbauwerk.Tools.Vivendi
{
    public enum VivendiResourceType
    {
        StoreCollection = 2,
        StoreDocument = 1,
        ObjectInstanceCollection = 3,
        ObjectTypeCollection = 4,
    }

    public abstract class VivendiResource
    {
        private struct ObjectInstance
        {
            public ObjectInstance(int? id, string name)
            {
                ID = id;
                Name = name;
            }

            public readonly int? ID;
            public readonly string Name;
        }

        internal static readonly char[] ForbiddenNameEndingChars = new char[] { ' ', '.' };
        internal static readonly char[] InvalidNameChars = new char[] { '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007', '\u0008', '\u0009', '\u000A', '\u000B', '\u000C', '\u000D', '\u000E', '\u000F', '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017', '\u0018', '\u0019', '\u001A', '\u001B', '\u001C', '\u001D', '\u001E', '\u001F', '"', '%', '*', '/', ':', '<', '>', '?', '\\', '|' };
        internal const string ReservedNamePrefix = "DavWWW";

        internal static void EnsureNameLength(string name, int maxLength)
        {
            // make sure the name is not empty or too long
            if (string.IsNullOrWhiteSpace(name))
            {
                throw VivendiException.ResourceNameIsInvalid();
            }
            if (name.Length > maxLength)
            {
                throw VivendiException.ResourceNameExceedsRange(maxLength);
            }
        }

        internal static void EnsureValidNameWithoutPrefix(string name)
        {
            // check length and content of the name
            if (!IsValidName(name) || name.StartsWith(ReservedNamePrefix, Vivendi.PathComparison))
            {
                throw VivendiException.ResourceNameIsInvalid();
            }
        }

        internal static string FormatTypeAndId(VivendiResourceType type, int id, string extension = "")
        {
            // ensure the arguments are valid before formatting them
            if (!Enum.IsDefined(typeof(VivendiResourceType), type))
            {
                throw new InvalidEnumArgumentException(nameof(type), (int)type, typeof(VivendiResourceType));
            }
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Resource IDs must be non-negative.");
            }
            return Invariant($"{ReservedNamePrefix}-{(int)type}-{id}{extension}");
        }

        internal static bool IsValidName(string name) => name.Length > 0 && name.IndexOfAny(InvalidNameChars) == -1 && Array.IndexOf(ForbiddenNameEndingChars, name[name.Length - 1]) == -1;

        internal static bool TryParseTypeAndID(string name, out VivendiResourceType type, out int id)
        {
            // remove the extension and parse the remaining name
            var dot = name.LastIndexOf('.');
            var typeAndId = (dot > -1 ? name.Substring(0, dot) : name).Split('-');
            if (typeAndId.Length == 3 && string.Equals(typeAndId[0], ReservedNamePrefix, Vivendi.PathComparison) && int.TryParse(typeAndId[1], NumberStyles.None, CultureInfo.InvariantCulture, out var typeNumeric) && int.TryParse(typeAndId[2], NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                type = (VivendiResourceType)typeNumeric;
                return true;
            }
            else
            {
                type = 0;
                id = -1;
                return false;
            }
        }

        private ObjectInstance? _objectInstance;
        private int? _objectType;
        private ISet<int>? _sections;

        internal VivendiResource(VivendiCollection? parent, string name, string? localizedName = null)
        {
            // the name must be empty iff the resource is a root
            if (name.Length == 0 ^ parent == null)
            {
                throw new ArgumentException("Invalid name and parent combination.");
            }
            if (parent != null && !IsValidName(name))
            {
                throw new ArgumentException("Invalid name.", nameof(name));
            }

            Parent = parent;
            Name = name;
            LocalizedName = localizedName;
        }

        public virtual FileAttributes Attributes
        {
            get => FileAttributes.Archive | (CheckWrite(true) ? 0 : FileAttributes.ReadOnly);
            set
            {
                if (value != Attributes)
                {
                    throw VivendiException.ResourcePropertyIsReadonly();
                }
            }
        }

        public abstract DateTime CreationDate { get; set; }

        public abstract string DisplayName { get; set; }

        private ObjectInstance FirstObjectInstance => FirstObjectInstanceOrNull ?? throw new InvalidOperationException("Object instance has not been set.");

        private ObjectInstance? FirstObjectInstanceOrNull => SelfAndAncestorsWithSameObjectType.FirstOrDefault(r => r._objectInstance.HasValue)?._objectInstance;

        protected internal bool HasObjectInstance => FirstObjectInstanceOrNull.HasValue;

        protected internal bool HasObjectType => ObjectTypeOrNull.HasValue;

        internal virtual bool InCollection => CheckRead(true);

        public abstract DateTime LastModified { get; set; }

        protected internal string? LocalizedName { get; protected set; }

        public string Name { get; }

        protected internal int? ObjectID => FirstObjectInstance.ID;

        protected internal string ObjectName => FirstObjectInstance.Name;

        protected internal int ObjectType
        {
            get => ObjectTypeOrNull ?? throw new InvalidOperationException("Object type has not been set.");
            protected set => _objectType = value;
        }

        private int? ObjectTypeOrNull => SelfAndAncestors.FirstOrDefault(r => r._objectType.HasValue)?._objectType;

        public string Path => Parent == null ? "/" : string.Join("/", SelfAndAncestors.Reverse().Select(r => r.Name));

        public VivendiCollection? Parent { get; }

        protected int RequiredAccessLevel { get; set; }

        protected IEnumerable<int>? Sections
        {
            get => _sections;
            set => _sections = value?.ToHashSet();
        }

        internal IEnumerable<VivendiResource> SelfAndAncestors
        {
            get
            {
                for (var res = this; res != null; res = res.Parent)
                {
                    yield return res;
                }
            }
        }

        private IEnumerable<VivendiResource> SelfAndAncestorsWithSameObjectType
        {
            get
            {
                var objectType = ObjectTypeOrNull;
                return objectType == null ? Enumerable.Empty<VivendiResource>() : SelfAndAncestors.TakeWhile(r => r.ObjectTypeOrNull == objectType);
            }
        }

        public Vivendi Vivendi => VivendiOrNull ?? throw new InvalidOperationException("No ancestral Vivendi instance.");

        private Vivendi? VivendiOrNull => SelfAndAncestors.OfType<Vivendi>().FirstOrDefault();

        private bool CheckPermission(Func<IEnumerable<int>?, int> getAccessLevel, Func<int, IEnumerable<int>> getSections, bool dontThrow)
        {
            // make sure all access levels are sufficient
            if (SelfAndAncestors.Any(r => r.RequiredAccessLevel >= getAccessLevel(r.Sections)))
            {
                return dontThrow ? false : throw VivendiException.ResourceRequiresHigherAccessLevel();
            }

            // if the resource has a type, make sure it's at least one of its section is accessible
            if (HasObjectType)
            {
                var grantedSections = getSections(ObjectType);
                if (grantedSections != null && SelfAndAncestors.Any(r => r.Sections != null && !r.Sections.Intersect(grantedSections).Any()))
                {
                    return dontThrow ? false : throw VivendiException.ResourceNotInGrantedSections();
                }
            }

            // all tests passed
            return true;
        }

        private bool CheckRead(bool dontThrow) => VivendiOrNull == null || CheckPermission(Vivendi.GetReadAccessLevel, Vivendi.GetReadableSections, dontThrow);

        private bool CheckWrite(bool dontThrow) => VivendiOrNull != null && CheckPermission(Vivendi.GetWriteAccessLevel, Vivendi.GetWritableSections, dontThrow);

        internal virtual bool EnsureCanRead() => CheckRead(false);

        internal virtual void EnsureCanWrite() => CheckWrite(false);

        protected void SetObjectInstance(int? id, string name)
        {
            if (id < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "An object's ID must be positive.");
            }
            if (!HasObjectType)
            {
                throw new InvalidOperationException("Object type must be set before the instance.");
            }
            _objectInstance = new ObjectInstance(id, name);
        }
    }
}
