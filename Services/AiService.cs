using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace JIroad.Services;

public class AiService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public AiService(HttpClient http, IConfiguration config)
    {
        _http = http;
        // Считываем ключ из конфигурации (в Railway это будет Gemini__ApiKey)
        _apiKey = config["Gemini:ApiKey"] ?? "";
    }

    public async Task<AiRoadmapData?> GenerateRoadmapAsync(string goal, string timeline, string currentLevel, string category)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Console.WriteLine("[AI ERROR]: API Key (Gemini:ApiKey) пустой или не найден в конфигурации!");
            return null;
        }

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

        var prompt = $@"
Ты — эксперт-ментор. Составь исчерпывающую и полноценную карту для достижения цели: {goal}.
Сроки: {timeline}, уровень: {currentLevel}, категория: {category}.
Требования:
1. Включи все необходимые темы, подтемы, инструменты и практические проекты.
2. Не ограничивай количество узлов. Создай столько, сколько нужно для глубокого освоения темы.
3. Структурируй их логически: от фундаментальных основ до узкоспециализированных навыков.
4. Верни ответ СТРОГО в формате JSON по указанной схеме.

Схема JSON:
{{
    ""title"": ""{goal}"",
    ""nodes"": [
        {{ ""id"": 1, ""title"": ""Название темы"", ""description"": ""Описание"", ""type"": ""Topic"", ""materials"": [{{ ""title"": ""Документация"", ""url"": ""https://example.com"" }}] }}
    ],
    ""edges"": [{{ ""fromId"": 1, ""toId"": 2 }}]
}}";

        // ДОБАВЛЕНО ТРЕБОВАНИЕ JSON MIME-TYPE НА УРОВНЕ API GOOGLE
        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        try
        {
            var response = await _http.PostAsJsonAsync(endpoint, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Ошибка API Gemini (Roadmap): {error}");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);
            var text = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(text)) return null;

            return JsonSerializer.Deserialize<AiRoadmapData>(text.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiService Exception]: {ex.Message}");
            return null;
        }
    }

    public async Task<AiRoadmapTestsContainer?> GenerateMultipleTestsAsync(string roadmapTitle, List<string> nodesWithMaterials)
    {
        if (string.IsNullOrEmpty(_apiKey)) return null;

        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

        var prompt = $@"
Ты — профессиональный технический экзаменатор. Твоя задача — сгенерировать короткие проверочные тесты для узлов дорожной карты.
Название карты: {roadmapTitle}
Узлы: {string.Join(", ", nodesWithMaterials)}

Требования:
1. Для каждого узла создай тест из 3-5 вопросов (для экономии токенов квоты).
2. Для каждого вопроса создай ровно 3 варианта ответа. Строго у одного флага ""isCorrect"" должен быть true.
3. Верни ответ СТРОГО в формате JSON.

Схема JSON:
{{
    ""tests"": [
        {{
            ""nodeTitle"": ""Название узла"",
            ""title"": ""Тест по теме"",
            ""description"": ""Проверка знаний"",
            ""questions"": [
                {{
                    ""questionText"": ""Текст вопроса?"",
                    ""options"": [
                        {{ ""optionText"": ""Правильный ответ"", ""isCorrect"": true }},
                        {{ ""optionText"": ""Неверный ответ"", ""isCorrect"": false }},
                        {{ ""optionText"": ""Еще неверный"", ""isCorrect"": false }}
                    ]
                }}
            ]
        }}
    ]
}}";

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        try
        {
            var response = await _http.PostAsJsonAsync(endpoint, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Ошибка API Gemini (Tests): {error}");
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(jsonResponse);
            var text = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrWhiteSpace(text)) return null;

            return JsonSerializer.Deserialize<AiRoadmapTestsContainer>(text.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiService Tests Exception]: {ex.Message}");
            return null;
        }
    }
}