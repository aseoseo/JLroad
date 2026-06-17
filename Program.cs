using System.Security.Claims;
using System.Text;
using JIroad.Services;
using System.Text.Json;
using JIroad.Api;
using JIroad.Components;
using JIroad.Data;
using JIroad.Data.Entities;
using JIroad.Hubs;
using JIroad.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// КОРРЕКТНАЯ НАСТРОЙКА: Дополняем конфигурацию без полной очистки системных источников Railway
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables(); // Читает переменные из облака (ConnectionStrings__DefaultConnection)

// Add services to the container.
builder.Services.AddHttpClient<AiService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();

// СКОРРЕКТИРОВАНО: Инициализация контекста
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "change-this-super-secret-key-32-chars";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "JIroad";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "JIroadClient";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/collab"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // ИСПРАВЛЕНО ДЛЯ POSTGRESQL: Экранирование имен в верхнем регистре кавычками для информационного каталога
    try
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name 
            FROM information_schema.columns 
            WHERE table_name = 'PersonalTasks';";
        
        var columns = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                columns.Add(reader["column_name"]?.ToString() ?? "");
            }
        }

        if (!columns.Contains("StudentResponse") && !columns.Contains("student_response")) 
            db.Database.ExecuteSqlRaw("ALTER TABLE \"PersonalTasks\" ADD COLUMN \"StudentResponse\" TEXT NULL;");
        if (!columns.Contains("MentorComment") && !columns.Contains("mentor_comment")) 
            db.Database.ExecuteSqlRaw("ALTER TABLE \"PersonalTasks\" ADD COLUMN \"MentorComment\" TEXT NULL;");
        if (!columns.Contains("SubmittedAtUtc") && !columns.Contains("submitted_at_utc")) 
            db.Database.ExecuteSqlRaw("ALTER TABLE \"PersonalTasks\" ADD COLUMN \"SubmittedAtUtc\" TIMESTAMP WITH TIME ZONE NULL;");
        if (!columns.Contains("ReviewedAtUtc") && !columns.Contains("reviewed_at_utc")) 
            db.Database.ExecuteSqlRaw("ALTER TABLE \"PersonalTasks\" ADD COLUMN \"ReviewedAtUtc\" TIMESTAMP WITH TIME ZONE NULL;");
    }
    catch { }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

var api = app.MapGroup("/api");

api.MapPost("/auth/register", async (AuthRequest request, AppDbContext db, IPasswordHasher<AppUser> hasher, JwtTokenService jwt) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Email and password are required.");
    }
    var email = request.Email.Trim().ToLowerInvariant();
    var exists = await db.Users.AnyAsync(x => x.Email == email);
    if (exists)
    {
        return Results.Conflict("Email already exists.");
    }
    string? generatedMentorCode = null;
    if (request.Role == "Mentor")
    {
        generatedMentorCode = "JR-" + Random.Shared.Next(100000, 999999);
    }
    else if (request.Role == "Student")
    {
        var code = request.MentorCode;
        
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest("Mentor code is required for students.");
        }
        var mentorExists = await db.Users.AnyAsync(x => x.Role == "Mentor" && x.MentorCode == code.Trim());
        if (!mentorExists)
        {
            return Results.BadRequest("Invalid mentor code.");
        }
    }
    var user = new AppUser
    {
        Email = email,
        UserName = email.Split('@')[0],
        Role = request.Role, 
        MentorCode = generatedMentorCode,
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();

    if (request.Role == "Student")
    {
        var code = request.MentorCode ?? "";
        var mentor = await db.Users.FirstAsync(x => x.Role == "Mentor" && x.MentorCode == code.Trim());
        db.MentorStudents.Add(new MentorStudentEntity
        {
            MentorUserId = mentor.Id,
            StudentUserId = user.Id,
            AssignedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
    }

    if (request.Role != "Mentor")
    {
        db.Roadmaps.Add(new RoadmapEntity
        {
            OwnerUserId = user.Id,
            Title = "Моя первая дорожная карта",
            Description = "Стартовый шаблон для обучения",
            Category = "General",
            Difficulty = "Beginner",
            Tags = "starter",
            IsPublic = false,
            Progress = 0,
            CanvasData = "{}", 
            CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
    }
    return Results.Ok(new AuthResponse(jwt.Create(user), user.Email, user.Role));
});

api.MapPost("/auth/login", async (AuthRequest request, AppDbContext db, IPasswordHasher<AppUser> hasher, JwtTokenService jwt) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Email and password are required.");
    }
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
    if (user is null)
    {
        return Results.Unauthorized();
    }
    var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (verify == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }
    return Results.Ok(new AuthResponse(jwt.Create(user), user.Email, user.Role));
});

api.MapPost("/auth/forgot-password", async (ForgotPasswordRequest request, AppDbContext db) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
    if (user is null) return Results.Ok(new { Message = "If user exists, reset link was sent." });
    
    var token = Guid.NewGuid().ToString("N"); 
    
    db.PasswordResetTokens.Add(new PasswordResetTokenEntity
    {
        UserId = user.Id,
        Token = token,
        ExpiresAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Utc),
        IsUsed = false
    });
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Reset token generated.", Token = token });
});

api.MapPost("/auth/reset-password", async (ResetPasswordRequest request, AppDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
    if (user is null) return Results.BadRequest("Invalid token.");
    var token = await db.PasswordResetTokens
        .OrderByDescending(x => x.Id)
        .FirstOrDefaultAsync(x => x.UserId == user.Id && x.Token == request.Token && !x.IsUsed);
    if (token is null || token.ExpiresAtUtc < DateTime.UtcNow) return Results.BadRequest("Invalid token.");
    token.IsUsed = true;
    user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Password changed." });
});

var roadmap = api.MapGroup("/roadmap").RequireAuthorization();

roadmap.MapGet("/profile", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
    if (user is null) return Results.Unauthorized();
    return Results.Ok(new UserProfileResponse(user.Email, user.UserName, user.Role, user.AvatarUrl, user.Bio, user.MentorCode));
});

roadmap.MapPut("/profile", async (ClaimsPrincipal principal, UpdateProfileRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
    if (user is null) return Results.Unauthorized();
    user.UserName = request.UserName.Trim();
    user.AvatarUrl = request.AvatarUrl.Trim();
    user.Bio = request.Bio.Trim();
    await db.SaveChangesAsync();
    return Results.NoContent();
});

roadmap.MapPost("/templates/{id:int}/clone", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    try
    {
        var userId = GetUserId(principal);
        var originalRoadmap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == id && x.IsPublic);
        if (originalRoadmap is null) return Results.NotFound("Template not found.");
        var newRoadmap = new RoadmapEntity
        {
            Title = $"{originalRoadmap.Title} (Clone)",
            Description = originalRoadmap.Description,
            Category = originalRoadmap.Category,
            Difficulty = originalRoadmap.Difficulty,
            Tags = originalRoadmap.Tags,
            IsPublic = false, 
            Progress = 0,
            OwnerUserId = userId,
            CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            CanvasData = originalRoadmap.CanvasData
        };
        db.Roadmaps.Add(newRoadmap);
        await db.SaveChangesAsync();
        var originalNodes = await db.Nodes.Where(x => x.RoadmapId == id).ToListAsync();
        var nodeMapping = new Dictionary<int, int>();
        foreach (var oldNode in originalNodes)
        {
            var newNode = new RoadmapNodeEntity
            {
                RoadmapId = newRoadmap.Id,
                Title = oldNode.Title,
                Description = oldNode.Description,
                Resource = oldNode.Resource,
                Type = oldNode.Type,
                Difficulty = oldNode.Difficulty,
                Status = "Planned",
                Progress = 0,
                Tags = oldNode.Tags,
                PositionX = oldNode.PositionX,
                PositionY = oldNode.PositionY,
                UserId = userId
            };
            db.Nodes.Add(newNode);
            await db.SaveChangesAsync();
            nodeMapping.Add(oldNode.Id, newNode.Id);
        }
        var originalEdges = await db.Edges.Where(x => x.RoadmapId == id).ToListAsync();
        foreach (var oldEdge in originalEdges)
        {
            if (nodeMapping.TryGetValue(oldEdge.FromNodeId, out int newFromId) && 
                nodeMapping.TryGetValue(oldEdge.ToNodeId, out int newToId))
            {
                var newEdge = new RoadmapEdgeEntity
                {
                    RoadmapId = newRoadmap.Id,
                    FromNodeId = newFromId,
                    ToNodeId = newToId,
                    EdgeType = oldEdge.EdgeType,
                    IsDashed = oldEdge.IsDashed,
                    Thickness = oldEdge.Thickness,
                    Color = oldEdge.Color,
                    UserId = userId
                };
                db.Edges.Add(newEdge);
            }
        }
        await db.SaveChangesAsync();
        return Results.Ok(new { NewRoadmapId = newRoadmap.Id });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error]: {ex.Message}");
        return Results.Problem("Error during cloning.");
    }
});

