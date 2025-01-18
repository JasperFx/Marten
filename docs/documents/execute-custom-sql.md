# Execute custom SQL in session

Use `QueueSqlCommand(string sql, params object[] parameterValues)` method to register and execute any custom/arbitrary SQL commands with the underlying unit of work, as part of the batched commands within `IDocumentSession`. 

`?` placeholders can be used to denote parameter values. Postgres [type casts `::`](https://www.postgresql.org/docs/15/sql-expressions.html#SQL-SYNTAX-TYPE-CASTS) can be applied to the parameter if needed. If the `?` character is not suitable as a placeholder because you need to use `?` in your sql query, you can change the placeholder by providing an alternative. Pass this in before the sql argument. 

<!-- snippet: sample_QueueSqlCommand -->
<a id='snippet-sample_QueueSqlCommand'></a>
```cs
theSession.QueueSqlCommand("insert into names (name) values ('Jeremy')");
theSession.QueueSqlCommand("insert into names (name) values ('Babu')");
theSession.Store(Target.Random());
theSession.QueueSqlCommand("insert into names (name) values ('Oskar')");
theSession.Store(Target.Random());
var json = "{ \"answer\": 42 }";
theSession.QueueSqlCommand("insert into data (raw_value) values (?::jsonb)", json);
// Use ^ as the parameter placeholder
theSession.QueueSqlCommand('^', "insert into data (raw_value) values (^::jsonb)", json);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/executing_arbitrary_sql_as_part_of_transaction.cs#L39-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_QueueSqlCommand' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
