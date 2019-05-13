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
using static System.FormattableString;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.SessionState;
using System.Xml;
using Aufbauwerk.Tools.Vivendi;

public abstract class WebDAVHandler : IHttpHandler, IRequiresSessionState
{
    protected const string DAV = "DAV:";

    public bool IsReusable => true;

    public void ProcessRequest(HttpContext context)
    {
        // process the request
        try { context.Response.StatusCode = (int)ProcessRequestInternal(context); }
        catch (VivendiException e) { HandleException(context, WebDAVException.FromVivendiException(e)); }
        catch (WebDAVException e) { HandleException(context, e); }
    }

    protected abstract HttpStatusCode ProcessRequestInternal(HttpContext context);

    protected XmlElement ReadXml(HttpContext context, string documentElementName)
    {
        // parse the request as XML
        var doc = new XmlDocument();
        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
        {
            try
            {
                doc.Load(reader);
            }
            catch (XmlException)
            {
                throw WebDAVException.RequestInvalidXml();
            }
        }

        // ensure and return the proper document element
        var documentElement = doc.DocumentElement;
        if (documentElement == null || documentElement.LocalName != documentElementName || documentElement.NamespaceURI != DAV)
        {
            throw WebDAVException.RequestXmlInvalidRootElement(documentElementName);
        }
        return documentElement;
    }

    void HandleException(HttpContext context, WebDAVException e)
    {
        context.Response.TrySkipIisCustomErrors = true;
        context.Response.StatusCode = e.StatusCode;
        if (e.PostConditionCode != null)
        {
            var doc = new XmlDocument();
            doc.AppendChild(doc.CreateElement("error", DAV)).AppendChild(doc.CreateElement(e.PostConditionCode, DAV));
            WriteXml(context, doc);
        }
        else
        {
            context.Response.Write(e.Message);
        }
    }

    protected void WriteXml(HttpContext context, XmlDocument doc)
    {
        // write the document
        context.Response.ContentType = "application/xml";
        context.Response.ContentEncoding = Encoding.UTF8;
        using (var writer = new StreamWriter(context.Response.OutputStream, context.Response.ContentEncoding))
        {
            doc.Save(writer);
        }
    }
}

public abstract class WebDAVCopyAndMoveHandler : WebDAVHandler
{
    protected sealed override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // get the source and destination
        var source = context.GetDocument();
        var destinationHeader = context.Request.Headers["Destination"];
        if (string.IsNullOrEmpty(destinationHeader) || !Uri.TryCreate(destinationHeader, UriKind.RelativeOrAbsolute, out var destinationUri))
        {
            throw WebDAVException.RequestHeaderInvalidDestination();
        }
        var destination = context.TryGetDocument(destinationUri, out var destinationCollection, out var destinationName);

        // check if there already is such a document
        if (destination != null)
        {
            // fail if it's not a document or the same path
            if (source.Path == destination.Path)
            {
                throw WebDAVException.ResourceIsIdentical();
            }

            // ensure override is allowed and delete the old document
            if (!string.Equals(context.Request.Headers["Overwrite"], "T", StringComparison.OrdinalIgnoreCase))
            {
                throw WebDAVException.ResourceAlreadyExists();
            }
            destination.Delete();
        }

        // if the destination name is the type/id name of the source use the display name instead
        if (source.Type != VivendiResourceType.Named && string.Equals(source.Name, destinationName, Vivendi.PathComparison))
        {
            destinationName = source.DisplayName;
        }

        // copy or move the source document to the destination
        PerformOperation(source, destinationUri, destinationCollection, destinationName);

        // return success and inidicate whether a document was replaces
        return destination == null ? HttpStatusCode.Created : HttpStatusCode.NoContent;
    }

    protected abstract void PerformOperation(VivendiDocument sourceDoc, Uri destUri, VivendiCollection destCollection, string destName);
}

public abstract class WebDAVGetAndHeadHandler : WebDAVHandler
{
    protected sealed override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // get the resource and always return the last modified time
        var resource = context.GetResource();
        context.Response.AppendHeader("Last-Modified", resource.LastModified.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture));
        var doc = resource as VivendiDocument;
        if (doc != null)
        {
            context.Response.AppendHeader("Content-Type", doc.ContentType);
            context.Response.AppendHeader("Content-Length", doc.Size.ToString(CultureInfo.InvariantCulture));
        }
        return ProcessRequestInternal(context, resource);
    }

    protected abstract HttpStatusCode ProcessRequestInternal(HttpContext context, VivendiResource resource);
}