roadmap.MapPost("/{id:int}/publish", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var entity = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId); 
    if (entity is null) return Results.NotFound();
    entity.IsPublic = true;
    entity.UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Published successfully!" });
});

roadmap.MapPost("/change-password", async (ClaimsPrincipal principal, ChangePasswordRequest request, AppDbContext db, IPasswordHasher<AppUser> hasher) =>
{
    var userId = GetUserId(principal);
    var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId);
    if (user is null) return Results.Unauthorized();
    var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
    if (verify == PasswordVerificationResult.Failed) return Results.BadRequest("Current password is invalid.");
    user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

roadmap.MapGet("/list", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    try
    {
        var userId = GetUserId(principal); 

        // Дельта-синхронизация всех групповых треков студента перед выводом списка
        var userRoadmaps = await db.Roadmaps.Where(x => x.OwnerUserId == userId).ToListAsync();
        foreach (var r in userRoadmaps)
        {
            await SyncStudentRoadmapIfGroupAsync(r.Id, db);
        }

        var dbRoadmaps = await db.Roadmaps
            .Where(x => x.OwnerUserId == userId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync();
        var responseList = new List<RoadmapResponse>();
        foreach (var x in dbRoadmaps)
        {
            var rNodes = await db.Nodes.Where(n => n.RoadmapId == x.Id).ToListAsync();
            int nodesCount = rNodes.Count; 
            int currentCalculatedProgress = nodesCount > 0 
                ? (int)Math.Round(rNodes.Average(n => n.Progress)) 
                : 0;
            if (x.Progress != currentCalculatedProgress)
            {
                x.Progress = currentCalculatedProgress;
                x.UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            }
            responseList.Add(new RoadmapResponse(
                x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc,
                nodesCount, x.CanvasData));
        }
        await db.SaveChangesAsync();
        if (!responseList.Any())
        {
            return Results.Ok(new DashboardDataResponse(new List<RoadmapResponse>(), new GlobalDashboardAnalytics(0, 0, 0, 0)));
        }
        int total = responseList.Count;
        int completed = responseList.Count(r => r.Progress == 100);
        int inProgress = responseList.Count(r => r.Progress > 0 && r.Progress < 100);
        double avgProgress = Math.Round(responseList.Average(r => r.Progress), 1);
        var analytics = new GlobalDashboardAnalytics(total, completed, inProgress, avgProgress);
        return Results.Ok(new DashboardDataResponse(responseList, analytics));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API DASHBOARD ERROR]: {ex.Message}");
        return Results.Problem("Error loading dashboard data.");
    }
});

roadmap.MapGet("/students-roadmaps", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var role = principal.FindFirstValue(ClaimTypes.Role);
    if (role != "Mentor" && role != "Admin") return Results.Forbid();
    var data = await db.Roadmaps
        .Join(db.Users, r => r.OwnerUserId, u => u.Id, (r, u) => new { r, u })
        .Where(x => x.u.Role == "Student") 
        .OrderByDescending(x => x.r.UpdatedAtUtc)
        .Select(x => new {
            RoadmapId = x.r.Id,
            Title = x.r.Title,
            StudentEmail = x.u.Email,
            StudentName = x.u.UserName,
            Progress = x.r.Progress,
            Category = x.r.Category,
            Difficulty = x.r.Difficulty,
            UpdatedAt = x.r.UpdatedAtUtc
        })
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapGet("/templates/public", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    try
    {
        var userId = GetUserId(principal);
        var dbTemplates = await db.Roadmaps
            .Where(x => x.IsPublic)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync();
        var responseList = new List<RoadmapResponse>();
        foreach (var x in dbTemplates)
        {
            int nodesCount = await db.Nodes.CountAsync(n => n.RoadmapId == x.Id);
            responseList.Add(new RoadmapResponse(
                x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc,
                nodesCount, x.CanvasData));
        }
        return Results.Ok(responseList);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[API LIBRARY ERROR]: {ex.Message}");
        return Results.Problem("Error loading templates.");
    }
});

roadmap.MapPost("/create", async (ClaimsPrincipal principal, RoadmapCreateRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var roadmapEntity = new RoadmapEntity
    {
        OwnerUserId = userId,
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        Category = request.Category.Trim(),
        Difficulty = request.Difficulty,
        Tags = request.Tags.Trim(),
        IsPublic = request.IsPublic,
        Progress = 0,
        CanvasData = "{}", 
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Roadmaps.Add(roadmapEntity);
    await db.SaveChangesAsync();
    return Results.Ok(new RoadmapResponse(roadmapEntity.Id, roadmapEntity.Title, roadmapEntity.Description, roadmapEntity.Category, roadmapEntity.Difficulty, roadmapEntity.Tags, roadmapEntity.IsPublic, roadmapEntity.Progress, roadmapEntity.UpdatedAtUtc, 0, roadmapEntity.CanvasData));
});

roadmap.MapPut("/{id:int}", async (int id, ClaimsPrincipal principal, RoadmapUpdateRequest request, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    var roadmapEntity = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == id);
    if (roadmapEntity is null) return Results.NotFound(); 

    // Защита структуры: запрещаем студенту менять разметку холста и метаданные курса ментора
    if (role == "Student")
    {
        bool isGroupTrack = await db.Roadmaps.AnyAsync(m => m.Title == roadmapEntity.Title && db.Users.Any(u => u.Id == m.OwnerUserId && u.Role == "Mentor"));
        if (isGroupTrack)
        {
            roadmapEntity.Progress = Math.Clamp(request.Progress, 0, 100);
            roadmapEntity.UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }
    }

    bool isMentor = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == roadmapEntity.OwnerUserId);
    if (roadmapEntity.OwnerUserId != userId && !isMentor) return Results.Forbid(); 
    
    roadmapEntity.Title = request.Title.Trim();
    roadmapEntity.Description = request.Description.Trim();
    roadmapEntity.Category = request.Category.Trim();
    roadmapEntity.Difficulty = request.Difficulty;
    roadmapEntity.Tags = request.Tags.Trim();
    roadmapEntity.IsPublic = request.IsPublic;
    roadmapEntity.Progress = Math.Clamp(request.Progress, 0, 100); 
    if (!string.IsNullOrWhiteSpace(request.CanvasData)) 
        roadmapEntity.CanvasData = request.CanvasData; 
    roadmapEntity.UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    await db.SaveChangesAsync();

    // ДОБАВЛЕНО: Уведомление студентам при обновлении структуры преподавателем
    if (role == "Mentor")
    {
        var assignedStudents = await db.MentorStudents.Where(ms => ms.MentorUserId == userId).Select(ms => ms.StudentUserId).ToListAsync();
        foreach (var sid in assignedStudents)
        {
            var sMap = await db.Roadmaps.FirstOrDefaultAsync(rm => rm.OwnerUserId == sid && rm.Title == roadmapEntity.Title);
            if (sMap != null)
            {
                db.Notifications.Add(new UserNotificationEntity
                {
                    UserId = sid, Kind = "Roadmap", Title = "Учебный трек обновлен 🗺️",
                    Message = $"Преподаватель внес изменения в структуру графа '{roadmapEntity.Title}'. Нажмите, чтобы открыть холст.",
                    IsRead = false, CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                });
                await hub.Clients.User(sid.ToString()).SendAsync("NotificationReceived", $"/canvas/{sMap.Id}");
            }
        }
        await db.SaveChangesAsync();
    }

    return Results.NoContent();
});

