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
using System.Globalization;
using System.IO;
using System.Linq;
using static System.FormattableString;

namespace Aufbauwerk.Tools.Vivendi
{
    public enum VivendiResourceType
    {
        Named = -1,
        Root = 0,
        StoreCollection = 2,
        StoreDocument = 1,
        ObjectInstanceCollection = 3,
        ObjectTypeCollection = 4,
    }

    public abstract class VivendiResource
    {
        private static readonly char[] InvalidNameChars = new char[] { '"', '<', '>', '|', '\0', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\a', '\b', '\t', '\n', '\v', '\f', '\r', '\u000e', '\u000f', '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016', '\u0017', '\u0018', '\u0019', '\u001a', '\u001b', '\u001c', '\u001d', '\u001e', '\u001f', ':', '*', '?', '\\', '/' };

        internal static bool IsValidName(string name) => (name ?? throw new ArgumentNullException(nameof(name))).IndexOfAny(InvalidNameChars) == -1;

        internal static bool TryParseTypeAndID(string name, out VivendiResourceType type, out int id)
        {
            // remove the extension and parse the remaining name
            var dot = (name ?? throw new ArgumentNullException(nameof(name))).LastIndexOf('.');
            var typeAndId = (dot > -1 ? name.Substring(0, dot) : name).Split('-');
            if (typeAndId.Length == 2 && int.TryParse(typeAndId[0], NumberStyles.None, CultureInfo.InvariantCulture, out var typeNumeric) && int.TryParse(typeAndId[1], NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                type = (VivendiResourceType)typeNumeric;
                return true;
            }
            else
            {
                type = VivendiResourceType.Named;
                id = -1;
                return false;
            }
        }


        private bool _hasObjectInstance = false;
        private bool _hasObjectType = false;
        private int? _objectID;
        private string _objectName;
        private int _objectType;
        private IEnumerable<int> _sections;

        internal VivendiResource(VivendiCollection parent, VivendiResourceType type, int id, string name)
        {
            // check the arguments
            if (!Enum.IsDefined(typeof(VivendiResourceType), type))
            {
                throw new InvalidEnumArgumentException(nameof(type), (int)type, typeof(VivendiResourceType));
            }
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Resource IDs must be non-negative.");
            }
            if (type == VivendiResourceType.Root ^ parent == null)
            {
                throw new ArgumentException("Invalid type and parent combination.");
            }

            // set the non-computed values
            Parent = parent;
            Type = type;
            ID = id;

            // get the extension and set the localized name
            var extension = string.Empty;
            LocalizedName = name ?? throw new ArgumentNullException(nameof(name));
            if (!(this is VivendiCollection))
            {
                var dot = name.LastIndexOf('.');
                if (dot > -1)
                {
                    var extensionTest = name.Substring(dot);
                    if (extensionTest.IndexOfAny(InvalidNameChars) == -1)
                    {
                        extension = extensionTest;
                        LocalizedName = name.Substring(0, dot);
                    }
                }
            }

            // set the name property based on the type
            Name = type switch
            {
                VivendiResourceType.Root => string.Empty,
                VivendiResourceType.Named => name,
                _ => Invariant($"{(int)type}-{id}{extension}"),
            };
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

        internal bool HasObjectInstance => SelfAndAncestors.Any(r => r._hasObjectInstance);

        internal bool HasObjectType => SelfAndAncestors.Any(r => r._hasObjectType);

        internal int ID { get; }

        internal virtual bool InCollection => CheckRead(true);

        public abstract DateTime LastModified { get; set; }

        internal string LocalizedName { get; }

        public string Name { get; }

        internal int? ObjectID => GetInstanceProp(r => r._objectID);

        internal string ObjectName => GetInstanceProp(r => r._objectName);

        protected internal int ObjectType
        {
            get { return GetProp(r => r._objectType, r => r._hasObjectType, "Object type has not been set."); }
            protected set
            {
                _objectType = value;
                _hasObjectType = true;
            }
        }

        public string Path => Type == VivendiResourceType.Root ? "/" : string.Join("/", SelfAndAncestors.Reverse().Select(r => r.Name));

        public VivendiCollection Parent { get; }

        protected internal int RequiredAccessLevel { get; protected set; }

        protected internal IEnumerable<int> Sections
        {
            get => _sections;
            protected set => _sections = value?.ToHashSet();
        }

        internal IEnumerable<VivendiResource> SelfAndAncestors
        {
            get
            {
                // return this resource and all parent collections
                var res = this;
                while (true)
                {
                    yield return res;
                    if (res.Type == VivendiResourceType.Root)
                    {
                        break;
                    }
                    res = res.Parent;
                }
            }
        }

        public VivendiResourceType Type { get; }

        public Vivendi Vivendi => SelfAndAncestors.OfType<Vivendi>().First();

        private bool CheckPermission(Func<IEnumerable<int>, int> getAccessLevel, Func<int, IEnumerable<int>> getSections, bool dontThrow)
        {
            // make sure all access levels are sufficient
            if (SelfAndAncestors.Any(r => r.RequiredAccessLevel >= getAccessLevel(r.Sections)))
            {
                return dontThrow ? false : throw VivendiException.ResourceRequiresHigherAccessLevel();
            }

            // if the resource has a type, make sure it's at least one of its section is accessable
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

        private bool CheckRead(bool dontThrow) => CheckPermission(Vivendi.GetReadAccessLevel, Vivendi.GetReadableSections, dontThrow);

        private bool CheckWrite(bool dontThrow) => CheckPermission(Vivendi.GetWriteAccessLevel, Vivendi.GetWritableSections, dontThrow);

        internal virtual bool EnsureCanRead() => CheckRead(false);

        internal virtual void EnsureCanWrite() => CheckWrite(false);

        private T GetProp<T>(Func<VivendiResource, T> prop, Func<VivendiResource, bool> hasProp, string missing) => prop(SelfAndAncestors.FirstOrDefault(hasProp) ?? throw new InvalidOperationException(missing));

        private T GetInstanceProp<T>(Func<VivendiResource, T> prop) => GetProp(prop, r => r._hasObjectInstance, "Object instance has not been set.");

        protected void SetObjectInstace(int? id, string name)
        {
            if (id < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "An object's ID must be positive.");
            }
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (!HasObjectType)
            {
                throw new InvalidOperationException("Object type must be set before the instance.");
            }
            _objectID = id;
            _objectName = name;
            _hasObjectInstance = true;
        }
    }
}
