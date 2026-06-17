using System;

namespace JIroad.Data.Entities;

public sealed class UserNotificationEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = "Info";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }

  
    public DateTime CreatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}