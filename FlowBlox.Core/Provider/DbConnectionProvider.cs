using FlowBlox.Core.Enums;
using FlowBlox.Core.Factories;
using System.Data;
using System.Data.Common;

namespace FlowBlox.Core.Provider
{
    public class DbConnectionProvider
    {
        public DbConnectionProvider()
        {
        }

        private static DbConnectionProvider _dbConnectionProvider;
        public static DbConnectionProvider Instance
        {
            get
            {
                if ( _dbConnectionProvider == null )
                    _dbConnectionProvider = new DbConnectionProvider();
                return _dbConnectionProvider;
            }
        }

        private Dictionary<string, DbConnection> _nameToDbConnection { get; } = new Dictionary<string, DbConnection>();

        /// <summary>
        /// Releases cached connections and their pools before recreating a SQLite file.
        /// Equivalent connection strings must not retain handles to the old database.
        /// </summary>
        public void ReleaseSqliteConnections(string filePath) =>
            SqliteConnectionHelper.ReleaseConnections(_nameToDbConnection, filePath);

        private static void EnsureOpen(DbConnection connection)
        {
            if (connection.State != ConnectionState.Open)
                connection.Open();
        }

        public DbConnection GetOrCreateDbConnection(DbTypes dbType, string connectionstring)
        {
            DbConnection dbConnection;
            if (_nameToDbConnection.ContainsKey(connectionstring))
            {
                dbConnection = _nameToDbConnection[connectionstring];
            }
            else
            {
                dbConnection = DbConnectionFactory.Create(dbType, connectionstring);
                if (dbConnection != null)
                    _nameToDbConnection[connectionstring] = dbConnection;
            }
            EnsureOpen(dbConnection);
            return dbConnection;
        }
    }
}
