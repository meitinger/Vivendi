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

using Aufbauwerk.Tools.Vivendi;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.DirectoryServices;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Web;
using static System.FormattableString;

public static class WebDAVContextExtensions
{
    public static VivendiDocument GetDocument(this HttpContext context) => GetDocumentInternal(context, context.Request.Url);

    public static VivendiDocument GetDocument(this HttpContext context, Uri uri) => GetDocumentInternal(context, context.VerifyUri(uri));

    private static VivendiDocument GetDocumentInternal(HttpContext context, Uri uri) => GetResourceInternal(context, uri) as VivendiDocument ?? throw WebDAVException.ResourceCollectionsImmutable();

    public static Uri GetHref(this HttpContext context, VivendiResource resource)
    {
        // get the suffix and prefix
        var suffix = (resource ?? throw new ArgumentNullException(nameof(resource))).Path;
        var prefix = context.Request.ApplicationPath;
        var path = new StringBuilder();

        // append the paths if they are not roots
        if (prefix.Length > 1)
        {
            path.Append(prefix);
        }
        if (suffix.Length > 1)
        {
            path.Append(suffix);
        }

        // terminate collections with a trailing slash and return the uri
        if (resource is VivendiCollection)
        {
            path.Append("/");
        }
        return new Uri(context.Request.Url, path.ToString());
    }

    public static Uri GetRelativeHref(this HttpContext context, VivendiResource resource) => context.Request.Url.MakeRelativeUri(context.GetHref(resource));

    public static VivendiResource GetResource(this HttpContext context) => GetResourceInternal(context, context.Request.Url);

    public static VivendiResource GetResource(this HttpContext context, Uri uri) => GetResourceInternal(context, context.VerifyUri(uri));

    private static VivendiResource GetResourceInternal(HttpContext context, Uri uri) => TryGetResourceInternal(context, uri, out _, out _, out _) ?? throw WebDAVException.ResourceNotFound(uri);

    private static T GetSessionVariable<T>(HttpContext context, string variableName, Func<HttpContext, T> variableCreator) where T : class
    {
        // quickly try to get the value without a lock
        var session = context.Session;
        if (!(session[variableName] is T variable))
        {
            // not found, create the value and try again, this time with a lock
            var value = variableCreator(context);
            lock (session.SyncRoot)
            {
                variable = session[variableName] as T;
                if (variable == null)
                {
                    // still not set so do it now
                    session[variableName] = variable = value;
                }
            }
        }
        return variable;
    }

    private static Vivendi GetVivendi(HttpContext context) => GetSessionVariable(context, "Vivendi", c =>
    {
        // query all principal properties
        var principal = c.User as WindowsPrincipal ?? throw new UnauthorizedAccessException();
        var identity = principal.Identity as WindowsIdentity ?? throw new UnauthorizedAccessException();
        var sid = identity.User ?? throw new UnauthorizedAccessException();
        var userName = identity.Name ?? throw new UnauthorizedAccessException();

        // create Vivendi (strip away the domain name from the user name)
        var domainSep = userName.IndexOf('\\');
        var vivendi = new Vivendi
        (
            domainSep > -1 ? userName.Substring(domainSep + 1) : userName,
            sid,
            ((VivendiSource[])Enum.GetValues(typeof(VivendiSource))).ToDictionary(_ => _, v => ConfigurationManager.ConnectionStrings[v.ToString()].ConnectionString)
        );
        vivendi.AddBereiche();
        vivendi.AddKlienten().AddKlienten("(Alle Klienten)").ShowAll = true;
        vivendi.AddMitarbeiter().AddMitarbeiter("(Alle Mitarbeiter)").ShowAll = true;

        // local method to check if a given principal collection contains the current user
        bool isInList(WebDAVSettings.PrincipalCollection principals) => principals.Any(p => p.Type switch
        {
            WebDAVSettings.PrincipalType.User => string.Equals(p.Name, userName, StringComparison.OrdinalIgnoreCase),
            WebDAVSettings.PrincipalType.Group => principal.IsInRole(p.Name),
            _ => false,
        });

        // check if the user is allowed to modify any owned instance
        if (isInList(WebDAVSettings.Instance.AllowModificationOfOwnedResources))
        {
            vivendi.AllowModificationOfOwnedResource = (_, owner) => true;
        }
        else if (WebDAVSettings.Instance.AllowModificationOfOwnedResources.Managers || WebDAVSettings.Instance.AllowModificationOfOwnedResources.Team)
        {
            // query the distinguishedName and manager of the current user
            using var dnSearcher = new DirectorySearcher(Invariant($"(objectSid={sid})"), new string[] { "distinguishedName", "manager" });
            var user = dnSearcher.FindOne();
            if (user != null)
            {
                // build a list of owners that can be accessed
                var allowedOwners = new HashSet<SecurityIdentifier>();
                void addSids(string propertyName, Func<string, string> queryBuilder)
                {
                    // search the domain and add the object's sids if the property exists
                    var property = user.Properties[propertyName].OfType<string>().FirstOrDefault();
                    if (property != null)
                    {
                        using var searcher = new DirectorySearcher(queryBuilder(property), new string[] { "objectSid" }) { PageSize = 1000 };
                        allowedOwners.UnionWith(searcher.FindAll().Cast<SearchResult>().SelectMany(r => r.Properties["objectSid"].OfType<byte[]>()).Select(b => new SecurityIdentifier(b, 0)));
                    }
                }
                if (WebDAVSettings.Instance.AllowModificationOfOwnedResources.Managers)
                {
                    addSids("distinguishedName", distinguishedName => Invariant($"(manager:1.2.840.113556.1.4.1941:={distinguishedName})"));
                }
                if (WebDAVSettings.Instance.AllowModificationOfOwnedResources.Team)
                {
                    addSids("manager", manager => Invariant($"(manager={manager})"));
                }
                vivendi.AllowModificationOfOwnedResource = (_, owner) => allowedOwners.Contains(owner);
            }
        }

        // check if the user is allowed to modify Vivendi resources
        if (isInList(WebDAVSettings.Instance.AllowModificationOfVivendiResources))
        {
            vivendi.AllowModificationOfVivendiResource = _ => true;
        }
        return vivendi;
    });

