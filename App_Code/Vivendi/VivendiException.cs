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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aufbauwerk.Tools.Vivendi
{
    public sealed class VivendiException : ExternalException
    {
        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_BAD_PATHNAME = 161;
        private const int ERROR_FILE_TOO_LARGE = 223;
        private const int ERROR_FILENAME_EXCED_RANGE = 206;
        private const int ERROR_LOCK_VIOLATION = 33;
        private const int ERROR_NOT_SUPPORTED = 50;
        private const int FACILITY_WIN32 = 7;

        internal static VivendiException DocumentContainsAdditionalLinks() => new VivendiException("The document contains additional links and should therefore only be modified within Vivendi.");
        internal static VivendiException DocumentHasDifferentOwner() => new VivendiException("The document was uploaded by a different user.");
        internal static VivendiException DocumentIsLocked(DateTime lockDate) => new VivendiException(ERROR_LOCK_VIOLATION, $"The document has been locked since {lockDate}.");
        internal static VivendiException DocumentIsNotWebDAV() => new VivendiException("The document was created or modified in Vivendi and therefore cannot be modified outsite.");
        internal static VivendiException DocumentIsTooLarge(int maxSize) => new VivendiException(ERROR_FILE_TOO_LARGE, $"The document exceeds the size of {maxSize} bytes.");
        internal static VivendiException DocumentNotAllowedInCollection() => new VivendiException(ERROR_NOT_SUPPORTED, "Documents cannot be created in or copied/moved to this collection.");
        internal static VivendiException ResourceIsStatic() => new VivendiException("The resource is static and cannot be altered.");
        internal static VivendiException ResourceNameExceedsRange(int maxLength) => new VivendiException(ERROR_FILENAME_EXCED_RANGE, $"The name of the resource must not exceed {maxLength} characters.");
        internal static VivendiException ResourceNameIsInvalid() => new VivendiException(ERROR_BAD_PATHNAME, "The name of the resource is invalid.");
        internal static VivendiException ResourceNotInGrantedSections() => new VivendiException("Access denied.");
        internal static VivendiException ResourcePropertyIsReadonly([CallerMemberName]string propertyName = "") => new VivendiException($"The property {propertyName} is read-only.");
        internal static VivendiException ResourceRequiresHigherAccessLevel() => new VivendiException("Insufficent access level.");

        private VivendiException(string message)
        : this(ERROR_ACCESS_DENIED, message)
        { }

        private VivendiException(int errorCode, string message)
        : base(message)
        {
            ErrorCode = errorCode;
            HResult = errorCode <= 0 ? errorCode : ((errorCode & 0x0000FFFF) | (FACILITY_WIN32 << 16) | -2147483648);
        }

        public override int ErrorCode { get; }
    }
}