public abstract class WebDAVPropHandler : WebDAVHandler
{
    protected abstract class Property
    {
        static readonly IDictionary<PropertyName, Property> _properties = new Dictionary<PropertyName, Property>();

        public static IEnumerable<Property> All => _properties.Values;

        public static Property FromName(PropertyName name) => _properties.TryGetValue(name, out var property) ? property : null;

        protected static void Register(Property property) => _properties.Add(property.Name, property);

        protected Property(PropertyName name, bool isProtected)
        {
            Name = name;
            IsProtected = isProtected;
        }

        public bool IsProtected { get; }

        public PropertyName Name { get; }

        public abstract void Get(VivendiResource resource, XmlElement value);

        public abstract bool IsApplicable(VivendiResource resource);

        public abstract void Set(VivendiResource resource, XmlElement value);
    }

    protected class Property<T> : Property where T : VivendiResource
    {
        static readonly Action<T, XmlElement> ProtectedSetter = (r, e) => throw WebDAVException.PropertyIsProtected();

        readonly Action<T, XmlElement> _getter;
        readonly Action<T, XmlElement> _setter;

        private Property(string namespaceURI, string localName, bool isProtected, Action<T, XmlElement> getter, Action<T, XmlElement> setter)
        : base(new PropertyName(namespaceURI, localName), isProtected)
        {
            _getter = getter ?? throw new ArgumentNullException("getter");
            _setter = setter ?? throw new ArgumentNullException("setter");
        }

        public static void Register(string namespaceURI, string localName, Action<T, XmlElement> getter) => Register(new Property<T>(namespaceURI, localName, true, getter, ProtectedSetter));

        public static void Register(string namespaceURI, string localName, Action<T, XmlElement> getter, Action<T, XmlElement> setter) => Register(new Property<T>(namespaceURI, localName, false, getter, setter));

        public override void Get(VivendiResource resource, XmlElement value) => _getter(resource as T ?? throw new ArgumentOutOfRangeException("resource"), value);

        public override bool IsApplicable(VivendiResource resource) => resource is T;

        public override void Set(VivendiResource resource, XmlElement value) => _setter(resource as T ?? throw new ArgumentOutOfRangeException("resource"), value);
    }

    protected struct PropertyName
    {
        public PropertyName(string namespaceURI, string localName)
        {
            NamespaceURI = namespaceURI ?? throw new ArgumentNullException(namespaceURI);
            LocalName = localName ?? throw new ArgumentNullException(localName);
        }

        public PropertyName(XmlElement e)
        {
            NamespaceURI = e.NamespaceURI;
            LocalName = e.LocalName;
        }

        public readonly string NamespaceURI;
        public readonly string LocalName;
    }

    static WebDAVPropHandler()
    {
        // register all known properties
        Property<VivendiResource>.Register(DAV, "creationdate", (r, e) => e.InnerText = ToTimestamp(r.CreationDate), (r, e) => r.CreationDate = FromTimestamp(e.InnerText));
        Property<VivendiResource>.Register(DAV, "displayname", (r, e) => e.InnerText = r.DisplayName, (r, e) => r.DisplayName = e.InnerText);
        Property<VivendiResource>.Register(DAV, "getlastmodified", (r, e) => e.InnerText = ToRTT(r.LastModified), (r, e) => r.LastModified = FromRTT(e.InnerText));
        Property<VivendiResource>.Register(DAV, "resourcetype", (r, e) => { if (r is VivendiCollection) { e.AppendChild(e.OwnerDocument.CreateElement("collection", e.NamespaceURI)); } });
        Property<VivendiResource>.Register(DAV, "ishidden", (r, e) => e.InnerText = (r.Attributes & FileAttributes.Hidden) == 0 ? "0" : "1");
        Property<VivendiResource>.Register(MS, "Win32FileAttributes", (r, e) => e.InnerText = ToHex((int)r.Attributes), (r, e) => r.Attributes = (FileAttributes)FromHex(e.InnerText));
        Property<VivendiResource>.Register(MS, "Win32CreationTime", (r, e) => e.InnerText = ToRTT(r.CreationDate), (r, e) => r.CreationDate = FromRTT(e.InnerText));
        Property<VivendiResource>.Register(MS, "Win32LastAccessTime", (r, e) => e.InnerText = ToRTT(DateTime.Now));
        Property<VivendiResource>.Register(MS, "Win32LastModifiedTime", (r, e) => e.InnerText = ToRTT(r.LastModified), (r, e) => r.LastModified = FromRTT(e.InnerText));
        Property<VivendiDocument>.Register(DAV, "getcontentlength", (d, e) => e.InnerText = d.Size.ToString(CultureInfo.InvariantCulture));
        Property<VivendiDocument>.Register(DAV, "getcontenttype", (d, e) => e.InnerText = d.ContentType);
    }

