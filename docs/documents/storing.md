# Storing Documents

The primary function of Marten is to store and retrieve documents to a Postgresql database, so here's
all the ways that Marten enables you to write to the document storage.

## "Upsert" with Store()

Postgresql has an efficient _[upsert](https://wiki.postgresql.org/wiki/UPSERT)_ capability that we
exploit in Marten to let users just say "this document has changed" with the `IDocumentSession.Store()`
method and not have to worry about whether or not the document is brand new or is replacing
a previously persisted document with the same identity. Here's that method in action
with a sample that shows storing both a brand new document and a modified document:

<!-- snippet: sample_using_DocumentSession_Store -->
<a id='snippet-sample_using_DocumentSession_Store'></a>
```cs
using var store = DocumentStore.For("some connection string");

await using var session = store.LightweightSession();

var newUser = new User
{
    UserName = "travis.kelce"
};

var existingUser = await session.Query<User>()
    .SingleAsync(x => x.UserName == "patrick.mahomes");

existingUser.Roles = new[] {"admin"};

// We're storing one brand new document, and one
// existing document that will just be replaced
// upon SaveChangesAsync()
session.Store(newUser, existingUser);

await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/StoringDocuments.cs#L37-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_DocumentSession_Store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Store()` method can happily take a mixed bag of document types at one time, but you'll need to tell Marten to use `Store<object>()` instead of letting it infer the document type as shown below:

<!-- snippet: sample_store_mixed_bag_of_document_types -->
<a id='snippet-sample_store_mixed_bag_of_document_types'></a>
```cs
using var store = DocumentStore.For("some connection string");
var user1 = new User();
var user2 = new User();
var issue1 = new Issue();
var issue2 = new Issue();
var company1 = new Company();
var company2 = new Company();

await using var session = store.LightweightSession();

session.Store<object>(user1, user2, issue1, issue2, company1, company2);
await session.SaveChangesAsync();

// Or this usage:
var documents = new object[] {user1, user2, issue1, issue2, company1, company2};

// The argument here is any kind of IEnumerable<object>
session.StoreObjects(documents);
await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/StoringDocuments.cs#L10-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_store_mixed_bag_of_document_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Insert & Update

While `IDocumentSession.Store` will perform either insertion or update depending on the existence of documents, `IDocumentSession` also exposes methods to explicitly control this persistence behavior through `IDocumentSession.Insert` and `IDocumentSession.Update`.

`IDocumentSession.Insert` stores a document only in the case that it does not already exist. Otherwise a `DocumentAlreadyExistsException` is thrown. `IDocumentSession.Update` on the other hand performs an update on an existing document or throws a `NonExistentDocumentException` in case the document cannot be found.

<!-- snippet: sample_sample-document-insertonly -->
<a id='snippet-sample_sample-document-insertonly'></a>
```cs
using (var session = theStore.LightweightSession())
{
    session.Insert(target);
    await session.SaveChangesAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/document_inserts.cs#L75-L83' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-document-insertonly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Bulk Loading

Marten supports [Postgresql's COPY](http://www.postgresql.org/docs/9.4/static/sql-copy.html) functionality for very efficient insertion of documents like you might need for test data set up or data migrations. Marten calls this feature "Bulk Insert," and it's exposed off the `IDocumentStore` interface as shown below:

<!-- snippet: sample_using_bulk_insert -->
<a id='snippet-sample_using_bulk_insert'></a>
```cs
// This is just creating some randomized
// document data
var data = Target.GenerateRandomData(100).ToArray();

// Load all of these into a Marten-ized database
theStore.BulkInsert(data, batchSize: 500);

// And just checking that the data is actually there;)
theSession.Query<Target>().Count().ShouldBe(data.Length);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L146-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_bulk_insert' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The bulk insert is done with a single transaction. For really large document collections, you may need to page the calls to `IDocumentStore.BulkInsert()`.

And new with Marten v4.0 is an asynchronous version:

::: tip
If you are concerned with server resource utilization, you probably want to be using
the asynchronous versions of Marten APIs.
:::

<!-- snippet: sample_using_bulk_insert_async -->
<a id='snippet-sample_using_bulk_insert_async'></a>
```cs
// This is just creating some randomized
// document data
var data = Target.GenerateRandomData(100).ToArray();

// Load all of these into a Marten-ized database
await theStore.BulkInsertAsync(data, batchSize: 500);

// And just checking that the data is actually there;)
theSession.Query<Target>().Count().ShouldBe(data.Length);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L304-L314' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_bulk_insert_async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default, bulk insert will fail if there are any duplicate id's between the documents being inserted and the existing database data. You can alter this behavior through the `BulkInsertMode` enumeration as shown below:

<!-- snippet: sample_BulkInsertMode_usages -->
<a id='snippet-sample_BulkInsertMode_usages'></a>
```cs
// Just say we have an array of documents we want to bulk insert
var data = Target.GenerateRandomData(100).ToArray();

using var store = DocumentStore.For("some connection string");

// Discard any documents that match the identity of an existing document
// in the database
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.IgnoreDuplicates);

// This is the default mode, the bulk insert will fail if any duplicate
// identities with existing data or within the data set being loaded are detected
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.InsertsOnly);

// Overwrite any existing documents with the same identity as the documents
// being loaded
await store.BulkInsertDocumentsAsync(data, BulkInsertMode.OverwriteExisting);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L383-L402' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_BulkInsertMode_usages' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

When using `BulkInsertMode.OverwriteExisting` it is also possible to pass in a condition to be evaluated when overwriting documents.
This allows for checks such as "don't update document if the existing document is newer" without having to do more expensive round trips.
Be careful with using this - this is a low level call and the update condition does not support parameters or protection from sql injection.

<!-- snippet: sample_BulkInsertWithUpdateCondition -->
<a id='snippet-sample_BulkInsertWithUpdateCondition'></a>
```cs
// perform a bulk insert of `Target` documents
// but only overwrite existing if the existing document's "Number"
// property is less then the new document's
await theStore.BulkInsertAsync(
    data2,
    BulkInsertMode.OverwriteExisting,
    updateCondition: "(d.data ->> 'Number')::int <= (excluded.data ->> 'Number')::int");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L101-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_BulkInsertWithUpdateCondition' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The bulk insert feature can also be used with multi-tenanted documents, but in that
case you are limited to only loading documents to a single tenant at a time as
shown below:

<!-- snippet: sample_MultiTenancyWithBulkInsert -->
<a id='snippet-sample_MultiTenancyWithBulkInsert'></a>
```cs
// Just say we have an array of documents we want to bulk insert
var data = Target.GenerateRandomData(100).ToArray();

using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");
    opts.Policies.AllDocumentsAreMultiTenanted();
});

// If multi-tenanted
await store.BulkInsertDocumentsAsync("a tenant id", data);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Writing/bulk_loading.cs#L407-L421' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_MultiTenancyWithBulkInsert' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
