#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Events;
using Marten.Internal.Operations;

namespace Marten;

/// <summary>
///     Basic storage operations for document types, but cannot initiate any actual writes
/// </summary>
public interface IDocumentOperations: IQuerySession
{
    new IEventStore Events { get; }

    /// <summary>
    ///     Mark this entity for deletion upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Delete<T>(T entity) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void Delete<T>(int id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void Delete<T>(long id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void Delete<T>(object id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for deletion upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void Delete<T>(Guid id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with a string id for deletion upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void Delete<T>(string id) where T : notnull;

    /// <summary>
    ///     Bulk delete all documents of type T matching the expression condition
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="expression"></param>
    void DeleteWhere<T>(Expression<Func<T, bool>> expression) where T : notnull;

    /// <summary>
    ///     Delete an enumerable of potentially mixed documents
    /// </summary>
    /// <param name="documents"></param>
    void DeleteObjects(IEnumerable<object> documents);

    /// <summary>
    ///     Explicitly marks multiple documents as needing to be inserted or updated upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Store<T>(IEnumerable<T> entities) where T : notnull;

    /// <summary>
    ///     Explicitly marks one or more documents as needing to be inserted or updated upon the next call to SaveChanges()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Store<T>(params T[] entities) where T : notnull;

    /// <summary>
    ///     Explicitly marks a document as needing to be updated and supplies the
    ///     current known version for the purpose of optimistic versioning checks
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    /// <param name="version"></param>
    void UpdateExpectedVersion<T>(T entity, Guid version) where T : notnull;

    /// <summary>
    /// Explicitly marks a document as needing to be updated and supplies the
    /// *new* revision for the purpose of optimistic versioning checks. This operation
    /// will be rejected and cause a ConcurrencyException on SaveChanges() if the revision in the database is greater or equal to the given
    /// revision
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    /// <param name="revision"></param>
    void UpdateRevision<T>(T entity, int revision);

    /// <summary>
    /// Explicitly marks a document as needing to be updated and supplies the
    /// *new* revision for the purpose of optimistic versioning checks. This operation
    /// will do nothing  if the revision in the database is greater or equal to the given
    /// revision
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="revision"></param>
    /// <typeparam name="T"></typeparam>
    void TryUpdateRevision<T>(T entity, int revision);

    /// <summary>
    ///     Store an enumerable of potentially mixed documents
    /// </summary>
    /// <param name="documents"></param>
    void StoreObjects(IEnumerable<object> documents);

    /// <summary>
    ///     Catch all mechanism to add additional database calls to the batched
    ///     updates in SaveChanges()/SaveChangesAsync()
    /// </summary>
    /// <param name="storageOperation"></param>
    void QueueOperation(IStorageOperation storageOperation);

    /// <summary>
    ///     Explicitly marks a document as needing to be inserted upon the next call to SaveChanges().
    ///     Will throw an exception if the document already exists
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Insert<T>(IEnumerable<T> entities) where T : notnull;

    /// <summary>
    ///     Explicitly marks a document as needing to be inserted upon the next call to SaveChanges().
    ///     Will throw an exception if the document already exists
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Insert<T>(params T[] entities) where T : notnull;

    /// <summary>
    ///     Explicitly marks a document as needing to be updated upon the next call to SaveChanges().
    ///     Will throw an exception if the document does not already exists
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Update<T>(IEnumerable<T> entities) where T : notnull;

    /// <summary>
    ///     Explicitly marks a document as needing to be updated upon the next call to SaveChanges().
    ///     Will throw an exception if the document does not already exists
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void Update<T>(params T[] entities) where T : notnull;

    /// <summary>
    ///     Insert an enumerable of potentially mixed documents. Will throw exceptions
    ///     if a document overwrite is detected
    /// </summary>
    /// <param name="documents"></param>
    void InsertObjects(IEnumerable<object> documents);

    /// <summary>
    ///     Mark this entity for a "hard" deletion upon the next call to SaveChanges()
    ///     that will delete the underlying database row
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="entity"></param>
    void HardDelete<T>(T entity) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for "hard" deletion upon the next call to SaveChanges()
    ///     that will delete the underlying database row
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void HardDelete<T>(int id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for hard deletion upon the next call to SaveChanges()
    ///     that will delete the underlying database row
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void HardDelete<T>(long id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with either a numeric or Guid id for hard deletion upon the next call to SaveChanges()
    ///     that will delete the underlying database row
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void HardDelete<T>(Guid id) where T : notnull;

    /// <summary>
    ///     Mark an entity of type T with a string id for hard deletion upon the next call to SaveChanges()
    ///     that will delete the underlying database row
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    void HardDelete<T>(string id) where T : notnull;

    /// <summary>
    ///     Bulk hard delete all documents of type T matching the expression condition
    ///     that will delete the underlying database rows
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="expression"></param>
    void HardDeleteWhere<T>(Expression<Func<T, bool>> expression) where T : notnull;

    /// <summary>
    ///     For soft-deleted document types, this is a one sized fits all mechanism to reverse the
    ///     soft deletion tracking
    /// </summary>
    /// <param name="expression"></param>
    /// <typeparam name="T"></typeparam>
    /// <exception cref="InvalidOperationException"></exception>
    void UndoDeleteWhere<T>(Expression<Func<T, bool>> expression) where T : notnull;


    /// <summary>
    ///     Registers a SQL command to be executed with the underlying unit of work as part of the batched command.
    ///     Use "?" placeholders to denote parameter values
    /// </summary>
    /// <param name="sql"></param>
    /// <param name="parameterValues"></param>
    void QueueSqlCommand(string sql, params object[] parameterValues);

    /// <summary>
    ///     Registers a SQL command to be executed with the underlying unit of work as part of the batched command.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameterValues"></param>
    void QueueSqlCommand(char placeholder, string sql, params object[] parameterValues);

    /// <summary>
    /// In the case of a lightweight session, this will direct Marten to opt into identity map mechanics
    /// for only the document type T. This is a micro-optimization added for the event sourcing + projections
    /// support
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void UseIdentityMapFor<T>();
}
