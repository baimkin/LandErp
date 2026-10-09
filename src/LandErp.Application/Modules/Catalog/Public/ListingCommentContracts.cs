using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Catalog.Contracts;

using Domain;

public sealed record ListingCommentTypeView(Guid Id, string Name, string? Description,
    int SortOrder, bool IsActive, long Version);

public sealed record ListingCommentView(Guid Id, Guid ListingId, Guid CommentTypeId, string Text,
    Guid CreatedByEmployeeId, string CreatedByName, DateTimeOffset CreatedAt,
    Guid UpdatedByEmployeeId, string UpdatedByName, DateTimeOffset UpdatedAt, long Version);

public sealed record ListingCommentHistoryView(Guid Id, Guid ListingCommentId, Guid ListingId,
    Guid CommentTypeId, string OldText, Guid OldAuthorEmployeeId, string OldAuthorName,
    DateTimeOffset OldCreatedAt, DateTimeOffset OldUpdatedAt, Guid ChangedByEmployeeId,
    string ChangedByName, DateTimeOffset ChangedAt, ListingCommentHistoryOperation Operation);

public sealed record ListingCommentEditorView(IReadOnlyList<ListingCommentTypeView> Types,
    IReadOnlyList<ListingCommentView> Comments,
    IReadOnlyDictionary<Guid, IReadOnlyList<ListingCommentHistoryView>> HistoryByType,
    bool CanEdit, bool CanManageTypes);

public sealed record ListingCommentPreview(Guid Id, Guid CommentTypeId, string TypeName, string Text);

public sealed record SaveListingComment(Guid ListingId, Guid CommentTypeId, string Text,
    long? ExpectedVersion);

public sealed record SaveListingCommentType(Guid? Id, long? ExpectedVersion, string Name,
    string? Description, int SortOrder);

public sealed record SetListingCommentTypeActive(Guid Id, long ExpectedVersion, bool IsActive);

public interface IListingCommentService
{
    Task<ListingCommentEditorView> ReadAsync(Subject subject, Guid listingId,
        CancellationToken cancellationToken);
    Task<ListingCommentEditorView> SaveAsync(Subject subject, SaveListingComment command,
        string correlationId, CancellationToken cancellationToken);
    Task<ListingCommentTypeView> SaveTypeAsync(Subject subject, SaveListingCommentType command,
        string correlationId, CancellationToken cancellationToken);
    Task<ListingCommentTypeView> SetTypeActiveAsync(Subject subject,
        SetListingCommentTypeActive command, string correlationId, CancellationToken cancellationToken);
}
