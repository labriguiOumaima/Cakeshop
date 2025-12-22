using Neo4j.Driver;

namespace Services
{
    public class Neo4jService : IDisposable
    {
        private readonly IDriver _driver;
        private readonly string _database;

        public Neo4jService(string uri, string username, string password, string database)
        {
            _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
            _database = database;
        }

        public IAsyncSession GetAsyncSession()
        {
            return _driver.AsyncSession(o => o.WithDatabase(_database));
        }

        public void Dispose()
        {
            _driver?.Dispose();
        }
    }
}