    public static VivendiDocument TryGetDocument(this HttpContext context, out VivendiCollection parentCollection, out string name) => TryGetDocumentInternal(context, context.Request.Url, out parentCollection, out name);

    public static VivendiDocument TryGetDocument(this HttpContext context, Uri uri, out VivendiCollection parentCollection, out string name) => TryGetDocumentInternal(context, context.VerifyUri(uri), out parentCollection, out name);

    private static VivendiDocument TryGetDocumentInternal(HttpContext context, Uri uri, out VivendiCollection parentCollection, out string name)
    {
        // try to get the resource
        var res = TryGetResourceInternal(context, uri, out parentCollection, out name, out var isCollection);
        if (res == null)
        {
            // if the uri ends with a slash also treat it as a collection
            if (isCollection)
            {
                throw WebDAVException.ResourceCollectionsImmutable();
            };
            return null;
        }

        // ensure the value is a document
        return res as VivendiDocument ?? throw WebDAVException.ResourceCollectionsImmutable();
    }

    public static VivendiResource TryGetResource(this HttpContext context) => TryGetResourceInternal(context, context.Request.Url, out _, out _, out _);

    public static VivendiResource TryGetResource(this HttpContext context, Uri uri) => TryGetResourceInternal(context, context.VerifyUri(uri), out _, out _, out _);

    private static VivendiResource TryGetResourceInternal(HttpContext context, Uri uri, out VivendiCollection parentCollection, out string name, out bool isCollection)
    {
        // ensure the uri refers to the same store
        var vivendi = GetVivendi(context);
        var localPath = uri.LocalPath;
        var prefix = context.Request.ApplicationPath;
        if (!localPath.StartsWith(prefix, Vivendi.PathComparison) || (localPath = localPath.Substring(prefix.Length)).Length > 0 && localPath[0] != '/')
        {
            throw WebDAVException.RequestDifferentStore(uri);
        }

        // check for empty name parts
        if (localPath.Contains("//"))
        {
            throw WebDAVException.RequestInvalidPath(uri);
        }

        // traverse all parts starting at the root
        var names = localPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        parentCollection = vivendi;
        name = null;
        isCollection = localPath.Length > 0 && localPath[localPath.Length - 1] == '/';
        var result = vivendi as VivendiResource;
        for (var i = 0; i < names.Length; i++)
        {
            // ensure that the parent is a collection and get the next child
            parentCollection = result as VivendiCollection ?? throw WebDAVException.ResourceParentNotFound(uri);
            name = names[i];
            result = parentCollection.GetChild(name);
        }

        // ensure that no document uri ends in a trailing slash
        if (isCollection && result != null && !(result is VivendiCollection))
        {
            throw WebDAVException.ResourceParentNotFound(uri);
        }
        return result;
    }

    public static Uri VerifyUri(this HttpContext context, Uri uri)
    {
        // if it's a relative uri, return an absolute one
        if (!(uri ?? throw new ArgumentNullException(nameof(uri))).IsAbsoluteUri)
        {
            return new Uri(context.Request.Url, uri);
        }

        // ensure the uri is at the same server
        if (!new Uri(context.Request.Url, "/").IsBaseOf(uri))
        {
            throw WebDAVException.RequestDifferentStore(uri);
        }
        return uri;
    }
}
