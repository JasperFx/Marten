#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten;

public class NullChangeListener: IChangeListener
{
    public static IChangeListener Instance { get; } = new NullChangeListener();

    private NullChangeListener()
    {
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        return Task.CompletedTask;
    }
}

#region sample_IDocumentSessionListener

public interface IChangeListener
{
    /// <summary>
    /// Used to carry out actions on potentially changed projected documents generated and updated
    /// during the execution of asynchronous projections. This will give you "at most once" delivery guarantees
    /// </summary>
    /// <param name="session"></param>
    /// <param name="commit"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token);

    /// <summary>
    /// Used to carry out actions on potentially changed projected documents generated and updated
    /// during the execution of asynchronous projections. This will execute *before* database changes
    /// are committed. Use this for "at least once" delivery guarantees.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="commit"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token);
}

/// <summary>
///     Used to listen to and intercept operations within an IDocumentSession.SaveChanges()/SaveChangesAsync()
///     operation
/// </summary>
public interface IDocumentSessionListener
{
    /// <summary>
    ///     After an IDocumentSession is committed
    /// </summary>
    /// <param name="session"></param>
    /// <param name="commit"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token);

    /// <summary>
    ///     Called just after IDocumentSession.SaveChanges() is called, but before
    ///     any event apply are made
    /// </summary>
    /// <param name="session"></param>
    void BeforeProcessChanges(IDocumentSession session);

    /// <summary>
    ///     Called just after IDocumentSession.SaveChanges() is called, but before
    ///     any event apply are made
    /// </summary>
    /// <param name="session"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task BeforeProcessChangesAsync(IDocumentSession session, CancellationToken token);

    /// <summary>
    ///     Called just after IDocumentSession.SaveChanges() is called, but before
    ///     any database calls are made
    /// </summary>
    /// <param name="session"></param>
    void BeforeSaveChanges(IDocumentSession session);

    /// <summary>
    ///     Called just after IDocumentSession.SaveChanges() is called,
    ///     but before any database calls are made
    /// </summary>
    /// <param name="session"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token);

    /// <summary>
    ///     After an IDocumentSession is committed
    /// </summary>
    /// <param name="session"></param>
    /// <param name="commit"></param>
    void AfterCommit(IDocumentSession session, IChangeSet commit);

    /// <summary>
    ///     Called after a document is loaded
    /// </summary>
    void DocumentLoaded(object id, object document);

    /// <summary>
    ///     Called after a document is explicitly added to a session
    ///     as a staged insert or update
    /// </summary>
    void DocumentAddedForStorage(object id, object document);
}

#endregion

/// <summary>
///     Base class to help create custom IDocumentSessionListener classes
/// </summary>
public abstract class DocumentSessionListenerBase: IDocumentSessionListener
{
    public virtual void BeforeProcessChanges(IDocumentSession session)
    {
        // Nothing
    }

    public virtual Task BeforeProcessChangesAsync(IDocumentSession session, CancellationToken token)
    {
        // Nothing
        return Task.CompletedTask;
    }

    public virtual void BeforeSaveChanges(IDocumentSession session)
    {
        // Nothing
    }

    public virtual Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        // Nothing
        return Task.CompletedTask;
    }

    public virtual void AfterCommit(IDocumentSession session, IChangeSet commit)
    {
        // Nothing
    }

    public virtual Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        // Nothing
        return Task.CompletedTask;
    }

    public virtual void DocumentLoaded(object id, object document)
    {
        // Nothing
    }

    public virtual void DocumentAddedForStorage(object id, object document)
    {
        // Nothing
    }
}
