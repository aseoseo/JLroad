using JIroad.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JIroad.Data;

// Новая сущность для привязки Учеников к конкретному Ментору
public class MentorStudentEntity
{
    public int Id { get; set; }
    public int MentorUserId { get; set; }
    public int StudentUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}

// Сущность для хранения индивидуальных заданий в любых форматах (добавлены поля ответа и проверки)
public class PersonalTaskEntity
{
    public int Id { get; set; }
    public int MentorUserId { get; set; }
    public int StudentUserId { get; set; }
    public int RoadmapId { get; set; }
    public string TaskType { get; set; } = "Text"; // Video, Link, Text
    public string Title { get; set; } = "";
    public string Content { get; set; } = ""; // Текст задания или ссылка на видео/репозиторий
    public string Status { get; set; } = "Assigned"; // Assigned, Submitted, Approved, Rejected
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // --- Добавленные поля для реализации отправки результатов и проверки ментором ---
    public string? StudentResponse { get; set; } // Ответ учащегося (текст или ссылка)
    public string? MentorComment { get; set; }   // Фидбек от ментора
    public DateTime? SubmittedAtUtc { get; set; } // Дата отправки решения
    public DateTime? ReviewedAtUtc { get; set; }  // Дата проверки решения ментором
}

// Групповое задание, привязанное к конкретной дорожной карте (Roadmap) группы
public class GroupTaskEntity
{
    public int Id { get; set; }
    public int MentorUserId { get; set; }
    public int RoadmapId { get; set; } // Привязка к общей Roadmap
    public string TaskType { get; set; } = "Text"; // Video, Link, Text
    public string Title { get; set; } = "";
    public string Content { get; set; } = ""; // Текст общего задания для всех
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Ответ конкретного пользователя (студента) на групповое задание дорожной карты
public class GroupTaskSubmissionEntity
{
    public int Id { get; set; }
    public int GroupTaskId { get; set; } // Связь с групповым заданием
    public int StudentUserId { get; set; } // Кто отправил
    public string StudentResponse { get; set; } = ""; // Текст ответа или ссылка на репозиторий
    public string Status { get; set; } = "Submitted"; // Submitted, Approved, Rejected
    public string? MentorComment { get; set; } // Комментарий преподавателя
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RoadmapEntity> Roadmaps => Set<RoadmapEntity>();
    public DbSet<RoadmapNodeEntity> Nodes => Set<RoadmapNodeEntity>();
    public DbSet<RoadmapEdgeEntity> Edges => Set<RoadmapEdgeEntity>();
    public DbSet<RoadmapCommentEntity> Comments => Set<RoadmapCommentEntity>();
    public DbSet<RoadmapInviteEntity> Invites => Set<RoadmapInviteEntity>();
    public DbSet<UserNotificationEntity> Notifications => Set<UserNotificationEntity>();
    public DbSet<NodeMaterialEntity> Materials => Set<NodeMaterialEntity>();
    public DbSet<PasswordResetTokenEntity> PasswordResetTokens => Set<PasswordResetTokenEntity>();
    
    // Таблицы для системы тестирования
    public DbSet<RoadmapTestEntity> Tests => Set<RoadmapTestEntity>();
    public DbSet<TestQuestionEntity> TestQuestions => Set<TestQuestionEntity>();
    public DbSet<QuestionOptionEntity> QuestionOptions => Set<QuestionOptionEntity>();
    public DbSet<UserTestAttemptEntity> TestAttempts => Set<UserTestAttemptEntity>();
    
    // Таблица связи Ментора и Студента
    public DbSet<MentorStudentEntity> MentorStudents => Set<MentorStudentEntity>();

    // Таблица для хранения персональных заданий от преподавателя
    public DbSet<PersonalTaskEntity> PersonalTasks => Set<PersonalTaskEntity>();

