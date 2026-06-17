using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Nodes")]
public sealed class RoadmapNodeEntity
{
    public int Id { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public int UserId { get; set; }
    public int RoadmapId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Resource { get; set; } = "";
    public string Type { get; set; } = "Topic";
    public string Difficulty { get; set; } = "Beginner";
    public string Status { get; set; } = "Planned";
    public int Progress { get; set; }
    public string Tags { get; set; } = "";
}