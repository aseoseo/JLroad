using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Edges")]
public sealed class RoadmapEdgeEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoadmapId { get; set; }
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public string EdgeType { get; set; } = "Prerequisite";
    public bool IsDashed { get; set; }
    public int Thickness { get; set; } = 2;
    public string Color { get; set; } = "#5f84c4";
}