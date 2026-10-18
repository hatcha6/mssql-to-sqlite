using System.Collections.Generic;

namespace MssqlToSqlite.Core
{
    /// <summary>A user table as listed for selection.</summary>
    public sealed class TableRef
    {
        public TableRef(string schema, string name, long approxRows)
        {
            Schema = schema;
            Name = name;
            ApproxRows = approxRows;
        }

        public string Schema { get; }
        public string Name { get; }

        /// <summary>From sys.partitions — fast but only an estimate.</summary>
        public long ApproxRows { get; }

        public string FullName => Schema + "." + Name;

        /// <summary>T-SQL two-part name, bracket-quoted.</summary>
        public string Quoted => Sql.QuoteMssql(Schema) + "." + Sql.QuoteMssql(Name);

        public override string ToString() => FullName;
    }

    public sealed class ColumnSchema
    {
        public string Name { get; set; }

        /// <summary>Base system type name (alias types already resolved).</summary>
        public string TypeName { get; set; }

        public bool Nullable { get; set; }
        public SqlKind Kind { get; set; }
    }

    public sealed class IndexSchema
    {
        public string Name { get; set; }
        public bool PrimaryKey { get; set; }
        public bool Unique { get; set; }
        public List<string> Columns { get; } = new List<string>();
    }

    public sealed class TableSchema
    {
        public TableSchema(TableRef table)
        {
            Table = table;
        }

        public TableRef Table { get; }
        public List<ColumnSchema> Columns { get; } = new List<ColumnSchema>();
        public List<IndexSchema> Indexes { get; } = new List<IndexSchema>();
    }
}
