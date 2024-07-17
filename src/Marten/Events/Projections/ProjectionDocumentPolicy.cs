using Marten.Schema;

namespace Marten.Events.Projections;

/// <summary>
/// Makes several modifications to the documents of projections
/// </summary>
internal class ProjectionDocumentPolicy : IDocumentPolicy
{
    public void Apply(DocumentMapping mapping)
    {
        if (mapping.StoreOptions.Projections.TryFindAggregate(mapping.DocumentType, out var projection))
        {
            if (projection.ProjectionVersion > 1)
            {
                mapping.Alias += "_" + projection.ProjectionVersion;
            }

            mapping.UseOptimisticConcurrency = false;
            mapping.Metadata.Version.Enabled = false;
            mapping.UseNumericRevisions = true;
            mapping.UseVersionFromMatchingStream = true;
            mapping.Metadata.Revision.Enabled = true;
        }
    }
}
