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
using System.Net;
using Aufbauwerk.Tools.Vivendi;

public sealed class WebDAVException : Exception
{
    const int ERROR_BAD_ARGUMENTS = 160;
    const int ERROR_BAD_PATHNAME = 161;
    const int ERROR_FILE_EXISTS = 80;
    const int ERROR_FILE_NOT_FOUND = 2;
    const int ERROR_FILE_TOO_LARGE = 223;
    const int ERROR_INVALID_PARAMETER = 87;
    const int ERROR_NOT_FOUND = 1168;
    const int ERROR_NOT_SAME_DEVICE = 17;
    const int ERROR_NOT_SUPPORTED = 50;
    const int ERROR_PATH_NOT_FOUND = 3;
    const int ERROR_SHARING_VIOLATION = 32;
    const int ERROR_SUCCESS = 0;
    const int ERROR_WRITE_PROTECT = 19;

    public static WebDAVException FromVivendiException(VivendiException e) => new WebDAVException(e.ErrorCode == ERROR_FILE_TOO_LARGE ? (HttpStatusCode)507 : HttpStatusCode.Forbidden, e.ErrorCode, e.Message);

    internal static WebDAVException RequestDifferentStore(Uri uri) => new WebDAVException(HttpStatusCode.BadRequest, ERROR_NOT_SAME_DEVICE, $"The URI '{uri}' refers to a different Vivendi store.");
    internal static WebDAVException RequestHeaderInifiniteDepthNotSupported() => new WebDAVException(HttpStatusCode.Forbidden, ERROR_NOT_SUPPORTED, "Infinite depth requests are not supported.", "propfind-finite-depth");
    static WebDAVException RequestHeaderInvalid(string message) => new WebDAVException(HttpStatusCode.BadRequest, ERROR_BAD_ARGUMENTS, message);
    internal static WebDAVException RequestHeaderInvalidDepth() => RequestHeaderInvalid("Only depths of '0', '1' and 'infinity' are supported.");
    internal static WebDAVException RequestHeaderInvalidDestination() => RequestHeaderInvalid("The destination header is missing or invalid.");
    internal static WebDAVException RequestInvalidPath(Uri uri) => new WebDAVException(HttpStatusCode.BadRequest, ERROR_BAD_PATHNAME, $"The path of URI '{uri}' is invalid.");
    internal static WebDAVException RequestInvalidXml() => new WebDAVException(HttpStatusCode.BadRequest, ERROR_BAD_ARGUMENTS, "The request contains invalid XML.");
    static WebDAVException RequestXmlInvalid(string message) => new WebDAVException((HttpStatusCode)422, ERROR_INVALID_PARAMETER, message);
    internal static WebDAVException RequestXmlInvalidProperyUpdateEement() => RequestXmlInvalid("The propertyupdate node must contain at least one set or remove element.");
    internal static WebDAVException RequestXmlInvalidPropfindElement() => RequestXmlInvalid("The propfind node must contain exactly one of allprop, propname or prop element.");
    internal static WebDAVException RequestXmlInvalidRootElement(string expectedName) => RequestXmlInvalid($"Root node must be {expectedName} element.");
    internal static WebDAVException RequestXmlInvalidSetOrRemoveElement() => RequestXmlInvalid("The set and remove nodes must contain exactly one prop element.");
    static WebDAVException PropertyInvalid(string message) => new WebDAVException(HttpStatusCode.Conflict, ERROR_INVALID_PARAMETER, message);
    internal static WebDAVException PropertyInvalidHexNumber() => PropertyInvalid($"Not a valid hexadecimal number.");
    internal static WebDAVException PropertyInvalidRountripTime() => PropertyInvalid($"Not a valid roundtrip time.");
    internal static WebDAVException PropertyInvalidTimestamp() => PropertyInvalid($"Not a valid timestamp value.");
    internal static WebDAVException PropertyIsProtected() => new WebDAVException(HttpStatusCode.Forbidden, ERROR_WRITE_PROTECT, "Cannot change a protected property.", "cannot-modify-protected-property");
    internal static WebDAVException PropertyNotFound() => new WebDAVException(HttpStatusCode.NotFound, ERROR_NOT_FOUND, "No property with this name exists.");
    internal static WebDAVException PropertyNotRemovable() => new WebDAVException(HttpStatusCode.NotImplemented, ERROR_NOT_SUPPORTED, "Removal of property is not supported.");
    internal static WebDAVException PropertyOperationSuccessful() => new WebDAVException(HttpStatusCode.OK, ERROR_SUCCESS, null);
    internal static WebDAVException ResourceAlreadyExists() => new WebDAVException(HttpStatusCode.PreconditionFailed, ERROR_FILE_EXISTS, "Resource is already present but overwrite header is not set.");
    internal static WebDAVException ResourceCollectionsImmutable() => new WebDAVException(HttpStatusCode.Forbidden, ERROR_NOT_SUPPORTED, "Only documents can be moved, copied, deleted, uploaded or replaced.");
    internal static WebDAVException ResourceIsIdentical() => new WebDAVException(HttpStatusCode.Forbidden, ERROR_SHARING_VIOLATION, "Source and destination are the same resource.");
    internal static WebDAVException ResourceNotFound(Uri uri) => new WebDAVException(HttpStatusCode.NotFound, ERROR_FILE_NOT_FOUND, $"The resource '{uri}' has not been found.");
    internal static WebDAVException ResourceParentNotFound(Uri uri) => new WebDAVException(HttpStatusCode.Conflict, ERROR_PATH_NOT_FOUND, $"Not all parent directories of URI '{uri}' exist.");

    WebDAVException(HttpStatusCode statusCode, int errorCode, string message, string postConditionCode = null)
    : base(message)
    {
        StatusCode = (int)statusCode;
        ErrorCode = errorCode;
        Message = message;
        PostConditionCode = postConditionCode;
    }

    public int ErrorCode { get; }

    public override string Message { get; }

    public string PostConditionCode { get; }

    public int StatusCode { get; }

    public override bool Equals(object obj) => obj != null &&
                                               obj.GetType() == GetType() &&
                                               ((WebDAVException)obj).ErrorCode == ErrorCode &&
                                               ((WebDAVException)obj).Message == Message &&
                                               ((WebDAVException)obj).PostConditionCode == PostConditionCode &&
                                               ((WebDAVException)obj).StatusCode == StatusCode;

    public override int GetHashCode() => ErrorCode.GetHashCode() ^ (Message?.GetHashCode() ?? 0) ^ (PostConditionCode?.GetHashCode() ?? 0) ^ StatusCode.GetHashCode();
}
