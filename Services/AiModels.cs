namespace JIroad.Services;

// --- Существующие модели для генерации Roadmap (НЕ УДАЛЕНО) ---
public class AiRoadmapData 
{ 
    public string Title { get; set; } = ""; 
    public string Description { get; set; } = ""; 
    public List<AiParsedNode> Nodes { get; set; } = new(); 
    public List<AiParsedEdge> Edges { get; set; } = new(); 
}

public class AiParsedNode 
{ 
    public int Id { get; set; } 
    public string Title { get; set; } = ""; 
    public string Description { get; set; } = ""; 
    public string Type { get; set; } = "Topic"; 
    public string Difficulty { get; set; } = "Beginner";
    public List<AiParsedMaterial> Materials { get; set; } = new();
}

public class AiParsedMaterial
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string MaterialType { get; set; } = "Article";
    public string Url { get; set; } = "";
}

public class AiParsedEdge 
{ 
    public int FromId { get; set; } 
    public int ToId { get; set; } 
}


// --- ОБНОВЛЕНО: Модели для ИИ-генерации множества тестов по узлам и материалам ---
public class AiRoadmapTestsContainer
{
    public List<AiTestData> Tests { get; set; } = new();
}

public class AiTestData
{
    public string NodeTitle { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<AiGeneratedQuestion> Questions { get; set; } = new();
}

public class AiGeneratedQuestion
{
    public string QuestionText { get; set; } = "";
    public List<AiGeneratedOption> Options { get; set; } = new();
}

public class AiGeneratedOption
{
    public string OptionText { get; set; } = "";
    public bool IsCorrect { get; set; }
}