roadmap.MapDelete("/{id:int}", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var roadmapEntity = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId);
    if (roadmapEntity is null) return Results.NotFound();
    db.Roadmaps.Remove(roadmapEntity);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

roadmap.MapGet("/{roadmapId:int}/nodes", async (int roadmapId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role); 

    // Синхронизируем узлы перед открытием холста у студента
    await SyncStudentRoadmapIfGroupAsync(roadmapId, db);

    var r = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == roadmapId);
    if (r is null) return Results.NotFound(); 
    bool isMentor = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == r.OwnerUserId);
    if (r.OwnerUserId != userId && role != "Mentor" && !r.IsPublic && !isMentor) return Results.Forbid();
    var data = await db.Nodes.Where(x => x.RoadmapId == roadmapId)
        .OrderBy(x => x.Id)
        .Select(x => new NodeResponse(x.Id, x.RoadmapId, x.Title, x.Description, x.Resource, x.Type, x.Difficulty, x.Status, x.Progress, x.Tags, x.PositionX, x.PositionY))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapPost("/ai/generate", async (ClaimsPrincipal principal, AiGenerateRequest request, AppDbContext db, AiService ai) =>
{
    var userId = GetUserId(principal);
    var generatedData = await ai.GenerateRoadmapAsync(request.Goal, request.Timeline, request.CurrentLevel, request.Category); 
    if (generatedData == null) return Results.BadRequest("AI Generation failed.");
    var roadmapEntity = new RoadmapEntity {
        OwnerUserId = userId, Title = generatedData.Title, Description = generatedData.Description,
        Category = request.Category, Difficulty = request.CurrentLevel, CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Roadmaps.Add(roadmapEntity);
    await db.SaveChangesAsync();
    var idMap = new Dictionary<int, int>();
    var nodesList = generatedData.Nodes;
    for (int i = 0; i < nodesList.Count; i++)
    {
        var aiNode = nodesList[i];
        double level = i / 3; 
        double branch = i % 3;
        var node = new RoadmapNodeEntity {
            UserId = userId, RoadmapId = roadmapEntity.Id, Title = aiNode.Title,
            Description = aiNode.Description, Type = aiNode.Type, Difficulty = aiNode.Difficulty,
            Status = "Planned", Progress = 0,
            PositionX = 100 + (branch * 300), 
            PositionY = 100 + (level * 180) 
        };
        db.Nodes.Add(node);
        await db.SaveChangesAsync(); 
        idMap[aiNode.Id] = node.Id;
        foreach (var m in aiNode.Materials)
        {
            db.Materials.Add(new NodeMaterialEntity {
                UserId = userId, RoadmapId = roadmapEntity.Id, NodeId = node.Id,
                Title = m.Title, Description = m.Description, MaterialType = m.MaterialType, Url = m.Url
            });
        }
    }
    foreach (var aiEdge in generatedData.Edges)
    {
        if (idMap.ContainsKey(aiEdge.FromId) && idMap.ContainsKey(aiEdge.ToId))
        {
            db.Edges.Add(new RoadmapEdgeEntity {
                UserId = userId, RoadmapId = roadmapEntity.Id,
                FromNodeId = idMap[aiEdge.FromId], ToNodeId = idMap[aiEdge.ToId],
                EdgeType = "Solid", Thickness = 2, Color = "#6078a8"
            });
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { RoadmapId = roadmapEntity.Id });
});

roadmap.MapPost("/nodes", async (ClaimsPrincipal principal, NodeRequest request, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    var rMap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == request.RoadmapId);
    if (rMap is null) return Results.BadRequest("Roadmap not found."); 

    if (role == "Student")
    {
        bool isGroupTrack = await db.Roadmaps.AnyAsync(m => m.Title == rMap.Title && db.Users.Any(u => u.Id == m.OwnerUserId && u.Role == "Mentor"));
        if (isGroupTrack) return Results.Forbid();
    }

    bool isMentor = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == rMap.OwnerUserId);
    if (rMap.OwnerUserId != userId && !isMentor) return Results.Forbid(); 

    var node = new RoadmapNodeEntity
    {
        UserId = rMap.OwnerUserId, 
        RoadmapId = request.RoadmapId,
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        Resource = request.Resource.Trim(),
        Type = request.Type,
        Difficulty = request.Difficulty,
        Status = request.Status,
        Progress = Math.Clamp(request.Progress, 0, 100),
        Tags = request.Tags.Trim(),
        PositionX = request.PositionX,
        PositionY = request.PositionY
    };
    db.Nodes.Add(node);
    await db.SaveChangesAsync(); 
    await RecalculateRoadmapProgressAsync(request.RoadmapId, db); 
    await hub.Clients.User(rMap.OwnerUserId.ToString()).SendAsync("NodeUpdated", request.RoadmapId);
    return Results.Ok(new NodeResponse(node.Id, node.RoadmapId, node.Title, node.Description, node.Resource, node.Type, node.Difficulty, node.Status, node.Progress, node.Tags, node.PositionX, node.PositionY));
});

roadmap.MapPut("/nodes/{id:int}", async (int id, ClaimsPrincipal principal, NodeRequest request, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    var node = await db.Nodes.FirstOrDefaultAsync(x => x.Id == id);
    if (node is null) return Results.NotFound();
    var rMap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == node.RoadmapId);

    if (role == "Student")
    {
        bool isGroupTrack = await db.Roadmaps.AnyAsync(m => m.Title == rMap!.Title && db.Users.Any(u => u.Id == m.OwnerUserId && u.Role == "Mentor"));
        if (isGroupTrack)
        {
            node.Status = request.Status;
            node.Progress = Math.Clamp(request.Progress, 0, 100);
            await db.SaveChangesAsync();
            await RecalculateRoadmapProgressAsync(node.RoadmapId, db);
            await hub.Clients.User(rMap!.OwnerUserId.ToString()).SendAsync("NodeUpdated", node.RoadmapId);
            return Results.NoContent();
        }
    }

    bool isMentor = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == rMap!.OwnerUserId);
    if (rMap!.OwnerUserId != userId && !isMentor) return Results.Forbid();

    node.Title = request.Title.Trim();
    node.Description = request.Description.Trim();
    node.Resource = request.Resource.Trim();
    node.Type = request.Type;
    node.Difficulty = request.Difficulty;
    node.Status = request.Status;
    node.Progress = Math.Clamp(request.Progress, 0, 100);
    node.Tags = request.Tags.Trim();
    node.PositionX = request.PositionX;
    node.PositionY = request.PositionY;
    await db.SaveChangesAsync(); 
    await RecalculateRoadmapProgressAsync(node.RoadmapId, db); 
    await hub.Clients.User(rMap.OwnerUserId.ToString()).SendAsync("NodeUpdated", rMap.OwnerUserId.ToString());
    return Results.NoContent();
});

roadmap.MapDelete("/nodes/{id:int}", async (int id, ClaimsPrincipal principal, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    var node = await db.Nodes.FirstOrDefaultAsync(x => x.Id == id);
    if (node is null) return Results.NotFound();
    var rMap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == node.RoadmapId);

    if (role == "Student")
    {
        bool isGroupTrack = await db.Roadmaps.AnyAsync(m => m.Title == rMap!.Title && db.Users.Any(u => u.Id == m.OwnerUserId && u.Role == "Mentor"));
        if (isGroupTrack) return Results.Forbid();
    }

    if (node.UserId != userId && (rMap is null || rMap.OwnerUserId != userId)) return Results.Forbid();

    var roadmapId = node.RoadmapId;
    db.Nodes.Remove(node);
    await db.SaveChangesAsync(); 
    await RecalculateRoadmapProgressAsync(roadmapId, db); 
    await hub.Clients.User(userId.ToString()).SendAsync("NodeUpdated", roadmapId);
    return Results.NoContent();
});

