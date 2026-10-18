namespace MssqlToSqlite.Core
{
    internal static class Sql
    {
        /// <summary>[name] with embedded ] doubled.</summary>
        public static string QuoteMssql(string name) => "[" + name.Replace("]", "]]") + "]";

        /// <summary>"name" with embedded " doubled.</summary>
        public static string QuoteSqlite(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

        /// <summary>N'text' with embedded ' doubled, for the few statements that take no parameters.</summary>
        public static string NLiteral(string text) => "N'" + text.Replace("'", "''") + "'";
    }
}
