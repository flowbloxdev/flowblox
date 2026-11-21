# Native SQL full-text search

`SQLTable` and `ExecuteSQLFlowBlock` expose `EnableFullTextSearch`, disabled by default.
Existing SQL, parameter substitution and result formats remain unchanged when it is disabled.
The existing `DbType` and connection select the implementation; there is no search-engine Managed Object.

## Queries

Use `FULLTEXT_MATCH(search)` in the existing SQL statement. The search argument is a bound field
parameter or a string literal. Text columns are discovered from database metadata. Optionally list
the searchable columns before the search argument to exclude unrelated text metadata:

```sql
SELECT Id, Title, FileName, Content, FULLTEXT_RANK() AS Score
FROM Documents
WHERE FULLTEXT_MATCH(Title, FileName, Content, $User::Keywords)
LIMIT $User::TopK;
```

Keep **Use field values as parameters** enabled for dynamic search input. The example uses SQLite/MySQL
pagination; existing MSSQL `TOP`/`OFFSET ... FETCH` and Oracle `FETCH FIRST` syntax are retained.
Results are ordered by native relevance descending, before any existing sort criteria and pagination.
`FULLTEXT_RANK()` is optional and only adds a score column when explicitly selected. Empty search input
returns no matches. Search words are normalized into bound provider expressions; input is not treated
as raw SQL or an unrestricted full-text query language. Multiple words match alternatives.

Full-text mode supports a single physical source table, optionally schema-qualified, without joins,
subqueries, unions, DISTINCT or aggregation. Unsupported shapes produce an explicit error; use the
ordinary SQL path for other queries. Native tokenization, stopwords, stemming and scores differ by database.

`SQLTable` returns the same DataTable shape as its SELECT, consumed by the existing TableReader.
ExecuteSQL keeps its single Boolean **Success** output. It can prepare native indexes for single-table
CREATE TABLE, INSERT, UPDATE and DELETE statements; it does not acquire a second result dataset.
For ranked row output use SQLTable/TableReader.

## Index lifecycle and prerequisites

The first enabled operation creates the native index and includes existing rows. Later writes are
maintained by native indexing or synchronization triggers, including writes outside FlowBlox.
No schema, server feature or operating-system package is installed silently. Missing capabilities
or index-creation permissions produce an actionable error.

| Provider | Index/ranking | Prerequisites and consistency |
| --- | --- | --- |
| SQLite | FTS5, BM25 (sign adjusted so higher scores rank first) | FTS5-enabled SQLite, ordinary rowid table; external-content index with atomic initial rebuild and insert/update/delete triggers. Main database only. |
| MSSQL | Full-text index, FREETEXTTABLE rank | Full-Text Search feature, a single-column unique non-null key index, catalog/index privileges; automatic change tracking. Searches wait up to 30 seconds for pending indexing. Existing indexes are enabled and extended with missing selected text columns. |
| Oracle | Oracle Text CONTEXT, CONTAINS/SCORE | Oracle Text and index privileges; per-column indexes with SYNC ON COMMIT and TRANSACTIONAL support. |
| MySQL | FULLTEXT, MATCH/AGAINST rank | InnoDB/MyISAM and supported text columns; bound Boolean prefix terms also match filename prefixes such as `invoice_2026`. Native stopword/minimum-token rules still apply. |

FlowBlox-owned objects use deterministic `fbx_fts_...` names. Enabling this feature does not require
`SQL.AllowDestructiveStatements`: internal SQLite trigger maintenance changes only the owned search
index. User-supplied SQL still passes the existing destructive-statement guard before index setup.

## Verification

`NativeFullTextSearchTests` executes real SQLite indexing, backfill, ranking, filename matching,
updates/deletes, parameterization, query-shape rejection and serialization compatibility.
`FullTextProviderQueryTests` exercises parameterized SQL generation for all four providers.

`MySqlFullTextIntegrationTests` requires `FLOWBLOX_FTS_MYSQL_TEST_PORT` pointing to a dedicated
loopback instance other than port 3306. It creates and drops its own randomly named test schema.
Do not point it at a working database. MariaDB can also be exercised through the MySQL driver;
MSSQL/Oracle live-server tests require suitable isolated test instances.

Provider references: [SQLite FTS5](https://www.sqlite.org/fts5.html),
[SQL Server full-text indexes](https://learn.microsoft.com/en-us/sql/relational-databases/search/create-and-manage-full-text-indexes),
[Oracle Text indexes](https://docs.oracle.com/en/database/oracle/oracle-database/19/ccref/create-index.html),
[MySQL Boolean full-text search](https://dev.mysql.com/doc/refman/8.4/en/fulltext-boolean.html).
