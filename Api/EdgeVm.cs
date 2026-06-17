using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Api;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы в PascalCase-регистре для PostgreSQL
[Table("Edges")]
public sealed class EdgeVm
{
    public int Id { get; set; }
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public string EdgeType { get; set; } = "Зависимость";
    public bool IsDashed { get; set; }
    public int Thickness { get; set; } = 2;
    public string Color { get; set; } = "#6078a8";
}