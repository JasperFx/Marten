using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class QueryBySql
{
    public void QueryForWholeDocumentByWhereClause(IQuerySession session)
    {
        #region sample_query_for_whole_document_by_where_clause

        var millers = session
            .Query<User>("where data ->> 'LastName' = 'Miller'");

        #endregion
    }

    public void QueryWithParameters(IQuerySession session)
    {
        #region sample_query_with_sql_and_parameters

        // pass in a list of anonymous parameters
        var millers = session
            .Query<User>("where data ->> 'LastName' = ?", "Miller");

        // pass in named parameters using an anonymous object
        var params1 = new { First = "Jeremy", Last = "Miller" };
        var jeremysAndMillers1 = session
            .Query<User>("where data ->> 'FirstName' = @First or data ->> 'LastName' = @Last", params1);

        // pass in named parameters using a dictionary
        var params2 = new Dictionary<string, object>
        {
            { "First", "Jeremy" },
            { "Last", "Miller" }
        };
        var jeremysAndMillers2 = session
            .Query<User>("where data ->> 'FirstName' = @First or data ->> 'LastName' = @Last", params2);

        #endregion
    }

    public async Task QueryAsynchronously(IQuerySession session)
    {
        #region sample_query_with_sql_async

        var millers = await session
            .QueryAsync<User>("where data ->> 'LastName' = ?", "Miller");

        #endregion
    }

}
