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
        _apiKey = config["Gemini:ApiKey"] ?? "";
    }

    // Существующий метод генерации дорожной карты (НЕ УДАЛЕНО)
    public async Task<AiRoadmapData?> GenerateRoadmapAsync(string goal, string timeline, string currentLevel, string category)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key={_apiKey}";

        var prompt = $@"
    Ты — эксперт-ментор. Составь исчерпывающую и полноценную карту для достижения цели: {goal}.
        Сроки: {timeline}, уровень: {currentLevel}, категория: {category}.
Требования:
1. Включи ВСЕ необходимые темы, подтемы, инструменты, библиотеки и практические проекты. 
2. Не ограничивай количество узлов. Создай столько, сколько нужно для глубокого освоения темы.
3. Структурируй их логически: от фундаментальных основ до узкоспециализированных навыков.
4. Создай связи (edges) так, чтобы они формировали полноценный граф знаний с ветвлениями.
5. Верни ответ СТРОГО в формате JSON.
        Верни JSON:
        {{
            ""title"": ""{goal}"",
            ""nodes"": [
            {{ ""id"": 1, ""title"": ""Основы"", ""description"": ""..."", ""type"": ""Topic"", ""materials"": [{{ ""title"": ""Wiki"", ""url"": ""..."" }}] }}
            ],
            ""edges"": [{{ ""fromId"": 1, ""toId"": 2 }}]
        }}
        Важно: связи должны строить иерархическое дерево.";

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } }
        };

        var response = await _http.PostAsJsonAsync(endpoint, requestBody);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Ошибка API: {error}");
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

        var cleanJson = text!.Replace("```json", "").Replace("```", "").Trim();
        return JsonSerializer.Deserialize<AiRoadmapData>(cleanJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // ОБНОВЛЕНО: Генерация масштабных тестов (от 10 до 30 вопросов на каждый узел)
    public async Task<AiRoadmapTestsContainer?> GenerateMultipleTestsAsync(string roadmapTitle, List<string> nodesWithMaterials)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key={_apiKey}";

        var prompt = $@"
Ты — профессиональный технический экзаменатор высшей квалификации. Твоя задача — проанализировать каждый отдельный узел (этап обучения) дорожной карты и сгенерировать ИНДИВИДУАЛЬНЫЕ, подробные и объемные проверочные тесты для каждого из них.
Особенно внимательно изучи прикрепленные к узлам ссылки (URL) и названия материалов — вопросы должны досконально проверять знание концепций, заложенных в этих источниках.

Название дорожной карты: {roadmapTitle}
Список узлов и их материалов для анализа:
{string.Join("\n\n", nodesWithMaterials)}

Требования к генерации вопросов:
1. Для каждого предоставленного узла создай один полноценный тест.
2. КАЖДЫЙ ТЕСТ ДОЛЖЕН СОДЕРЖАТЬ МИНИМУМ 10–15 ВОПРОСОВ (МАКСИМУМ 30 ВОПРОСОВ), если объем темы и прикрепленных ссылок/материалов позволяет сделать глубокую проверку. Вопросы не должны быть поверхностными. Они должны охватывать синтаксис, архитектуру, практические кейсы и подводные камни темы, описанной в статьях по ссылкам.
3. Для каждого вопроса создай ровно 3 варианта ответа. Строго у ОДНОГО варианта флаг ""isCorrect"" должен быть true, у остальных двух — false.
4. Верни ответ СТРОГО в формате JSON, без какого-либо текстового или markdown-обрамления (не пиши ```json в начале).

Формат ответа JSON:
{{
    ""tests"": [
        {{
            ""nodeTitle"": ""Точное название узла из списка"",
            ""title"": ""🤖 Тест: [Название узла]"",
            ""description"": ""Комплексный срез знаний (10+ вопросов) по материалам и ссылкам темы: [Название узла]"",
            ""questions"": [
                {{
                    ""questionText"": ""Глубокий технический вопрос, проверяющий знание материала по этой ссылке/теме..."",
                    ""options"": [
                        {{ ""optionText"": ""Вариант правильного ответа"", ""isCorrect"": true }},
                        {{ ""optionText"": ""Вариант ложного ответа"", ""isCorrect"": false }},
                        {{ ""optionText"": ""Еще один ложный вариант ответа"", ""isCorrect"": false }}
                    ]
                }}
            ]
        }}
    ]
}}";

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } }
        };

        var response = await _http.PostAsJsonAsync(endpoint, requestBody);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Ошибка API при генерации множества тестов: {error}");
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

        var cleanJson = text!.Replace("```json", "").Replace("```", "").Trim();
        return JsonSerializer.Deserialize<AiRoadmapTestsContainer>(cleanJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}