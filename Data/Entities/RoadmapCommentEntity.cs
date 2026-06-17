using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

[Table("Comments")]
public sealed class RoadmapCommentEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoadmapId { get; set; }
    public int? NodeId { get; set; }
    public string AuthorRole { get; set; } = "";
    public string Text { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}