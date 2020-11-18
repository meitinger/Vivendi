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
    private const string AllowModificationOfOwnedResourcesElement = "allowModificationOfOwnedResources";
    private const string AllowModificationOfVivendiResourcesElement = "allowModificationOfVivendiResources";
    private const string InstanceElement = "webDAVSettings";

    public sealed class OwnedResourcesPrincipalCollection : PrincipalCollection
    {
        private const string ManagersAttribute = "managers";
        private const string TeamAttribute = "team";

        [ConfigurationProperty(ManagersAttribute, DefaultValue = true)]
        public bool Managers => (bool)this[ManagersAttribute];

        [ConfigurationProperty(TeamAttribute, DefaultValue = false)]
        public bool Team => (bool)this[TeamAttribute];
    }

    public abstract class PrincipalCollection : ConfigurationElementCollection, IEnumerable<PrincipalElement>
    {
        protected override ConfigurationElement CreateNewElement() => new PrincipalElement();

        protected override Object GetElementKey(ConfigurationElement element) => ((PrincipalElement)element).Name;

        public new IEnumerator<PrincipalElement> GetEnumerator()
        {
            // this.Cast<>(PrincipalElement).GetEnumerator() doesn't work
            var enumerator = base.GetEnumerator();
            while (enumerator.MoveNext())
            {
                yield return (PrincipalElement)enumerator.Current;
            }
        }
    }

    public sealed class PrincipalElement : ConfigurationElement
    {
        private const string NameAttribute = "name";
        private const string TypeAttribute = "type";

        [ConfigurationProperty(NameAttribute, IsRequired = true, IsKey = true)]
        public string Name => (string)this[NameAttribute];

        [ConfigurationProperty(TypeAttribute, IsRequired = true)]
        public PrincipalType Type => (PrincipalType)this[TypeAttribute];
    }

    public enum PrincipalType
    {
        User,
        Group,
    }

    public sealed class VivendiResourcesPrincipalCollection : PrincipalCollection { }

    public static WebDAVSettings Instance { get; } = ConfigurationManager.GetSection(InstanceElement) as WebDAVSettings;

    [ConfigurationProperty(AllowModificationOfOwnedResourcesElement, IsDefaultCollection = false)]
    [ConfigurationCollection(typeof(OwnedResourcesPrincipalCollection))]
    public OwnedResourcesPrincipalCollection AllowModificationOfOwnedResources => (OwnedResourcesPrincipalCollection)base[AllowModificationOfOwnedResourcesElement];

    [ConfigurationProperty(AllowModificationOfVivendiResourcesElement, IsDefaultCollection = false)]
    [ConfigurationCollection(typeof(VivendiResourcesPrincipalCollection))]
    public VivendiResourcesPrincipalCollection AllowModificationOfVivendiResources => (VivendiResourcesPrincipalCollection)base[AllowModificationOfVivendiResourcesElement];
}
