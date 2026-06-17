using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Invites")]
public sealed class RoadmapInviteEntity
{
    public int Id { get; set; }
    public int RoadmapId { get; set; }
    public int InviterUserId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Viewer";
    public string Status { get; set; } = "Pending";

    // ОБНОВЛЕНО: Гарантируем строгую передачу признака DateTimeKind.Utc для корректной записи в PostgreSQL
    public DateTime CreatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}