roadmap.MapGet("/{roadmapId:int}/edges", async (int roadmapId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role); 
    var r = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == roadmapId);
    if (r is null) return Results.NotFound(); 
    bool isMentor = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == r.OwnerUserId);
    if (r.OwnerUserId != userId && role != "Mentor" && !r.IsPublic && !isMentor) return Results.Forbid();
    var data = await db.Edges.Where(x => x.RoadmapId == roadmapId)
        .OrderBy(x => x.Id)
        .Select(x => new EdgeResponse(x.Id, x.RoadmapId, x.FromNodeId, x.ToNodeId, x.EdgeType, x.IsDashed, x.Thickness, x.Color))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapPost("/edges", async (ClaimsPrincipal principal, EdgeRequest request, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    var rMap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == request.RoadmapId);

    // Студент не может соединять линии на общем треке ментора
    if (role == "Student" && rMap != null)
    {
        bool isGroupTrack = await db.Roadmaps.AnyAsync(m => m.Title == rMap.Title && db.Users.Any(u => u.Id == m.OwnerUserId && u.Role == "Mentor"));
        if (isGroupTrack) return Results.Forbid();
    }

    var fromNodeExists = await db.Nodes.AnyAsync(x => x.Id == request.FromNodeId);
    var toNodeExists = await db.Nodes.AnyAsync(x => x.Id == request.ToNodeId);
    if (!fromNodeExists || !toNodeExists)
    {
        return Results.BadRequest("Nodes not found.");
    }
    var edge = new RoadmapEdgeEntity
    {
        UserId = userId,
        RoadmapId = request.RoadmapId,
        FromNodeId = request.FromNodeId,
        ToNodeId = request.ToNodeId,
        EdgeType = request.EdgeType,
        IsDashed = request.IsDashed,
        Thickness = request.Thickness <= 0 ? 2 : request.Thickness, 
        Color = string.IsNullOrWhiteSpace(request.Color) ? "#6078a8" : request.Color
    };
    db.Edges.Add(edge);
    await db.SaveChangesAsync(); 
    await hub.Clients.User(userId.ToString()).SendAsync("EdgeUpdated", request.RoadmapId);
    return Results.Ok(new EdgeResponse(edge.Id, edge.RoadmapId, edge.FromNodeId, edge.ToNodeId, edge.EdgeType, edge.IsDashed, edge.Thickness, edge.Color));
});

roadmap.MapDelete("/edges/{id:int}", async (int id, ClaimsPrincipal principal, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    var edge = await db.Edges.FirstOrDefaultAsync(x => x.Id == id);
    if (edge is null) return Results.NotFound();
    var roadmapEntity = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == edge.RoadmapId);

    // Студент не может удалять линии на общем треке ментора
    if (role == "Student" && roadmapEntity != null)
    {
        bool isGroupTrack = await db.Roadmaps.AnyAsync(m => m.Title == roadmapEntity.Title && db.Users.Any(u => u.Id == m.OwnerUserId && u.Role == "Mentor"));
        if (isGroupTrack) return Results.Forbid();
    }

    if (edge.UserId != userId && (roadmapEntity is null || roadmapEntity.OwnerUserId != userId))
    {
        return Results.Forbid(); 
    }
    var roadmapId = edge.RoadmapId;
    db.Edges.Remove(edge);
    await db.SaveChangesAsync(); 
    await hub.Clients.User(userId.ToString()).SendAsync("EdgeUpdated", roadmapId);
    return Results.NoContent();
});

roadmap.MapGet("/{roadmapId:int}/comments", async (int roadmapId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var data = await db.Comments.Where(x => x.RoadmapId == roadmapId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new CommentResponse(x.Id, x.RoadmapId, x.NodeId, x.AuthorRole, x.Text, x.CreatedAtUtc))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapGet("/public-templates", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var templates = await db.Roadmaps
        .Where(x => x.IsPublic && x.OwnerUserId != userId)
        .OrderByDescending(x => x.UpdatedAtUtc)
        .Select(x => new RoadmapResponse(
            x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc,
            db.Nodes.Count(n => n.RoadmapId == x.Id), x.CanvasData))
        .ToListAsync();
    return Results.Ok(templates);
});

roadmap.MapGet("/{id:int}/analytics", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var roadmapExists = await db.Roadmaps.AnyAsync(x => x.Id == id);
    if (!roadmapExists) return Results.NotFound();
    var nodes = await db.Nodes.Where(x => x.RoadmapId == id).ToListAsync();
    var edges = await db.Edges.Where(x => x.RoadmapId == id).ToListAsync();
    if (nodes.Count == 0)
    {
        return Results.Ok(new RoadmapAnalyticsResponse(0, 0, 0, 0, 0, edges.Count, [], [], []));
    }
    int totalNodes = nodes.Count;
    int completed = nodes.Count(n => n.Status == "Completed" || n.Progress == 100);
    int inProgress = nodes.Count(n => n.Status == "In Progress" || (n.Progress > 0 && n.Progress < 100));
    int planned = nodes.Count(n => n.Status == "Planned" || n.Progress == 0);
    double overallProgress = Math.Round(nodes.Average(n => n.Progress), 2);
    var nodesByType = nodes
        .GroupBy(n => n.Type)
        .Select(g => new NodeTypeCountDto(g.Key, g.Count()))
        .ToList();
    var nodesByDifficulty = nodes
        .GroupBy(n => n.Difficulty)
        .Select(g => new NodeDifficultyCountDto(g.Key, g.Count()))
        .ToList();
    var recentActivities = new List<RecentActivityDto>();
    if (completed > 0) 
        recentActivities.Add(new RecentActivityDto($"Completed nodes count: {completed} out of {totalNodes}.", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)));
    if (edges.Count > 0)
        recentActivities.Add(new RecentActivityDto($"Roadmap structures connected with {edges.Count} elements.", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)));
    return Results.Ok(new RoadmapAnalyticsResponse(
        totalNodes, completed, inProgress, planned, overallProgress, edges.Count,
        nodesByType, nodesByDifficulty, recentActivities
    ));
});

roadmap.MapPost("/comments", async (ClaimsPrincipal principal, CommentRequest request, AppDbContext db, IHubContext<CollaborationHub> hub) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role) ?? "Student";
    var comment = new RoadmapCommentEntity
    {
        UserId = userId,
        RoadmapId = request.RoadmapId,
        NodeId = request.NodeId,
        AuthorRole = role,
        Text = request.Text.Trim(),
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Comments.Add(comment);

    // Автоматическое определение адресата комментария и отправка SignalR с точной ссылкой на холст
    int targetNotificationUserId = userId;
    var currentMap = await db.Roadmaps.FindAsync(request.RoadmapId);
    if (currentMap != null)
    {
        if (role == "Mentor")
        {
            targetNotificationUserId = currentMap.OwnerUserId; 
        }
        else
        {
            var assignedMentorId = await db.MentorStudents.Where(ms => ms.StudentUserId == userId).Select(ms => ms.MentorUserId).FirstOrDefaultAsync();
            if (assignedMentorId != 0) targetNotificationUserId = assignedMentorId; 
        }
    }

    db.Notifications.Add(new UserNotificationEntity
    {
        UserId = targetNotificationUserId,
        Kind = "Comment",
        Title = "Новое сообщение в обсуждении 💬",
        Message = $"Оставлен новый комментарий: '{comment.Text}'. Нажмите, чтобы открыть холст.",
        IsRead = false,
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    });
    await db.SaveChangesAsync();

    await hub.Clients.User(targetNotificationUserId.ToString()).SendAsync("NotificationReceived", $"/canvas/{request.RoadmapId}");
    await hub.Clients.All.SendAsync("CommentAdded", request.RoadmapId);
    return Results.Ok(new CommentResponse(comment.Id, comment.RoadmapId, comment.NodeId, comment.AuthorRole, comment.Text, comment.CreatedAtUtc));
});

