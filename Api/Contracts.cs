using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JIroad.Api;

// --- КОНТРАКТЫ АУТЕНТИФИКАЦИИ И ПРОФИЛЯ ---
public sealed record AuthRequest(string Email, string Password, string Role, string? MentorCode = null);
public sealed record AuthResponse(string Token, string Email, string Role);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record UserProfileResponse(string Email, string UserName, string Role, string AvatarUrl, string Bio, string? MentorCode);
public sealed record UpdateProfileRequest(string UserName, string AvatarUrl, string Bio, string Language, string Theme);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// --- КОНТРАКТЫ ДОРОЖНЫХ КАРТ (ROADMAPS) ---
public sealed record RoadmapCreateRequest(string Title, string Description, string Category, string Difficulty, string Tags, bool IsPublic);
public sealed record RoadmapUpdateRequest(string Title, string Description, string Category, string Difficulty, string Tags, bool IsPublic, int Progress, string? CanvasData);
public sealed record RoadmapResponse(int Id, string Title, string Description, string Category, string Difficulty, string Tags, bool IsPublic, int Progress, DateTime UpdatedAtUtc, int NodesCount, string CanvasData)
{
    // ИСПРАВЛЕНО ДЛЯ POSTGRESQL: Гарантируем, что дата всегда имеет статус UTC для драйвера Npgsql
    private readonly DateTime _updatedAtUtc = DateTime.SpecifyKind(UpdatedAtUtc, DateTimeKind.Utc);
    public DateTime UpdatedAtUtc { get => _updatedAtUtc; init => _updatedAtUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

// --- КОНТРАКТЫ УЗЛОВ И СВЯЗЕЙ (NODES & EDGES) ---
public sealed record NodeRequest(int RoadmapId, string Title, string Description, string Resource, string Type, string Difficulty, string Status, int Progress, string Tags, double PositionX, double PositionY);
public sealed record NodeResponse(int Id, int RoadmapId, string Title, string Description, string Resource, string Type, string Difficulty, string Status, int Progress, string Tags, double PositionX, double PositionY);
public sealed record EdgeRequest(int RoadmapId, int FromNodeId, int ToNodeId, string EdgeType, bool IsDashed, int Thickness, string Color);
public sealed record EdgeResponse(int Id, int RoadmapId, int FromNodeId, int ToNodeId, string EdgeType, bool IsDashed, int Thickness, string Color);

// --- КОНТРАКТЫ АНАЛИТИКИ И РАБОЧЕЙ ЗОНЫ (DASHBOARD) ---
public record RoadmapAnalyticsResponse(
    int TotalNodes,
    int CompletedNodes,
    int InProgressNodes,
    int PlannedNodes,
    double OverallProgress, // Сохранено double (PostgreSQL double precision)
    int TotalEdges,
    List<NodeTypeCountDto> NodesByType,
    List<NodeDifficultyCountDto> NodesByDifficulty,
    List<RecentActivityDto> RecentActivities
);

public record DashboardDataResponse(
    List<RoadmapResponse> Roadmaps,
    GlobalDashboardAnalytics Analytics
);

public record GlobalDashboardAnalytics(
    int TotalRoadmaps,
    int FullyCompletedRoadmaps,
    int InProgressRoadmaps,
    double AverageGlobalProgress // Сохранено double для безопасного маппинга AVG() из Postgres
);

public record NodeTypeCountDto(string Type, int Count);
public record NodeDifficultyCountDto(string Difficulty, int Count);

public record RecentActivityDto(string Message, DateTime Timestamp)
{
    // ИСПРАВЛЕНО ДЛЯ POSTGRESQL: Принудительная установка UtcKind для логов активности
    private readonly DateTime _timestamp = DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc);
    public DateTime Timestamp { get => _timestamp; init => _timestamp = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

// --- КОНТРАКТЫ СИСТЕМЫ КОММЕНТАРИЕВ И СОВМЕСТНОЙ РАБОТЫ ---
public sealed record CommentRequest(int RoadmapId, int? NodeId, string Text);
public sealed record CommentResponse(int Id, int RoadmapId, int? NodeId, string AuthorRole, string Text, DateTime CreatedAtUtc)
{
    private readonly DateTime _createdAtUtc = DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc);
    public DateTime CreatedAtUtc { get => _createdAtUtc; init => _createdAtUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

public sealed record InviteRequest(int RoadmapId, string Email, string Role);
public sealed record InviteResponse(int Id, int RoadmapId, string Email, string Role, string Status, DateTime CreatedAtUtc)
{
    private readonly DateTime _createdAtUtc = DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc);
    public DateTime CreatedAtUtc { get => _createdAtUtc; init => _createdAtUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

// --- КОНТРАКТЫ УВЕДОМЛЕНИЙ, МАТЕРИАЛОВ И ИИ ---
public sealed record NotificationResponse(int Id, string Kind, string Title, string Message, bool IsRead, DateTime CreatedAtUtc)
{
    private readonly DateTime _createdAtUtc = DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc);
    public DateTime CreatedAtUtc { get => _createdAtUtc; init => _createdAtUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

public sealed record MaterialRequest(int RoadmapId, int NodeId, string Title, string Description, string MaterialType, string Url);
public sealed record MaterialResponse(int Id, int RoadmapId, int NodeId, string Title, string Description, string MaterialType, string Url);
public sealed record AiGenerateRequest(string Goal, string Timeline, string CurrentLevel, string Category);