using Edvaniq.Testing;

// One MySQL container for all tests of this project. Every ServiceFactory creates its own database in it.
[assembly: AssemblyFixture(typeof(MySqlTestServer))]
