using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Users")]
public sealed class AppUser
{
    public int Id { get; set; }
    
    public string? MentorCode { get; set; } // Код, который преподаватель дает своим ученикам
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Student";
    public string AvatarUrl { get; set; } = "";
    public string Bio { get; set; } = "";
    
    // ОБНОВЛЕНО: Гарантируем строгую передачу признака DateTimeKind.Utc для корректной записи в PostgreSQL
    public DateTime CreatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Tests")]
public class RoadmapTestEntity
{
    public int Id { get; set; }
    public int RoadmapId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAiGenerated { get; set; }
    
    // ОБНОВЛЕНО: Гарантируем строгую передачу признака DateTimeKind.Utc для корректной записи в PostgreSQL
    public DateTime CreatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    // Навигационные свойства
    public List<TestQuestionEntity> Questions { get; set; } = [];
}

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("TestQuestions")]
public class TestQuestionEntity
{
    public int Id { get; set; }
    public int TestId { get; set; }
    public string QuestionText { get; set; } = string.Empty;

    public List<QuestionOptionEntity> Options { get; set; } = [];
}

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("QuestionOptions")]
public class QuestionOptionEntity
{
    public int Id { get; set; }
    public int TestQuestionId { get; set; }
    public string OptionText { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("TestAttempts")]
public class UserTestAttemptEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TestId { get; set; }
    public int Score { get; set; }
    public int TotalQuestions { get; set; }
    
    // ОБНОВЛЕНО: Гарантируем строгую передачу признака DateTimeKind.Utc для корректной записи в PostgreSQL
    public DateTime CompletedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}