roadmap.MapGet("/{roadmapId:int}/materials", async (int roadmapId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role); 
    var r = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == roadmapId);
    if (r is null) return Results.NotFound();
    if (r.OwnerUserId != userId && role != "Mentor" && !r.IsPublic) return Results.Forbid();
    var data = await db.Materials
        .Where(x => x.RoadmapId == roadmapId)
        .OrderBy(x => x.Id)
        .Select(x => new MaterialResponse(x.Id, x.RoadmapId, x.NodeId, x.Title, x.Description, x.MaterialType, x.Url))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapPost("/materials", async (ClaimsPrincipal principal, MaterialRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var material = new NodeMaterialEntity
    {
        UserId = userId,
        RoadmapId = request.RoadmapId,
        NodeId = request.NodeId,
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        MaterialType = request.MaterialType,
        Url = request.Url.Trim()
    };
    db.Materials.Add(material);
    await db.SaveChangesAsync();
    return Results.Ok(new MaterialResponse(material.Id, material.RoadmapId, material.NodeId, material.Title, material.Description, material.MaterialType, material.Url));
});

roadmap.MapPut("/materials/{id:int}", async (int id, ClaimsPrincipal principal, MaterialRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var material = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
    if (material is null) return Results.NotFound();
    material.Title = request.Title.Trim();
    material.Description = request.Description.Trim();
    material.MaterialType = request.MaterialType;
    material.Url = request.Url.Trim();
    await db.SaveChangesAsync();
    return Results.NoContent();
});