    // Новые таблицы для работы с групповыми задачами и ответами на них
    public DbSet<GroupTaskEntity> GroupTasks => Set<GroupTaskEntity>();
    public DbSet<GroupTaskSubmissionEntity> GroupTaskSubmissions => Set<GroupTaskSubmissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ЯВНАЯ ПРИВЯЗКА ИМЕН ТАБЛИЦ С СОХРАНЕНИЕМ РЕГИСТРА ДЛЯ POSTGRESQL
        modelBuilder.Entity<AppUser>().ToTable("Users");
        modelBuilder.Entity<RoadmapEntity>().ToTable("Roadmaps");
        modelBuilder.Entity<RoadmapNodeEntity>().ToTable("Nodes");
        modelBuilder.Entity<RoadmapEdgeEntity>().ToTable("Edges");
        modelBuilder.Entity<RoadmapCommentEntity>().ToTable("Comments");
        modelBuilder.Entity<RoadmapInviteEntity>().ToTable("Invites");
        modelBuilder.Entity<UserNotificationEntity>().ToTable("Notifications");
        modelBuilder.Entity<NodeMaterialEntity>().ToTable("Materials");
        modelBuilder.Entity<PasswordResetTokenEntity>().ToTable("PasswordResetTokens");
        modelBuilder.Entity<MentorStudentEntity>().ToTable("MentorStudents");
        modelBuilder.Entity<RoadmapTestEntity>().ToTable("Tests");
        modelBuilder.Entity<TestQuestionEntity>().ToTable("TestQuestions");
        modelBuilder.Entity<QuestionOptionEntity>().ToTable("QuestionOptions");
        modelBuilder.Entity<UserTestAttemptEntity>().ToTable("TestAttempts");
        modelBuilder.Entity<PersonalTaskEntity>().ToTable("PersonalTasks");
        modelBuilder.Entity<GroupTaskEntity>().ToTable("GroupTasks");
        modelBuilder.Entity<GroupTaskSubmissionEntity>().ToTable("GroupTaskSubmissions");

        // Уникальный индекс для Email пользователя
        modelBuilder.Entity<AppUser>()
            .HasIndex(x => x.Email)
            .IsUnique();

        // Конфигурация узлов (RoadmapNodeEntity)
        modelBuilder.Entity<RoadmapNodeEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            
            // ИСПРАВЛЕНО ДЛЯ POSTGRESQL: "REAL" из SQLite заменен на "double precision" (или дефолтный тип double)
            entity.Property(x => x.PositionX).HasColumnType("double precision");
            entity.Property(x => x.PositionY).HasColumnType("double precision");

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<RoadmapEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoadmapId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Конфигурация связей (RoadmapEdgeEntity)
        modelBuilder.Entity<RoadmapEdgeEntity>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<RoadmapEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoadmapId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<RoadmapNodeEntity>()
                .WithMany()
                .HasForeignKey(x => x.FromNodeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<RoadmapNodeEntity>()
                .WithMany()
                .HasForeignKey(x => x.ToNodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoadmapEntity>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoadmapCommentEntity>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoadmapCommentEntity>()
            .HasOne<RoadmapEntity>()
            .WithMany()
            .HasForeignKey(x => x.RoadmapId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoadmapInviteEntity>()
            .HasOne<RoadmapEntity>()
            .WithMany()
            .HasForeignKey(x => x.RoadmapId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserNotificationEntity>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NodeMaterialEntity>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NodeMaterialEntity>()
            .HasOne<RoadmapEntity>()
            .WithMany()
            .HasForeignKey(x => x.RoadmapId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PasswordResetTokenEntity>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoadmapTestEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<RoadmapEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoadmapId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestQuestionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<RoadmapTestEntity>()
                .WithMany(x => x.Questions)
                .HasForeignKey(x => x.TestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuestionOptionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<TestQuestionEntity>()
                .WithMany(x => x.Options)
                .HasForeignKey(x => x.TestQuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserTestAttemptEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Конфигурация таблицы менторства
        modelBuilder.Entity<MentorStudentEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.MentorUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Конфигурация каскадного удаления для персональных заданий
        modelBuilder.Entity<PersonalTaskEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<RoadmapEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoadmapId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Настройка связей для групповых задач
        modelBuilder.Entity<GroupTaskEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<RoadmapEntity>()
                .WithMany()
                .HasForeignKey(x => x.RoadmapId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Настройка связей для ответов на групповые задачи
        modelBuilder.Entity<GroupTaskSubmissionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasOne<GroupTaskEntity>()
                .WithMany()
                .HasForeignKey(x => x.GroupTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}