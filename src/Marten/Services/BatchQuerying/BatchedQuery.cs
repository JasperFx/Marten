using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQuery : IBatchedQuery
    {
        private static readonly MartenQueryParser QueryParser = new MartenQueryParser();
        private readonly IManagedConnection _runner;
        private readonly IDocumentSchema _schema;
        private readonly IIdentityMap _identityMap;
        private readonly QuerySession _parent;
        private readonly ISerializer _serializer;
        private readonly MartenExpressionParser _parser;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<IDataReaderHandler> _handlers = new List<IDataReaderHandler>();

        public BatchedQuery(IManagedConnection runner, IDocumentSchema schema, IIdentityMap identityMap, QuerySession parent, ISerializer serializer, MartenExpressionParser parser)
        {
            _runner = runner;
            _schema = schema;
            _identityMap = identityMap;
            _parent = parent;
            _serializer = serializer;
            _parser = parser;
        }

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        public void AddHandler(IDataReaderHandler handler)
        {
            if (_handlers.Any())
            {
                _handlers.Add(new DataReaderAdvancer(handler));
            }
            else
            {
                _handlers.Add(handler);
            }
        }

        private Task<T> load<T>(object id) where T : class
        {
            if (_identityMap.Has<T>(id))
            {
                return Task.FromResult(_identityMap.Retrieve<T>(id));
            }

            var source = new TaskCompletionSource<T>();

            var mapping = _schema.MappingFor(typeof (T));
            var parameter = _command.AddParameter(id);

            _command.AppendQuery(
                $"select {mapping.SelectFields().Join(", ")} from {mapping.Table.QualifiedName} as d where id = :{parameter.ParameterName}");

            var handler = new SingleResultReader<T>(source, _schema.StorageFor(typeof (T)), _identityMap);
            AddHandler(handler);

            return source.Task;
        }


        public IBatchLoadByKeys<TDoc> LoadMany<TDoc>() where TDoc : class
        {
            return new BatchLoadByKeys<TDoc>(this);
        }

        public class BatchLoadByKeys<TDoc> : IBatchLoadByKeys<TDoc> where TDoc : class
        {
            private readonly BatchedQuery _parent;

            public BatchLoadByKeys(BatchedQuery parent)
            {
                _parent = parent;
            }

            private Task<IList<TDoc>> load<TKey>(TKey[] keys)
            {
                var mapping = _parent._schema.MappingFor(typeof (TDoc));
                var parameter = _parent._command.AddParameter(keys);
                _parent._command.AppendQuery(
                    $"select {mapping.SelectFields().Join(", ")} from {mapping.Table.QualifiedName} as d where d.id = ANY(:{parameter.ParameterName})");

                var resolver = _parent._schema.StorageFor(typeof (TDoc)).As<IResolver<TDoc>>();

                var handler = new MultipleResultsReader<TDoc>(new WholeDocumentSelector<TDoc>(mapping, resolver), _parent._identityMap);

                _parent.AddHandler(handler);

                return handler.ReturnValue;
            }

            public Task<IList<TDoc>> ById<TKey>(params TKey[] keys)
            {
                return load(keys);
            }

            public Task<IList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys)
            {
                return load(keys.ToArray());
            }
        }

        public Task<IList<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            _parent.ConfigureCommand<T>(_command, sql, parameters);

            var handler = new QueryResultsReader<T>(_serializer);
            AddHandler(handler);

            return handler.ReturnValue;
        }

        private DocumentQuery toDocumentQuery<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            var expression = queryable.Expression;

            var model = QueryParser.GetParsedQuery(expression);

            _schema.EnsureStorageExists(model.MainFromClause.ItemType);

            var docQuery = new DocumentQuery(_schema.MappingFor(model.MainFromClause.ItemType), model, _parser);
            docQuery.Includes.AddRange(queryable.Includes);
            

            return docQuery;
        }

        public Task<TReturn> AddHandler<TDoc, THandler, TReturn>(IMartenQueryable<TDoc> queryable) where THandler : IDataReaderHandler<TReturn>, new()
        {
            var model = toDocumentQuery(queryable);
            var handler = new THandler();
            handler.Configure(_command, model);
            AddHandler(handler);

            return handler.ReturnValue;
        }

        public Task<bool> Any<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return AddHandler<TDoc, AnyHandler, bool>(queryable);
        }


        public Task<long> Count<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return AddHandler<TDoc, CountHandler, long>(queryable);
        }

        internal Task<IList<T>> Query<T>(IMartenQueryable<T> queryable)
        {
            var documentQuery = toDocumentQuery(queryable);
            var documentStorage = _schema.StorageFor(documentQuery.SourceDocumentType);
            var reader = new QueryHandler<T>(_schema, documentStorage, documentQuery, _command, _identityMap);

            AddHandler(reader);

            return reader.ReturnValue;
        }

        public IBatchedQueryable<T> Query<T>() where T : class
        {
            return new BatchedQueryable<T>(this, _parent.Query<T>());
        }


        public Task<T> First<T>(IMartenQueryable<T> queryable)
        {
            var documentQuery = toDocumentQuery<T>(queryable.Take(1).As<IMartenQueryable<T>>());
            var reader = new QueryHandler<T>(_schema, _schema.StorageFor(documentQuery.SourceDocumentType), documentQuery, _command, _identityMap);

            AddHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.First());
        }

        public Task<T> FirstOrDefault<T>(IMartenQueryable<T> queryable) 
        {
            var documentQuery = toDocumentQuery<T>(queryable.Take(1).As<IMartenQueryable<T>>());
            var reader = new QueryHandler<T>(_schema, _schema.StorageFor(documentQuery.SourceDocumentType), documentQuery, _command, _identityMap);


            AddHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.FirstOrDefault());
        }

        public Task<T> Single<T>(IMartenQueryable<T> queryable) 
        {
            var documentQuery = toDocumentQuery<T>(queryable.Take(2).As<IMartenQueryable<T>>());
            var reader = new QueryHandler<T>(_schema, _schema.StorageFor(documentQuery.SourceDocumentType), documentQuery, _command, _identityMap);


            AddHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.Single());
        }

        public Task<T> SingleOrDefault<T>(IMartenQueryable<T> queryable)
        {
            var documentQuery = toDocumentQuery<T>(queryable.Take(2).As<IMartenQueryable<T>>());
            var reader = new QueryHandler<T>(_schema, _schema.StorageFor(documentQuery.SourceDocumentType), documentQuery, _command, _identityMap);


            AddHandler(reader);

            return reader.ReturnValue.ContinueWith(r => r.Result.SingleOrDefault());
        }

        public Task Execute(CancellationToken token = default(CancellationToken))
        {
            return _runner.ExecuteAsync(_command, async (cmd, tk) =>
            {
                using (var reader = await _command.ExecuteReaderAsync(tk).ConfigureAwait(false))
                {
                    foreach (var handler in _handlers)
                    {
                        await handler.Handle(reader, tk).ConfigureAwait(false);
                    }
                }

                return 0;
            }, token);
        }
    }
}