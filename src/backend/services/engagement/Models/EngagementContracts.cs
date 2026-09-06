using System.ComponentModel.DataAnnotations;

namespace StreamForge.Engagement.Api.Models;

public sealed record VideoSummaryResponse(
    Guid VideoId,
    long LikeCount,
    long DislikeCount,
    long ViewCount,
    long CommentCount);

public sealed record ReactionResponse(string Reaction);

public sealed record ReactionRequest(
    [Required, RegularExpression("^(like|dislike|none)$")] string Reaction);

public sealed record ReactionUpdateResponse(
    string Reaction,
    long? LikeCount,
    long? DislikeCount,
    bool CountsPending);

public sealed record ViewRequest(Guid ViewSessionId);

public sealed record ViewAcceptedResponse(bool Counted, long? ViewCount, bool CountsPending);

public sealed record CommentRequest([Required, StringLength(2_000, MinimumLength = 1)] string Body);

public sealed record CommentResponse(
    Guid Id,
    Guid VideoId,
    Guid AuthorId,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CommentPageResponse(
    IReadOnlyList<CommentResponse> Items,
    long TotalCount,
    string? NextCursor);

public sealed record CommentMutationResponse(CommentResponse Comment, long CommentCount);

public sealed record CommentDeletedResponse(long CommentCount);