roadmap.MapDelete("/materials/{id:int}", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var material = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
    if (material is null) return Results.NotFound();
    db.Materials.Remove(material);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

roadmap.MapPost("/invites", async (ClaimsPrincipal principal, InviteRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var roadmapExists = await db.Roadmaps.AnyAsync(x => x.Id == request.RoadmapId && x.OwnerUserId == userId);
    if (!roadmapExists) return Results.NotFound();
    var invite = new RoadmapInviteEntity
    {
        RoadmapId = request.RoadmapId,
        InviterUserId = userId,
        Email = request.Email.Trim().ToLowerInvariant(),
        Role = request.Role,
        Status = "Pending",
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Invites.Add(invite);
    await db.SaveChangesAsync();
    return Results.Ok(new InviteResponse(invite.Id, invite.RoadmapId, invite.Email, invite.Role, invite.Status, invite.CreatedAtUtc));
});

roadmap.MapGet("/invites", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var data = await db.Invites.Where(x => x.InviterUserId == userId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new InviteResponse(x.Id, x.RoadmapId, x.Email, x.Role, x.Status, x.CreatedAtUtc))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapGet("/notifications", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var data = await db.Notifications.Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new NotificationResponse(x.Id, x.Kind, x.Title, x.Message, x.IsRead, x.CreatedAtUtc))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapPost("/notifications/{id:int}/read", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var notification = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
    if (notification is null) return Results.NotFound();
    notification.IsRead = true;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

roadmap.MapGet("/search", async (ClaimsPrincipal principal, AppDbContext db, string? q, string? difficulty, string? category) =>
{
    var userId = GetUserId(principal);
    var query = db.Roadmaps.Where(x => x.OwnerUserId == userId || x.IsPublic);
    if (!string.IsNullOrWhiteSpace(q))
    {
        q = q.Trim().ToLowerInvariant();
        query = query.Where(x => x.Title.ToLower().Contains(q) || x.Tags.ToLower().Contains(q) || x.Description.ToLower().Contains(q));
    }
    if (!string.IsNullOrWhiteSpace(difficulty))
    {
        query = query.Where(x => x.Difficulty == difficulty);
    }
    if (!string.IsNullOrWhiteSpace(category))
    {
        query = query.Where(x => x.Category == category);
    }
    var data = await query
        .OrderByDescending(x => x.UpdatedAtUtc)
        .Take(100)
        .Select(x => new RoadmapResponse(x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc, db.Nodes.Count(n => n.RoadmapId == x.Id), x.CanvasData))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapGet("/{roadmapId:int}/export/json", async (int roadmapId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role); 
    var r = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == roadmapId);
    if (r is null) return Results.NotFound();
    if (r.OwnerUserId != userId && role != "Mentor" && !r.IsPublic) return Results.Forbid();
    var nodes = await db.Nodes.Where(x => x.RoadmapId == roadmapId).ToListAsync();
    var edges = await db.Edges.Where(x => x.RoadmapId == roadmapId).ToListAsync();
    var materials = await db.Materials.Where(x => x.RoadmapId == roadmapId).ToListAsync();
    var payload = new { roadmap = r, nodes, edges, materials };
    return Results.Ok(payload);
});

roadmap.MapPost("/import/json", async (ClaimsPrincipal principal, AppDbContext db, JsonElement payload) =>
{
    var userId = GetUserId(principal);
    if (!payload.TryGetProperty("roadmap", out var roadmapJson)) return Results.BadRequest("Invalid payload.");
    var title = roadmapJson.TryGetProperty("title", out var t) ? t.GetString() ?? "Imported roadmap" : "Imported roadmap";
    var description = roadmapJson.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
    var category = roadmapJson.TryGetProperty("category", out var c) ? c.GetString() ?? "General" : "General";
    var difficulty = roadmapJson.TryGetProperty("difficulty", out var diff) ? diff.GetString() ?? "Beginner" : "Beginner";
    var tags = roadmapJson.TryGetProperty("tags", out var tg) ? tg.GetString() ?? "imported" : "imported";
    var roadmapEntity = new RoadmapEntity
    {
        OwnerUserId = userId,
        Title = title,
        Description = description,
        Category = category,
        Difficulty = difficulty,
        Tags = tags,
        IsPublic = false,
        Progress = 0,
        CanvasData = "{}", 
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Roadmaps.Add(roadmapEntity);
    await db.SaveChangesAsync();
    return Results.Ok(new { RoadmapId = roadmapEntity.Id });
});

app.MapGet("/api/library", async (AppDbContext db, string? q, string? difficulty, string? category) =>
{
    var query = db.Roadmaps.Where(x => x.IsPublic);
    if (!string.IsNullOrWhiteSpace(q))
    {
        q = q.Trim().ToLowerInvariant();
        query = query.Where(x => x.Title.ToLower().Contains(q) || x.Tags.ToLower().Contains(q) || x.Description.ToLower().Contains(q));
    }
    if (!string.IsNullOrWhiteSpace(difficulty))
    {
        query = query.Where(x => x.Difficulty == difficulty);
    }
    if (!string.IsNullOrWhiteSpace(category))
    {
        query = query.Where(x => x.Category == category);
    }
    var data = await query
        .OrderByDescending(x => x.UpdatedAtUtc)
        .Take(100)
        .Select(x => new RoadmapResponse(x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc, db.Nodes.Count(n => n.RoadmapId == x.Id), x.CanvasData))
        .ToListAsync();
    return Results.Ok(data);
});

roadmap.MapGet("/{id:int}/tests", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var roadmapExists = await db.Roadmaps.AnyAsync(x => x.Id == id);
    if (!roadmapExists) return Results.NotFound();
    var tests = await db.Tests
        .Where(x => x.RoadmapId == id)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(t => new { t.Id, t.Title, t.Description, t.IsAiGenerated, t.CreatedAtUtc })
        .ToListAsync();
    return Results.Ok(tests);
});

roadmap.MapGet("/tests/{testId:int}", async (int testId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var test = await db.Tests
        .Include(t => t.Questions)
        .ThenInclude(q => q.Options)
        .FirstOrDefaultAsync(x => x.Id == testId);

    if (test is null) return Results.NotFound();

    var response = new TestResponse(
        test.Id, test.RoadmapId, test.Title, test.Description, test.IsAiGenerated,
        test.Questions.Select(q => new QuestionResponse(
            q.Id, 
            q.QuestionText,
            q.Options.Select(o => new OptionResponse(o.Id, o.OptionText, o.IsCorrect)).ToList()
        )).ToList()
    );

    return Results.Ok(response);
});

roadmap.MapPost("/{id:int}/tests", async (int id, ClaimsPrincipal principal, CreateTestRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var roadmapEntity = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == id);
    if (roadmapEntity is null) return Results.NotFound();
    bool isMentor = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == roadmapEntity.OwnerUserId);
    if (roadmapEntity.OwnerUserId != userId && !isMentor) return Results.Forbid();
    var testEntity = new RoadmapTestEntity
    {
        RoadmapId = id,
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        IsAiGenerated = false
    };
    db.Tests.Add(testEntity);
    await db.SaveChangesAsync();
    foreach (var q in request.Questions)
    {
        var questionEntity = new TestQuestionEntity
        {
            TestId = testEntity.Id,
            QuestionText = q.QuestionText.Trim()
        };
        db.TestQuestions.Add(questionEntity);
        await db.SaveChangesAsync();
        foreach (var o in q.Options)
        {
            db.QuestionOptions.Add(new QuestionOptionEntity
            {
                TestQuestionId = questionEntity.Id,
                OptionText = o.OptionText.Trim(),
                IsCorrect = o.IsCorrect
            });
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { TestId = testEntity.Id });
});

roadmap.MapPost("/tests/{testId:int}/submit", async (int testId, ClaimsPrincipal principal, SubmitTestRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var test = await db.Tests
        .Include(t => t.Questions)
        .ThenInclude(q => q.Options)
        .FirstOrDefaultAsync(x => x.Id == testId);
    if (test is null) return Results.NotFound();
    int score = 0;
    int total = test.Questions.Count;
    foreach (var q in test.Questions)
    {
        var userAns = request.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
        if (userAns != null)
        {
            var correctOption = q.Options.FirstOrDefault(o => o.IsCorrect);
            if (correctOption != null && correctOption.Id == userAns.SelectedOptionId)
            {
                score++;
            }
        }
    }
    var attempt = new UserTestAttemptEntity
    {
        UserId = userId,
        TestId = testId,
        Score = score,
        TotalQuestions = total,
        CompletedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.TestAttempts.Add(attempt);
    await db.SaveChangesAsync();
    return Results.Ok(new { Score = score, TotalQuestions = total });
});

roadmap.MapPost("/{id:int}/tests/ai-generate", async (int id, ClaimsPrincipal principal, AppDbContext db, AiService ai) =>
{
    var userId = GetUserId(principal); 
    var rMap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == id);
    if (rMap is null) return Results.NotFound();
    var nodes = await db.Nodes.Where(x => x.RoadmapId == id).ToListAsync();
    if (!nodes.Any()) return Results.BadRequest("Nodes context is missing.");
    var materials = await db.Materials.Where(x => x.RoadmapId == id).ToListAsync();
    var nodesWithMaterials = new List<string>();
    foreach (var n in nodes)
    {
        var nodeMaterials = materials.Where(m => m.NodeId == n.Id).ToList();
        var mText = nodeMaterials.Any() 
            ? string.Join(", ", nodeMaterials.Select(m => $"Material: '{m.Title}' (URL: {m.Url})"))
            : "No materials linked";
        nodesWithMaterials.Add($"Node: '{n.Title}'\nDescription: {n.Description}\nMaterials URL: mText");
    }
    var generatedContainer = await ai.GenerateMultipleTestsAsync(rMap.Title, nodesWithMaterials);
    if (generatedContainer == null || generatedContainer.Tests == null || !generatedContainer.Tests.Any()) 
        return Results.BadRequest("AI Test container layout is empty.");
    foreach (var testData in generatedContainer.Tests)
    {
        var testEntity = new RoadmapTestEntity
        {
            RoadmapId = id,
            Title = string.IsNullOrWhiteSpace(testData.Title) ? $"Test module: {testData.NodeTitle}" : testData.Title,
            Description = string.IsNullOrWhiteSpace(testData.Description) ? $"Automatic test coverage for {testData.NodeTitle}" : testData.Description,
            IsAiGenerated = true,
            CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        };
        db.Tests.Add(testEntity);
        await db.SaveChangesAsync();
        foreach (var q in testData.Questions)
        {
            var questionEntity = new TestQuestionEntity
            {
                TestId = testEntity.Id,
                QuestionText = q.QuestionText.Trim()
            };
            db.TestQuestions.Add(questionEntity);
            await db.SaveChangesAsync();
            foreach (var opt in q.Options)
            {
                db.QuestionOptions.Add(new QuestionOptionEntity
                {
                    TestQuestionId = questionEntity.Id,
                    OptionText = opt.OptionText.Trim(),
                    IsCorrect = opt.IsCorrect
                });
            }
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { Success = true, CreatedTestsCount = generatedContainer.Tests.Count });
});

roadmap.MapPost("/mentor/students/add", async (ClaimsPrincipal principal, [FromBody] JsonElement payload, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    if (role != "Mentor" && role != "Admin") return Results.Forbid();
    if (!payload.TryGetProperty("email", out var emailProp))
        return Results.BadRequest("Email configuration is missing.");
        
    var emailStr = emailProp.GetString();
    if (string.IsNullOrWhiteSpace(emailStr))
        return Results.BadRequest("Email configuration is empty.");

    var studentEmail = emailStr.Trim().ToLowerInvariant();
    var student = await db.Users.FirstOrDefaultAsync(u => u.Email == studentEmail); 
    if (student is null) return Results.NotFound("Student email records not found.");
    if (student.Role != "Student") return Results.BadRequest("Assigned user role mismatch.");
    var alreadyExists = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == student.Id);
    if (alreadyExists) return Results.Conflict("Student configuration already tied.");
    
    db.MentorStudents.Add(new MentorStudentEntity { MentorUserId = userId, StudentUserId = student.Id, AssignedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc) });
    await db.SaveChangesAsync();
    return Results.Ok(new { Message = "Student linked successfully!" });
});

roadmap.MapGet("/mentor/students/list", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var role = principal.FindFirstValue(ClaimTypes.Role);
    if (role != "Mentor" && role != "Admin") return Results.Forbid();
    var students = await db.MentorStudents
        .Where(ms => ms.MentorUserId == userId)
        .Join(db.Users, ms => ms.StudentUserId, u => u.Id, (ms, u) => new { u.Id, u.UserName, u.Email, u.AvatarUrl })
        .ToListAsync();
    return Results.Ok(students);
});

roadmap.MapGet("/mentor/students/{studentId:int}/roadmaps", async (int studentId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    bool isAssigned = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == studentId);
    if (!isAssigned) return Results.Forbid();
    var rMaps = await db.Roadmaps
        .Where(r => r.OwnerUserId == studentId)
        .OrderByDescending(r => r.UpdatedAtUtc)
        .ToListAsync();
    var list = new List<RoadmapResponse>();
    foreach (var x in rMaps)
    {
        var rNodes = await db.Nodes.Where(n => n.RoadmapId == x.Id).ToListAsync();
        int nodesCount = rNodes.Count;
        int currentCalculatedProgress = nodesCount > 0 
            ? (int)Math.Round(rNodes.Average(n => n.Progress)) 
            : 0;
        if (x.Progress != currentCalculatedProgress)
        {
            x.Progress = currentCalculatedProgress;
            await db.SaveChangesAsync();
        }
        list.Add(new RoadmapResponse(x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc, nodesCount, x.CanvasData));
    }
    return Results.Ok(list);
});

roadmap.MapPost("/mentor/students/{studentId:int}/create-manual", async (int studentId, ClaimsPrincipal principal, RoadmapCreateRequest request, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    bool isAssigned = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == studentId);
    if (!isAssigned) return Results.Forbid();
    var roadmapEntity = new RoadmapEntity
    {
        OwnerUserId = studentId,
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        Category = request.Category.Trim(),
        Difficulty = request.Difficulty,
        Tags = request.Tags.Trim(),
        IsPublic = false,
        Progress = 0,
        CanvasData = "{}",
        CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
        UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Roadmaps.Add(roadmapEntity);
    await db.SaveChangesAsync();
    return Results.Ok();
});

