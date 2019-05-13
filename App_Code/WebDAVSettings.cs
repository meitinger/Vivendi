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
using System.Configuration;

public sealed class WebDAVSettings : ConfigurationSection
{
    const string AllowModificationOfOwnedResourcesElement = "allowModificationOfOwnedResources";
    const string AllowModificationOfVivendiResourcesElement = "allowModificationOfVivendiResources";
    const string InstanceElement = "webDAVSettings";

    public sealed class OwnedResourcesPrincipalCollection : PrincipalCollection
    {
        const string ManagersAttribute = "managers";
        const string TeamAttribute = "team";

        [ConfigurationProperty(ManagersAttribute, DefaultValue = true)]
        public bool Managers
        {
            get => (bool)this[ManagersAttribute];
            set => this[ManagersAttribute] = value;
        }

        [ConfigurationProperty(TeamAttribute, DefaultValue = false)]
        public bool Team
        {
            get => (bool)this[TeamAttribute];
            set => this[TeamAttribute] = value;
        }
    }

    public abstract class PrincipalCollection : ConfigurationElementCollection, IEnumerable<PrincipalElement>
    {
        protected override ConfigurationElement CreateNewElement() => new PrincipalElement();

        protected override Object GetElementKey(ConfigurationElement element) => ((PrincipalElement)element).Name;

        public IEnumerator<PrincipalElement> GetEnumerator()
        {
            var enumerator = base.GetEnumerator();
            while (enumerator.MoveNext())
            {
                yield return (PrincipalElement)enumerator.Current;
            }
        }
    }

    public sealed class PrincipalElement : ConfigurationElement
    {
        const string NameAttribute = "name";
        const string TypeAttribute = "type";

        [ConfigurationProperty(NameAttribute, IsRequired = true, IsKey = true)]
        public string Name
        {
            get => (string)this[NameAttribute];
            set => this[NameAttribute] = value;
        }

        [ConfigurationProperty(TypeAttribute, IsRequired = true)]
        public PrincipalType Type
        {
            get => (PrincipalType)this[TypeAttribute];
            set => this[TypeAttribute] = value;
        }
    }

    public enum PrincipalType
    {
        User,
        Group,
    }

    public sealed class VivendiResourcesPrincipalCollection : PrincipalCollection
    { }

    public static WebDAVSettings Instance { get; } = ConfigurationManager.GetSection(InstanceElement) as WebDAVSettings;

    [ConfigurationProperty(AllowModificationOfOwnedResourcesElement, IsDefaultCollection = false)]
    [ConfigurationCollection(typeof(OwnedResourcesPrincipalCollection))]
    public OwnedResourcesPrincipalCollection AllowModificationOfOwnedResources
    {
        get => (OwnedResourcesPrincipalCollection)base[AllowModificationOfOwnedResourcesElement];
        set => base[AllowModificationOfOwnedResourcesElement] = value;
    }

    [ConfigurationProperty(AllowModificationOfVivendiResourcesElement, IsDefaultCollection = false)]
    [ConfigurationCollection(typeof(VivendiResourcesPrincipalCollection))]
    public VivendiResourcesPrincipalCollection AllowModificationOfVivendiResources
    {
        get => (VivendiResourcesPrincipalCollection)base[AllowModificationOfVivendiResourcesElement];
        set => base[AllowModificationOfVivendiResourcesElement] = value;
    }
}
