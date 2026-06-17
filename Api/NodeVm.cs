using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace JIroad.Api;

// ОБНОВЛЕНО: Явно фиксируем имя таблицы для PostgreSQL, чтобы сопоставить DTO с сущностью в БД
[Table("Nodes")]
public class NodeDto
{
    public int Id { get; set; }
    public int RoadmapId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    
    // Сохранено строго double: В PostgreSQL это поле будет создано как double precision
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    
    public string Type { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string Status { get; set; } = "";
    public int Progress { get; set; }
    public string Tags { get; set; } = "";
}

public sealed class NodeVm : NodeDto
{
    // ОБНОВЛЕНО: Привязываем локальные координаты представления к базовым полям сущности
    public double X 
    { 
        get => PositionX; 
        set => PositionX = value; 
    }
    
    public double Y 
    { 
        get => PositionY; 
        set => PositionY = value; 
    }
}