roadmap.MapPost("/mentor/students/{studentId:int}/create-ai", async (int studentId, ClaimsPrincipal principal, AiGenerateRequest request, AppDbContext db, AiService ai) =>
{
    var userId = GetUserId(principal);
    bool isAssigned = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == userId && ms.StudentUserId == studentId);
    if (!isAssigned) return Results.Forbid();
    var generatedData = await ai.GenerateRoadmapAsync(request.Goal, request.Timeline, request.CurrentLevel, request.Category);
    if (generatedData == null) return Results.BadRequest("Generation fault.");
    var roadmapEntity = new RoadmapEntity {
        OwnerUserId = studentId, 
        Title = generatedData.Title, Description = generatedData.Description,
        Category = request.Category, Difficulty = request.CurrentLevel, CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
    };
    db.Roadmaps.Add(roadmapEntity);
    await db.SaveChangesAsync();
    var idMap = new Dictionary<int, int>();
    var nodesList = generatedData.Nodes;
    for (int i = 0; i < nodesList.Count; i++)
    {
        var aiNode = nodesList[i];
        var node = new RoadmapNodeEntity {
            UserId = studentId, RoadmapId = roadmapEntity.Id, Title = aiNode.Title,
            Description = aiNode.Description, Type = aiNode.Type, Difficulty = aiNode.Difficulty,
            Status = "Planned", Progress = 0, PositionX = 100 + ((i % 3) * 300), PositionY = 100 + ((i / 3) * 180)
        };
        db.Nodes.Add(node);
        await db.SaveChangesAsync();
        idMap[aiNode.Id] = node.Id;
        foreach (var m in aiNode.Materials)
        {
            db.Materials.Add(new NodeMaterialEntity {
                UserId = studentId, RoadmapId = roadmapEntity.Id, NodeId = node.Id,
                Title = m.Title, Description = m.Description, MaterialType = m.MaterialType, Url = m.Url
            });
        }
    }
    foreach (var aiEdge in generatedData.Edges)
    {
        if (idMap.ContainsKey(aiEdge.FromId) && idMap.ContainsKey(aiEdge.ToId))
        {
            db.Edges.Add(new RoadmapEdgeEntity {
                UserId = studentId, RoadmapId = roadmapEntity.Id,
                FromNodeId = idMap[aiEdge.FromId], ToNodeId = idMap[aiEdge.ToId],
                EdgeType = "Solid", Thickness = 2, Color = "#6078a8"
            });
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

roadmap.MapGet("/mentor/master-roadmaps", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var mentorId = GetUserId(principal);
    var roadmaps = await db.Roadmaps
        .Where(r => r.OwnerUserId == mentorId)
        .OrderByDescending(r => r.UpdatedAtUtc)
        .ToListAsync();
    var responseList = new List<RoadmapResponse>();
    foreach (var x in roadmaps)
    {
        int nodesCount = await db.Nodes.CountAsync(n => n.RoadmapId == x.Id);
        responseList.Add(new RoadmapResponse(
            x.Id, x.Title, x.Description, x.Category, x.Difficulty, x.Tags, x.IsPublic, x.Progress, x.UpdatedAtUtc,
            nodesCount, x.CanvasData));
    }
    return Results.Ok(responseList);
});

roadmap.MapGet("/mentor/master-roadmaps/{id:int}/progress", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var mentorId = GetUserId(principal);
    var studentIds = await db.MentorStudents.Where(ms => ms.MentorUserId == mentorId).Select(ms => ms.StudentUserId).ToListAsync();
    var masterRoadmap = await db.Roadmaps.FindAsync(id);
    if (masterRoadmap == null) return Results.NotFound();
    var progressList = await db.Roadmaps.Where(r => studentIds.Contains(r.OwnerUserId) && r.Title == masterRoadmap.Title).Select(r => new { StudentId = r.OwnerUserId, StudentRoadmapId = r.Id, StudentName = db.Users.Where(u => u.Id == r.OwnerUserId).Select(u => u.UserName).FirstOrDefault() ?? "Student User", StudentEmail = db.Users.Where(u => u.Id == r.OwnerUserId).Select(u => u.Email).FirstOrDefault() ?? "", Progress = r.Progress }).ToListAsync();
    return Results.Ok(progressList);
});

roadmap.MapPost("/mentor/master-roadmaps/{id:int}/enroll", async (int id, [FromBody] JsonElement payload, ClaimsPrincipal principal, AppDbContext db) =>
{
    var mentorId = GetUserId(principal);
    if (!payload.TryGetProperty("studentId", out var studentIdProp)) return Results.BadRequest("Invalid student ID payload.");
    int studentId = studentIdProp.GetInt32();

    bool isAssigned = await db.MentorStudents.AnyAsync(ms => ms.MentorUserId == mentorId && ms.StudentUserId == studentId);
    if (!isAssigned) return Results.Forbid();

    var masterMap = await db.Roadmaps.FindAsync(id);
    if (masterMap == null) return Results.NotFound("Master roadmap not found.");

    bool alreadyEnrolled = await db.Roadmaps.AnyAsync(r => r.OwnerUserId == studentId && r.Title == masterMap.Title);
    if (alreadyEnrolled) return Results.Conflict("Student already enrolled in this course group.");

    var newRoadmap = new RoadmapEntity { Title = masterMap.Title, Description = masterMap.Description, Category = masterMap.Category, Difficulty = masterMap.Difficulty, Tags = masterMap.Tags, IsPublic = false, Progress = 0, OwnerUserId = studentId, CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), CanvasData = masterMap.CanvasData };
    db.Roadmaps.Add(newRoadmap);
    await db.SaveChangesAsync();

    var originalNodes = await db.Nodes.Where(x => x.RoadmapId == id).ToListAsync();
    var nodeMapping = new Dictionary<int, int>();
    foreach (var oldNode in originalNodes)
    {
        var newNode = new RoadmapNodeEntity
        {
            RoadmapId = newRoadmap.Id,
            Title = oldNode.Title,
            Description = oldNode.Description,
            Resource = oldNode.Resource,
            Type = oldNode.Type,
            Difficulty = oldNode.Difficulty,
            Status = "Planned",
            Progress = 0,
            Tags = oldNode.Tags,
            PositionX = oldNode.PositionX,
            PositionY = oldNode.PositionY,
            UserId = studentId
        };
        db.Nodes.Add(newNode);
        await db.SaveChangesAsync();
        nodeMapping.Add(oldNode.Id, newNode.Id);
    }

    var originalEdges = await db.Edges.Where(x => x.RoadmapId == id).ToListAsync();
    foreach (var oldEdge in originalEdges)
    {
        if (nodeMapping.TryGetValue(oldEdge.FromNodeId, out int newFromId) && 
            nodeMapping.TryGetValue(oldEdge.ToNodeId, out int newToId))
        {
            db.Edges.Add(new RoadmapEdgeEntity
            {
                RoadmapId = newRoadmap.Id,
                FromNodeId = newFromId,
                ToNodeId = newToId,
                EdgeType = oldEdge.EdgeType,
                IsDashed = oldEdge.IsDashed,
                Thickness = oldEdge.Thickness,
                Color = oldEdge.Color,
                UserId = studentId
            });
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

roadmap.MapPost("/mentor/tasks/assign", async (AssignTaskRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var mentorId = GetUserId(principal);
    var newTask = new PersonalTaskEntity { MentorUserId = mentorId, StudentUserId = request.StudentId, RoadmapId = request.RoadmapId, TaskType = request.TaskType, Title = request.Title, Content = request.Content, Status = "Assigned", CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), StudentResponse = "", MentorComment = "" };
    db.PersonalTasks.Add(newTask);
    await db.SaveChangesAsync();
    return Results.Ok();
});

roadmap.MapPost("/mentor/tasks/assign-group", async (AssignGroupTaskRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var mentorId = GetUserId(principal);
    var newGroupTask = new GroupTaskEntity { MentorUserId = mentorId, RoadmapId = request.RoadmapId, TaskType = request.TaskType, Title = request.Title, Content = request.Content, CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc) };
    db.GroupTasks.Add(newGroupTask);
    await db.SaveChangesAsync();
    return Results.Ok();
});

roadmap.MapGet("/mentor/students/personal-tasks/{roadmapId:int}", async (int roadmapId, ClaimsPrincipal principal, AppDbContext db) =>
{
    var personal = await db.PersonalTasks.Where(t => t.RoadmapId == roadmapId).Select(t => new { t.Id, t.TaskType, t.Title, t.Content, t.Status, t.StudentResponse, t.MentorComment, IsGroupTask = false }).ToListAsync();
    return Results.Ok(personal);
});

roadmap.MapPost("/mentor/tasks/review", async (ReviewTaskRequest request, AppDbContext db) =>
{
    if (request.IsGroupTask)
    {
        var sub = await db.GroupTaskSubmissions.FirstOrDefaultAsync(x => x.GroupTaskId == request.TaskId);
        if (sub != null)
        {
            sub.Status = request.Status;
            sub.MentorComment = request.Comment;
            sub.ReviewedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        }
    }
    else
    {
        var task = await db.PersonalTasks.FindAsync(request.TaskId);
        if (task != null)
        {
            task.Status = request.Status;
            task.MentorComment = request.Comment;
            task.ReviewedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

roadmap.MapGet("/student/tasks", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var personal = await db.PersonalTasks.Where(t => t.StudentUserId == userId).Select(t => new { t.Id, t.Title, t.Content, t.TaskType, t.Status, t.StudentResponse, t.MentorComment, RoadmapTitle = db.Roadmaps.Where(r => r.Id == t.RoadmapId).Select(r => r.Title).FirstOrDefault() ?? "", IsGroupTask = false }).ToListAsync();
    return Results.Ok(personal);
});

roadmap.MapPost("/student/tasks/submit", async (SubmitTaskSolutionRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    if (request.IsGroupTask)
    {
        var existing = await db.GroupTaskSubmissions.FirstOrDefaultAsync(x => x.GroupTaskId == request.TaskId && x.StudentUserId == userId);
        if (existing != null)
        {
            existing.StudentResponse = request.Response;
            existing.Status = "Submitted";
            existing.SubmittedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        }
        else
        {
            db.GroupTaskSubmissions.Add(new GroupTaskSubmissionEntity
            {
                GroupTaskId = request.TaskId,
                StudentUserId = userId,
                StudentResponse = request.Response,
                Status = "Submitted",
                SubmittedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
            });
        }
    }
    else
    {
        var task = await db.PersonalTasks.FindAsync(request.TaskId);
        if (task != null)
        {
            task.StudentResponse = request.Response;
            task.Status = "Submitted";
            task.SubmittedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapHub<CollaborationHub>("/hubs/collab");
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static int GetUserId(ClaimsPrincipal principal)
{
    var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return int.TryParse(claim, out var id) ? id : throw new UnauthorizedAccessException("Invalid user identifier mapping.");
}

static async Task RecalculateRoadmapProgressAsync(int roadmapId, AppDbContext db)
{
    var rMap = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == roadmapId);
    if (rMap != null)
    {
        var roadmapNodes = await db.Nodes.Where(n => n.RoadmapId == roadmapId).ToListAsync();
        if (roadmapNodes.Any())
        {
            rMap.Progress = (int)Math.Round(roadmapNodes.Average(n => n.Progress));
        }
        else
        {
            rMap.Progress = 0;
        }
        rMap.UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        await db.SaveChangesAsync();
    }
}

static async Task SyncStudentRoadmapIfGroupAsync(int roadmapId, AppDbContext db)
{
    var studentRm = await db.Roadmaps.FirstOrDefaultAsync(x => x.Id == roadmapId);
    if (studentRm == null) return;

    var masterRm = await db.Roadmaps.FirstOrDefaultAsync(x => x.Title == studentRm.Title && db.Users.Any(u => u.Id == x.OwnerUserId && u.Role == "Mentor"));
    if (masterRm == null) return;

    if (studentRm.Description != masterRm.Description || studentRm.CanvasData != masterRm.CanvasData)
    {
        studentRm.Description = masterRm.Description;
        studentRm.Category = masterRm.Category;
        studentRm.Difficulty = masterRm.Difficulty;
        studentRm.Tags = masterRm.Tags;
        studentRm.CanvasData = masterRm.CanvasData;
        studentRm.UpdatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    }

    var masterNodes = await db.Nodes.Where(n => n.RoadmapId == masterRm.Id).ToListAsync();
    var studentNodes = await db.Nodes.Where(n => n.RoadmapId == studentRm.Id).ToListAsync();

    foreach (var mn in masterNodes)
    {
        var sn = studentNodes.FirstOrDefault(x => x.Title == mn.Title);
        if (sn != null)
        {
            sn.Description = mn.Description;
            sn.Resource = mn.Resource;
            sn.Type = mn.Type;
            sn.Difficulty = mn.Difficulty;
            sn.Tags = mn.Tags;
            sn.PositionX = mn.PositionX;
            sn.PositionY = mn.PositionY;
        }
        else
        {
            db.Nodes.Add(new RoadmapNodeEntity
            {
                RoadmapId = studentRm.Id, UserId = studentRm.OwnerUserId, Title = mn.Title,
                Description = mn.Description, Resource = mn.Resource, Type = mn.Type,
                Difficulty = mn.Difficulty, Tags = mn.Tags, PositionX = mn.PositionX, PositionY = mn.PositionY,
                Status = "Planned", Progress = 0
            });
        }
    }

    foreach (var sn in studentNodes)
    {
        if (!masterNodes.Any(x => x.Title == sn.Title))
        {
            db.Nodes.Remove(sn);
        }
    }
    await db.SaveChangesAsync();

    studentNodes = await db.Nodes.Where(n => n.RoadmapId == studentRm.Id).ToListAsync();

    var currentStudentEdges = await db.Edges.Where(e => e.RoadmapId == studentRm.Id).ToListAsync();
    db.Edges.RemoveRange(currentStudentEdges);

    var masterEdges = await db.Edges.Where(e => e.RoadmapId == masterRm.Id).ToListAsync();
    foreach (var me in masterEdges)
    {
        var masterFrom = masterNodes.FirstOrDefault(n => n.Id == me.FromNodeId);
        var masterTo = masterNodes.FirstOrDefault(n => n.Id == me.ToNodeId);

        if (masterFrom != null && masterTo != null)
        {
            var studentFrom = studentNodes.FirstOrDefault(n => n.Title == masterFrom.Title);
            var studentTo = studentNodes.FirstOrDefault(n => n.Title == masterTo.Title);

            if (studentFrom != null && studentTo != null)
            {
                db.Edges.Add(new RoadmapEdgeEntity
                {
                    RoadmapId = studentRm.Id, UserId = studentRm.OwnerUserId,
                    FromNodeId = studentFrom.Id, ToNodeId = studentTo.Id,
                    EdgeType = me.EdgeType, IsDashed = me.IsDashed, Thickness = me.Thickness, Color = me.Color
                });
            }
        }
    }
    await db.SaveChangesAsync();
    await RecalculateRoadmapProgressAsync(studentRm.Id, db);
}

namespace JIroad.Api
{
    public record AssignTaskRequest(int StudentId, int RoadmapId, string TaskType, string Title, string Content);
    public record AssignGroupTaskRequest(int RoadmapId, string TaskType, string Title, string Content);
    public record ReviewTaskRequest(int TaskId, string Status, string Comment, bool IsGroupTask);
    public record SubmitTaskSolutionRequest(int TaskId, string Response, bool IsGroupTask);
    
    public record TestResponse(int Id, int RoadmapId, string Title, string Description, bool IsAiGenerated, List<QuestionResponse> Questions);
    public record QuestionResponse(int Id, string QuestionText, List<OptionResponse> Options);
    public record OptionResponse(int Id, string OptionText, bool IsCorrect);
    public record CreateTestRequest(string Title, string Description, List<CreateQuestionRequest> Questions);
    public record CreateQuestionRequest(string QuestionText, List<CreateOptionRequest> Options); 
    public record CreateOptionRequest(string OptionText, bool IsCorrect); 
    public record SubmitTestRequest(List<AnswerSubmissionDto> Answers); 
    public record AnswerSubmissionDto(int QuestionId, int SelectedOptionId); 
}