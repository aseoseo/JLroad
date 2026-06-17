using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Data.Entities;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в верхнем регистре для PostgreSQL
[Table("Materials")]
public sealed class NodeMaterialEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoadmapId { get; set; }
    public int NodeId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string MaterialType { get; set; } = "Article";
    public string Url { get; set; } = "";
}