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

using Aufbauwerk.Tools.Vivendi;
using System;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Web;

public static class WebDAVContextExtensions
{
    public static VivendiDocument GetDocument(this HttpContext context) => GetDocumentInternal(context, context.Request.Url);

    public static VivendiDocument GetDocument(this HttpContext context, Uri uri) => GetDocumentInternal(context, context.VerifyUri(uri));

    private static VivendiDocument GetDocumentInternal(HttpContext context, Uri uri) => GetResourceInternal(context, uri) as VivendiDocument ?? throw WebDAVException.ResourceCollectionsImmutable();

    public static Uri GetHref(this HttpContext context, VivendiResource resource)
    {
        // get the suffix and prefix
        var suffix = resource.Path;
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

        // terminate collections with a trailing slash and return the URI
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
        if (session[variableName] is T firstTryValue)
        {
            return firstTryValue;
        }

        // not found, create the value and try again, this time with a lock
        var createdValue = variableCreator(context);
        lock (session.SyncRoot)
        {
            if (session[variableName] is T secondTryValue)
            {
                return secondTryValue;
            }

            // still not set so do it now
            session[variableName] = createdValue;
            return createdValue;
        }
    }

    private static VivendiCollection GetRoot(HttpContext context, string userName, bool nested) => GetSessionVariable(context, "Vivendi-" + userName, c =>
    {
        var connectionStrings = ((VivendiSource[])Enum.GetValues(typeof(VivendiSource))).ToDictionary(_ => _, v => ConfigurationManager.ConnectionStrings[v.ToString()].ConnectionString);

        VivendiCollection result;
        Vivendi vivendi;
        if (nested)
        {
            result = VivendiCollection.CreateStaticRoot();
            vivendi = result.AddVivendi
            (
                name: userName,
                userName: userName,
                connectionStrings: connectionStrings
            );
        }
        else
        {
            result = vivendi = Vivendi.CreateRoot(userName, connectionStrings);
        }
        vivendi.AddBereiche();
        vivendi.AddKlienten().AddKlienten("(Alle Klienten)").ShowAll = true;
        vivendi.AddMitarbeiter().AddMitarbeiter("(Alle Mitarbeiter)").ShowAll = true;
        return result;
    });

    public static VivendiDocument? TryGetDocument(this HttpContext context, out VivendiCollection parentCollection, out string name) => TryGetDocumentInternal(context, context.Request.Url, out parentCollection, out name);

    public static VivendiDocument? TryGetDocument(this HttpContext context, Uri uri, out VivendiCollection parentCollection, out string name) => TryGetDocumentInternal(context, context.VerifyUri(uri), out parentCollection, out name);

    private static VivendiDocument? TryGetDocumentInternal(HttpContext context, Uri uri, out VivendiCollection parentCollection, out string name)
    {
        // try to get the resource
        var res = TryGetResourceInternal(context, uri, out parentCollection, out name, out var isCollection);
        if (res == null)
        {
            // if the URI ends with a slash also treat it as a collection
            if (isCollection)
            {
                throw WebDAVException.ResourceCollectionsImmutable();
            }
            return null;
        }

        // ensure the value is a document
        return res as VivendiDocument ?? throw WebDAVException.ResourceCollectionsImmutable();
    }

    public static VivendiResource? TryGetResource(this HttpContext context) => TryGetResourceInternal(context, context.Request.Url, out _, out _, out _);

    public static VivendiResource? TryGetResource(this HttpContext context, Uri uri) => TryGetResourceInternal(context, context.VerifyUri(uri), out _, out _, out _);

    private static VivendiResource? TryGetResourceInternal(HttpContext context, Uri uri, out VivendiCollection parentCollection, out string name, out bool isCollection)
    {
        // ensure authentication
        if (!context.User.Identity.IsAuthenticated)
        {
            throw new UnauthorizedAccessException();
        }
        var userName = context.User.Identity.Name;
        if (string.IsNullOrEmpty(userName))
        {
            throw new UnauthorizedAccessException();
        }

        // ensure the URI refers to the same store
        var localPath = uri.LocalPath;
        var prefix = context.Request.ApplicationPath;
        if (!localPath.StartsWith(prefix, Vivendi.PathComparison) || (localPath = localPath.Substring(prefix.Length)).Length > 0 && localPath[0] != '/')
        {
            throw WebDAVException.RequestDifferentStore(uri);
        }

        // check for empty segments before splitting
        if (localPath.Contains("//"))
        {
            throw WebDAVException.RequestInvalidPath(uri);
        }
        var segments = localPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        // super users specify the real user in the first segment
        if (ConfigurationManager.AppSettings.GetValues("SuperUser")?.Any(su => string.Equals(userName, su, StringComparison.OrdinalIgnoreCase)) ?? false)
        {
            if (segments.Length == 0)
            {
                // the Windows Redirector does not like it if there is no root
                parentCollection = VivendiCollection.CreateStaticRoot();
            }
            else
            {
                parentCollection = GetRoot(context, segments[0], true);
            }
        }
        else
        {
            // strip away the domain part if there is one
            var domainSep = userName.IndexOf('\\');
            parentCollection = GetRoot(context, domainSep > -1 ? userName.Substring(domainSep + 1) : userName, false);
        }

        // traverse all parts starting at the root
        name = string.Empty;
        isCollection = localPath.Length > 0 && localPath[localPath.Length - 1] == '/';
        var result = parentCollection as VivendiResource;
        foreach (var segment in segments)
        {
            // ensure that the parent is a collection and get the next child
            parentCollection = result as VivendiCollection ?? throw WebDAVException.ResourceParentNotFound(uri);
            name = segment;
            result = parentCollection.GetChild(name);
        }

        // ensure that no document URI ends in a trailing slash
        if (isCollection && result != null && !(result is VivendiCollection))
        {
            throw WebDAVException.ResourceParentNotFound(uri);
        }
        return result;
    }

    public static Uri VerifyUri(this HttpContext context, Uri uri)
    {
        // if it's a relative URI, return an absolute one
        if (!uri.IsAbsoluteUri)
        {
            return new Uri(context.Request.Url, uri);
        }

        // ensure the URI is at the same server
        if (!new Uri(context.Request.Url, "/").IsBaseOf(uri))
        {
            throw WebDAVException.RequestDifferentStore(uri);
        }
        return uri;
    }
}
