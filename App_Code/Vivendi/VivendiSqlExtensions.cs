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
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
using System.Linq;

public static class VivendiSqlExtensions
{
    static T? GetOptional<T>(Func<int, T> readerAccessor, string column) where T : struct
    {
        var reader = (SqlDataReader)readerAccessor.Target;
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? (T?)null : readerAccessor(i);
    }

    public static bool GetBoolean(this SqlDataReader reader, string column) => reader.GetBoolean(reader.GetOrdinal(column));

    public static bool? GetBooleanOptional(this SqlDataReader reader, string column) => GetOptional(reader.GetBoolean, column);

    public static DateTime GetDateTime(this SqlDataReader reader, string column) => reader.GetDateTime(reader.GetOrdinal(column));

    public static DateTime? GetDateTimeOptional(this SqlDataReader reader, string column) => GetOptional(reader.GetDateTime, column);

    public static IEnumerable<int> GetIDs(this SqlDataReader reader, string column) => GetIDsOptional(reader, column) ?? throw new SqlNullValueException();

    public static IEnumerable<int> GetIDsOptional(this SqlDataReader reader, string column) => GetStringOptional(reader, column)?.Split(',').Select(s => int.Parse(s, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture));

    public static short GetInt16(this SqlDataReader reader, string column) => reader.GetInt16(reader.GetOrdinal(column));

    public static short? GetInt16Optional(this SqlDataReader reader, string column) => GetOptional(reader.GetInt16, column);

    public static int GetInt32(this SqlDataReader reader, string column) => reader.GetInt32(reader.GetOrdinal(column));

    public static int? GetInt32Optional(this SqlDataReader reader, string column) => GetOptional(reader.GetInt32, column);

    public static string GetString(this SqlDataReader reader, string column) => reader.GetString(reader.GetOrdinal(column));

    public static string GetStringOptional(this SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return !reader.IsDBNull(i) ? reader.GetString(i) : null;
    }
}
