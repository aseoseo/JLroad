using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;


[Table("PasswordResetTokens")]
public sealed class PasswordResetTokenEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = "";
    

    public DateTime ExpiresAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Utc);
    
    public bool IsUsed { get; set; }
}