    static readonly DateTime MaxDate = new DateTime(2107, 12, 31, 23, 59, 59, DateTimeKind.Utc);
    static readonly DateTime MinDate = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    protected const string MS = "urn:schemas-microsoft-com:";
    const string TimestampFormat = @"yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'";

    protected HttpStatusCode ProcessRequestInternal(HttpContext context, IEnumerable<VivendiResource> resources, IEnumerable<PropertyName> propertyNames, Action<Property, VivendiResource, XmlElement> action)
    {
        // create the mulistatus response and handle each resource
        var doc = new XmlDocument();
        var multistatusElement = doc.AppendChild(doc.CreateElement("multistatus", DAV));
        foreach (var resource in resources)
        {
            // append the link to the resource
            var errorMap = new Dictionary<WebDAVException, ICollection<XmlElement>>();
            var responseElement = multistatusElement.AppendChild(doc.CreateElement("response", DAV));
            responseElement.AppendChild(doc.CreateElement("href", DAV)).InnerText = context.GetHref(resource).AbsoluteUri;

            // handle each requested property
            foreach (var propertyName in propertyNames)
            {
                // create the element and find the property
                var valueElement = doc.CreateElement(propertyName.LocalName, propertyName.NamespaceURI);
                var result = WebDAVException.PropertyOperationSuccessful();
                var property = Property.FromName(propertyName);

                // make sure the property exists and is applicable
                if (property == null)
                {
                    result = WebDAVException.PropertyNotFound();
                }
                else if (!property.IsApplicable(resource))
                {
                    continue;
                }
                else
                {
                    // perform the get, set or remove operation
                    try { action(property, resource, valueElement); }
                    catch (VivendiException e) { result = WebDAVException.FromVivendiException(e); }
                    catch (WebDAVException e) { result = e; }
                }

                // store the result
                if (!errorMap.TryGetValue(result, out var valueElements))
                {
                    errorMap.Add(result, valueElements = new LinkedList<XmlElement>());
                }
                valueElements.Add(valueElement);
            }

            // create a propstat for each different result
            foreach (var entry in errorMap)
            {
                // append all properties with the current result and the status element
                var propstatElement = responseElement.AppendChild(doc.CreateElement("propstat", DAV));
                var propElement = propstatElement.AppendChild(doc.CreateElement("prop", DAV));
                foreach (var valueElement in entry.Value)
                {
                    propElement.AppendChild(valueElement);
                }
                var statusCode = entry.Key.StatusCode;
                propstatElement.AppendChild(doc.CreateElement("status", DAV)).InnerText = Invariant($"HTTP/1.1 {statusCode} {HttpWorkerRequest.GetStatusDescription(statusCode)}");

                // append the error element if there is a postcondition present
                var postConditionCode = entry.Key.PostConditionCode;
                if (postConditionCode != null)
                {
                    propstatElement.AppendChild(doc.CreateElement("error", DAV)).AppendChild(doc.CreateElement(postConditionCode, DAV));
                }

                // apppend the responsedescription element, if there is a message
                var message = entry.Key.Message;
                if (message != null)
                {
                    propstatElement.AppendChild(doc.CreateElement("responsedescription", DAV)).InnerText = message;
                }
            }
        }

        // wtite the result and return multi-status
        WriteXml(context, doc);
        return (HttpStatusCode)207;
    }
    protected static int FromHex(string s) => int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var i) ? i : throw WebDAVException.PropertyInvalidHexNumber();
    protected static DateTime FromRTT(string s) => DateTime.TryParseExact(s, "R", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt.ToLocalTime() : throw WebDAVException.PropertyInvalidRountripTime();
    protected static DateTime FromTimestamp(string s) => DateTime.TryParseExact(s, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt.ToLocalTime() : throw WebDAVException.PropertyInvalidTimestamp();
    protected static string ToHex(int i) => i.ToString("X8", CultureInfo.InvariantCulture);
    protected static string ToRTT(DateTime dt) => ToUtcInBounds(dt).ToString("R", CultureInfo.InvariantCulture);
    protected static string ToTimestamp(DateTime dt) => ToUtcInBounds(dt).ToString(TimestampFormat, CultureInfo.InvariantCulture);
    static DateTime ToUtcInBounds(DateTime dt)
    {
        dt = dt.ToUniversalTime();
        if (dt < MinDate)
        {
            return MinDate;
        }
        if (dt > MaxDate)
        {
            return MaxDate;
        }
        return dt;
    }
}

public sealed class WebDAVCopyHandler : WebDAVCopyAndMoveHandler
{
    protected override void PerformOperation(VivendiDocument sourceDoc, Uri destUri, VivendiCollection destCollection, string destName) => destCollection.NewDocument(destName, sourceDoc.CreationDate, sourceDoc.LastModified, sourceDoc.Data);
}

public sealed class WebDAVDeleteHandler : WebDAVHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // get and delete the document
        var doc = context.GetDocument();
        doc.Delete();
        return HttpStatusCode.NoContent;
    }
}

