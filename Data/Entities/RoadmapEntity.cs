using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Roadmaps")]
public sealed class RoadmapEntity
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Difficulty { get; set; } = "Beginner";
    public string Tags { get; set; } = "";
    public bool IsPublic { get; set; }
    public int Progress { get; set; }
    
    // Поле для сохранения визуального состояния бесконечного холста
    public string CanvasData { get; set; } = "{}";
    
    // ОБНОВЛЕНО: Гарантируем строгую передачу признака DateTimeKind.Utc для корректной записи в PostgreSQL
    public DateTime CreatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    public DateTime UpdatedAtUtc { get; set; } = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}