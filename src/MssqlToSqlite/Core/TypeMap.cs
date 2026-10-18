using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace MssqlToSqlite.Core
{
    /// <summary>How a SQL Server column is read and what SQLite affinity it lands in.</summary>
    public enum SqlKind
    {
        Integer,
        Boolean,
        Decimal,
        Float,
        Real,
        /// <summary>datetime/smalldatetime: stored in 1/300 s ticks, shown (and exported) to the millisecond.</summary>
        DateTime,
        DateTime2,
        Date,
        Time,
        DateTimeOffset,
        FixedText,
        Text,
        Guid,
        Binary,
        Variant,

        /// <summary>CLR types (hierarchyid, geometry…) SqlClient cannot materialise; read as varbinary.</summary>
        Opaque,
    }

    /// <summary>
    /// SQL Server → SQLite type and value translation.
    ///
    /// The output is read by Pointy's migration connectors through Python, so the
    /// rules favour exactness over SQLite-native typing: decimals are bound as
    /// text into NUMERIC columns (SQLite keeps them numeric only when that is
    /// lossless), and dates are ISO text that <c>datetime.fromisoformat</c> reads.
    /// </summary>
    public static class TypeMap
    {
        public static SqlKind KindOf(string typeName)
        {
            switch ((typeName ?? "").ToLowerInvariant())
            {
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                    return SqlKind.Integer;
                case "bit":
                    return SqlKind.Boolean;
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    return SqlKind.Decimal;
                case "float":
                    return SqlKind.Float;
                case "real":
                    return SqlKind.Real;
                case "datetime":
                case "smalldatetime":
                    return SqlKind.DateTime;
                case "datetime2":
                    return SqlKind.DateTime2;
                case "date":
                    return SqlKind.Date;
                case "time":
                    return SqlKind.Time;
                case "datetimeoffset":
                    return SqlKind.DateTimeOffset;
                case "char":
                case "nchar":
                    return SqlKind.FixedText;
                case "varchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "xml":
                case "sysname":
                    return SqlKind.Text;
                case "uniqueidentifier":
                    return SqlKind.Guid;
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion":
                    return SqlKind.Binary;
                case "sql_variant":
                    return SqlKind.Variant;
                default:
                    return SqlKind.Opaque;
            }
        }

        public static string SqliteType(SqlKind kind)
        {
            switch (kind)
            {
                case SqlKind.Integer:
                case SqlKind.Boolean:
                    return "INTEGER";
                case SqlKind.Decimal:
                    return "NUMERIC";
                case SqlKind.Float:
                case SqlKind.Real:
                    return "REAL";
                case SqlKind.Binary:
                case SqlKind.Opaque:
                    return "BLOB";
                case SqlKind.Variant:
                    return "";
                default:
                    return "TEXT";
            }
        }

        public static DbType ParameterType(SqlKind kind)
        {
            switch (kind)
            {
                case SqlKind.Integer:
                case SqlKind.Boolean:
                    return DbType.Int64;
                case SqlKind.Float:
                case SqlKind.Real:
                    return DbType.Double;
                case SqlKind.Binary:
                case SqlKind.Opaque:
                    return DbType.Binary;
                default:
                    return DbType.String;
            }
        }

        /// <summary>sql_variant changes type row by row; pick the binding from the value itself.</summary>
        public static DbType ParameterTypeOf(object value)
        {
            switch (value)
            {
                case long _:
                case int _:
                case short _:
                case byte _:
                    return DbType.Int64;
                case double _:
                case float _:
                    return DbType.Double;
                case byte[] _:
                    return DbType.Binary;
                default:
                    return DbType.String;
            }
        }

        /// <summary>Digits SQLite can keep when it turns a NUMERIC text into REAL is 15; count what a value needs.</summary>
        public static int SignificantDigits(string number)
        {
            string digits = number.TrimStart('-', '+');
            int dot = digits.IndexOf('.');
            if (dot >= 0)
                digits = digits.TrimEnd('0').TrimEnd('.');
            digits = digits.Replace(".", "").TrimStart('0');
            if (dot < 0)
                digits = digits.TrimEnd('0');
            return digits.Length;
        }

        /// <summary>The SELECT expression for a column.</summary>
        public static string SelectExpression(ColumnSchema column)
        {
            string quoted = Sql.QuoteMssql(column.Name);
            return column.Kind == SqlKind.Opaque ? "CAST(" + quoted + " AS varbinary(max))" : quoted;
        }

        /// <summary>Reads column <paramref name="i"/> into the value bound to SQLite.</summary>
        public static object Read(SqlDataReader reader, int i, SqlKind kind, bool trimFixed)
        {
            if (reader.IsDBNull(i))
                return DBNull.Value;

            switch (kind)
            {
                case SqlKind.Integer:
                    return System.Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture);
                case SqlKind.Boolean:
                    return reader.GetBoolean(i) ? 1L : 0L;
                case SqlKind.Decimal:
                    // GetDecimal throws for decimal(38) values beyond System.Decimal.
                    return DecimalText(reader.GetSqlValue(i));
                case SqlKind.Float:
                    return reader.GetDouble(i);
                case SqlKind.Real:
                    // (double)1.1f is 1.100000023841858; round-trip through text instead.
                    return double.Parse(reader.GetFloat(i).ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                case SqlKind.DateTime:
                    // .123 is really 37/300 s = .12333…; SQL Server itself shows .123.
                    long ticks = reader.GetDateTime(i).Ticks;
                    return FormatDateTime(new DateTime((ticks + 5000) / 10000 * 10000));
                case SqlKind.Date:
                    return reader.GetDateTime(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case SqlKind.FixedText:
                    string fixedText = reader.GetString(i);
                    return trimFixed ? fixedText.TrimEnd(' ') : fixedText;
                default:
                    return ToSqliteValue(reader.GetValue(i));
            }
        }

        /// <summary>A .NET value as SQLite stores it; used for sql_variant and the generic path.</summary>
        public static object ToSqliteValue(object value)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    return DBNull.Value;
                case string s:
                    return s;
                case DateTime dt:
                    return FormatDateTime(dt);
                case DateTimeOffset dto:
                    return FormatDateTime(dto.DateTime) + dto.ToString("zzz", CultureInfo.InvariantCulture);
                case TimeSpan ts:
                    return FormatTime(ts);
                case Guid g:
                    return g.ToString("D").ToUpperInvariant();
                case bool b:
                    return b ? 1L : 0L;
                case decimal d:
                    return d.ToString(CultureInfo.InvariantCulture);
                case long _:
                case double _:
                case byte[] _:
                    return value;
                case int i32:
                    return (long)i32;
                case short i16:
                    return (long)i16;
                case byte u8:
                    return (long)u8;
                case float f32:
                    return double.Parse(f32.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        private static string DecimalText(object sqlValue)
        {
            if (sqlValue is System.Data.SqlTypes.SqlDecimal sd)
                return sd.ToString();
            if (sqlValue is System.Data.SqlTypes.SqlMoney sm)
                return sm.Value.ToString(CultureInfo.InvariantCulture);
            return System.Convert.ToString(sqlValue, CultureInfo.InvariantCulture);
        }

        /// <summary>yyyy-MM-dd HH:mm:ss with up to six fraction digits (what Python parses on every version).</summary>
        public static string FormatDateTime(DateTime value)
        {
            string text = value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return text + Fraction(value.Ticks % TimeSpan.TicksPerSecond);
        }

        public static string FormatTime(TimeSpan value)
        {
            string text = new DateTime(value.Ticks % TimeSpan.TicksPerDay).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return text + Fraction(value.Ticks % TimeSpan.TicksPerSecond);
        }

        private static string Fraction(long ticks)
        {
            long micros = ticks / 10;
            if (micros == 0)
                return "";
            return "." + micros.ToString("000000", CultureInfo.InvariantCulture).TrimEnd('0');
        }
    }
}