public sealed class WebDAVGetHandler : WebDAVGetAndHeadHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context, VivendiResource resource)
    {
        // return the resource
        switch (resource)
        {
            case VivendiCollection collection: WriteCollection(context, collection); break;
            case VivendiDocument document: WriteDocument(context, document); break;
            default: return HttpStatusCode.NotImplemented;
        }
        return HttpStatusCode.OK;
    }

    void WriteCollection(HttpContext context, VivendiCollection collection)
    {
        // build the response html
        var response = new StringBuilder();
        response.AppendLine(@"<!DOCTYPE html>");
        response.Append(@"<html><head><meta charset=""utf-8""/><title>/");
        response.Append(HttpUtility.HtmlEncode(string.Join("/", collection.SelfAndAncestors.Select(c => c.DisplayName).Reverse().Skip(1))));
        response.Append(@"</title></head><body>");
        if (collection.Type != VivendiResourceType.Root)
        {
            response.Append(@"<a href=""").Append(context.GetRelativeHref(collection.Parent)).Append(@""">[..]</a><br/>");
        }

        // write each readable resource ordered by name
        foreach (var res in collection.Children.Where(r => (r.Attributes & FileAttributes.Hidden) == 0).OrderBy(r => r.DisplayName, StringComparer.InvariantCultureIgnoreCase))
        {
            response.Append(@"<a href=""").Append(context.GetRelativeHref(res)).Append(@""">").Append(HttpUtility.HtmlEncode(res.DisplayName)).Append(@"</a><br/>");
        }

        // final tags and write
        response.Append(@"</body></html>");
        context.Response.Write(response);
    }

    void WriteDocument(HttpContext context, VivendiDocument document)
    {
        context.Response.AppendHeader("Content-Disposition", Invariant($"attachment; filename*=UTF-8''{Uri.EscapeDataString(document.DisplayName)}"));
        using (var stream = new MemoryStream(document.Data))
        {
            stream.CopyTo(context.Response.OutputStream);
        }
    }
}

public sealed class WebDAVHeadHandler : WebDAVGetAndHeadHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context, VivendiResource resource) => HttpStatusCode.OK;
}

public sealed class WebDAVMkColHandler : WebDAVHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // try to find the resource and return the approriate response if there is one
        var res = context.TryGetResource();
        if (res != null)
        {
            return HttpStatusCode.MethodNotAllowed;
        }

        // we dont't support creations of collections
        throw WebDAVException.ResourceCollectionsImmutable();
    }
}

public sealed class WebDAVMoveHandler : WebDAVCopyAndMoveHandler
{
    protected override void PerformOperation(VivendiDocument sourceDoc, Uri destUri, VivendiCollection destCollection, string destName) => sourceDoc.MoveTo(destCollection, destName);
}

public sealed class WebDAVOptionsHandler : WebDAVHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // let the client know what methods we support
        context.Response.AppendHeader("DAV", "1, 3");
        context.Response.AppendHeader("Allow", "COPY, DELETE, GET, HEAD, MKCOL, MOVE, OPTIONS, PROPFIND, PROPPATCH, PUT");
        return HttpStatusCode.OK;
    }
}

public sealed class WebDAVPropFindHandler : WebDAVPropHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // initialize the variables and check if there is a request body
        var resource = context.GetResource();
        var allpropCount = 0;
        var propnameCount = 0;
        var propCount = 0;
        var propertyNames = Property.All.Select(p => p.Name);
        if (context.Request.ContentLength == 0)
        {
            // handle an empty request as allprop
            allpropCount = 1;
        }
        else
        {
            // determine what properties should be queried
            var propfindElement = ReadXml(context, "propfind");
            foreach (var element in propfindElement.ChildNodes.OfType<XmlElement>().Where(e => e.NamespaceURI == DAV))
            {
                switch (element.LocalName)
                {
                    case "allprop":
                        allpropCount++;
                        break;
                    case "propname":
                        propnameCount++;
                        break;
                    case "prop":
                        propCount++;
                        propertyNames = element.ChildNodes.OfType<XmlElement>().Select(e => new PropertyName(e)).Distinct();
                        break;
                }
            }

            // ensure the request is valid
            if (allpropCount + propnameCount + propCount != 1)
            {
                throw WebDAVException.RequestXmlInvalidPropfindElement();
            }
        }

        // check the requested depth
        var resources = Enumerable.Repeat(resource, 1);
        switch (context.Request.Headers["Depth"])
        {
            case null:
            case "":
            case "infinity":
                // return a more informative error that infinite depths are not allowed
                throw WebDAVException.RequestHeaderInifiniteDepthNotSupported();
            case "1":
                // also return all children if the resource is a collection
                var collection = resource as VivendiCollection;
                if (collection != null)
                {
                    resources = resources.Concat(collection.Children);
                }
                break;
            case "0":
                break;
            default:
                throw WebDAVException.RequestHeaderInvalidDepth();
        }

        // build the response
        return ProcessRequestInternal(context, resources, propertyNames, (prop, res, val) => { if (propnameCount == 0) { prop.Get(res, val); } });
    }
}

// HACK: PROPPATCH should be atomic, but this just goes beyond this project
public sealed class WebDAVPropPatchHandler : WebDAVPropHandler
{
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // determine which elements should be set or removed
        var resources = Enumerable.Repeat(context.GetResource(), 1);
        var propertyupdateElement = ReadXml(context, "propertyupdate");
        var actions = new Dictionary<PropertyName, XmlElement>();
        foreach (var element in propertyupdateElement.ChildNodes.OfType<XmlElement>().Where(e => e.NamespaceURI == DAV))
        {
            // check if it's a set or remove operation
            bool isSet;
            switch (element.LocalName)
            {
                case "set":
                    isSet = true;
                    break;
                case "remove":
                    isSet = false;
                    break;
                default:
                    continue;
            }

            // find the single prop child
            var propElements = element.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "prop" && e.NamespaceURI == DAV);
            if (propElements.Count() != 1)
            {
                throw WebDAVException.RequestXmlInvalidSetOrRemoveElement();
            }

            // store all property names and their values
            foreach (var valueElement in propElements.Single().ChildNodes.OfType<XmlElement>())
            {
                actions[new PropertyName(valueElement)] = isSet ? valueElement : null;
            }
        }

        // ensure there is something to do
        if (actions.Count == 0)
        {
            throw WebDAVException.RequestXmlInvalidProperyUpdateEement();
        }

        // perform the actions and build the response
        return ProcessRequestInternal(context, resources, actions.Keys, (prop, res, _) => prop.Set(res, actions[prop.Name] ?? throw WebDAVException.PropertyNotRemovable()));
    }
}

public sealed class WebDAVPutHandler : WebDAVHandler
{
    byte[] ReadAllBytes(HttpContext context)
    {
        // read all uploaded data
        var input = context.Request.InputStream;
        using (var stream = new MemoryStream((int)input.Length))
        {
            input.CopyTo(stream);
            var buffer = stream.GetBuffer();
            return buffer.Length == stream.Length ? buffer : stream.ToArray();
        }
    }

    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        // check if a document under the current uri already exists
        var doc = context.TryGetDocument(out var collection, out var name);
        if (doc != null)
        {
            // replace the data
            doc.Data = ReadAllBytes(context);
        }
        else
        {
            // create the new document
            var creationDate = DateTime.Now;
            if (DateTime.TryParseExact(context.Request.Headers["Last-Modified"], "R", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastModified))
            {
                lastModified = lastModified.ToLocalTime();
            }
            else
            {
                lastModified = creationDate;
            }
            collection.NewDocument(name, creationDate, lastModified, ReadAllBytes(context));
        }
        return HttpStatusCode.NoContent;
    }
}

public sealed class WebDAVUnsupportedHandler : WebDAVHandler
{
    // we don't implement locking
    protected override HttpStatusCode ProcessRequestInternal(HttpContext context)
    {
        return HttpStatusCode.NotImplemented;
    }
}
