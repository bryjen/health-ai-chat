using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Pgvector.EntityFrameworkCore;
using WebApi.Configuration;
using WebApi.Data;
using WebApi.Models;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
builder.Environment.EnvironmentName = "Development";

// Get connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Error: Connection string 'DefaultConnection' not found in configuration.");
    Environment.Exit(1);
}

// Create DbContext directly with connection string to avoid service provider lifecycle issues
// Add connection string parameters to prevent premature connection closure
var connectionStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
{
    // Ensure connection stays open long enough
    CommandTimeout = 60,
    // Disable connection pooling for seeding to avoid disposal issues
    Pooling = false
};

var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionStringBuilder.ConnectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseVector(); // Required for Vector type mapping
    });
    // Disable retry for seeding to avoid connection disposal issues

using var dbContext = new AppDbContext(optionsBuilder.Options);

// Check if we should just drop tables (for reset script)
if (args.Length > 0 && args[0] == "--drop-tables")
{
    try
    {
        Console.WriteLine("Dropping all tables in conuhacks schema...");
        var dropTablesSql = @"
DO $$ 
DECLARE
    r RECORD;
BEGIN
    FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'conuhacks') 
    LOOP
        EXECUTE 'DROP TABLE IF EXISTS conuhacks.' || quote_ident(r.tablename) || ' CASCADE';
    END LOOP;
END;
$$;
DROP TABLE IF EXISTS ""__EFMigrationsHistory"" CASCADE;";
        
        await dbContext.Database.ExecuteSqlRawAsync(dropTablesSql);
        Console.WriteLine("Successfully dropped all tables.");
        await dbContext.DisposeAsync();
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        await dbContext.DisposeAsync();
        return 1;
    }
}

Console.WriteLine("=== Database Seeding ===\n");

#region Sample Users

var user1 = new User
{
    Id = Guid.NewGuid(),
    Email = "alice@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-30),
    UpdatedAt = DateTime.UtcNow.AddDays(-30)
};

var user2 = new User
{
    Id = Guid.NewGuid(),
    Email = "bob@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-20),
    UpdatedAt = DateTime.UtcNow.AddDays(-20)
};

var user3 = new User
{
    Id = Guid.NewGuid(),
    Email = "charlie@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-10),
    UpdatedAt = DateTime.UtcNow.AddDays(-10)
};

var sampleUser = new User
{
    Id = Guid.NewGuid(),
    Email = "miguelbryancarlo3434@gmail.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Kuyapogi3434!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-5),
    UpdatedAt = DateTime.UtcNow.AddDays(-5)
};

#endregion

#region Sample Chat Data for miguelbryancarlo3434@gmail.com

var baseTime = DateTime.UtcNow.AddDays(-3);

// Create conversation
var conversation = new Conversation
{
    Id = Guid.NewGuid(),
    UserId = sampleUser.Id,
    Title = "Health Consultation - Fever and Cough",
    CreatedAt = baseTime,
    UpdatedAt = baseTime.AddHours(1)
};

// Create symptoms
var feverSymptom = new Symptom
{
    Id = 0, // Will be set by DB
    UserId = sampleUser.Id,
    Name = "Fever",
    Description = "Elevated body temperature",
    CreatedAt = baseTime,
    UpdatedAt = baseTime
};

var coughSymptom = new Symptom
{
    Id = 0,
    UserId = sampleUser.Id,
    Name = "Cough",
    Description = "Dry cough",
    CreatedAt = baseTime,
    UpdatedAt = baseTime
};

var headacheSymptom = new Symptom
{
    Id = 0,
    UserId = sampleUser.Id,
    Name = "Headache",
    Description = "Head pain",
    CreatedAt = baseTime.AddHours(0.5),
    UpdatedAt = baseTime.AddHours(0.5)
};

// Create episodes
var feverEpisode = new Episode
{
    Id = 0,
    SymptomId = 0, // Will be set after symptom is saved
    UserId = sampleUser.Id,
    Stage = "characterized",
    Status = "active",
    StartedAt = baseTime.AddDays(-3),
    Severity = 7,
    Frequency = "constant",
    CreatedAt = baseTime,
    UpdatedAt = baseTime
};

var coughEpisode = new Episode
{
    Id = 0,
    SymptomId = 0,
    UserId = sampleUser.Id,
    Stage = "characterized",
    Status = "active",
    StartedAt = baseTime.AddDays(-3),
    Severity = 5,
    Frequency = "intermittent",
    CreatedAt = baseTime,
    UpdatedAt = baseTime
};

var headacheEpisode = new Episode
{
    Id = 0,
    SymptomId = 0,
    UserId = sampleUser.Id,
    Stage = "characterized",
    Status = "active",
    StartedAt = baseTime.AddDays(-1),
    Severity = 6,
    Frequency = "intermittent",
    CreatedAt = baseTime.AddHours(0.5),
    UpdatedAt = baseTime.AddHours(0.5)
};

// Create messages with proper ordering
var messages = new List<Message>();

// Message 1: User - "I've been experiencing fever and cough for the past few days"
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "user",
    Content = "I've been experiencing fever and cough for the past few days",
    CreatedAt = baseTime
});

// Message 2: Assistant - SymptomAddedStatus for Fever
var feverStatusJson = JsonSerializer.Serialize(new[]
{
    new
    {
        type = "symptom-added",
        symptomName = "Fever",
        episodeId = 0, // Will be updated after episode is saved
        location = (string?)null,
        timestamp = baseTime.AddSeconds(1)
    }
});
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "I've noted that you're experiencing a fever. Can you tell me more about it?",
    StatusInformationJson = feverStatusJson,
    CreatedAt = baseTime.AddSeconds(1)
});

// Message 3: Assistant - SymptomAddedStatus for Cough
var coughStatusJson = JsonSerializer.Serialize(new[]
{
    new
    {
        type = "symptom-added",
        symptomName = "Cough",
        episodeId = 0, // Will be updated after episode is saved
        location = (string?)null,
        timestamp = baseTime.AddSeconds(2)
    }
});
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "I've also recorded your cough. Let me gather more details.",
    StatusInformationJson = coughStatusJson,
    CreatedAt = baseTime.AddSeconds(2)
});

// Message 4: User - "The fever is around 102°F and the cough is dry"
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "user",
    Content = "The fever is around 102°F and the cough is dry",
    CreatedAt = baseTime.AddMinutes(1)
});

// Message 5: Assistant - Follow-up questions
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "Thank you for that information. A fever of 102°F is significant. Are you experiencing any other symptoms?",
    CreatedAt = baseTime.AddMinutes(1).AddSeconds(1)
});

// Message 6: User - "I also have a headache"
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "user",
    Content = "I also have a headache",
    CreatedAt = baseTime.AddMinutes(2)
});

// Message 7: Assistant - SymptomAddedStatus for Headache
var headacheStatusJson = JsonSerializer.Serialize(new[]
{
    new
    {
        type = "symptom-added",
        symptomName = "Headache",
        episodeId = 0, // Will be updated after episode is saved
        location = (string?)null,
        timestamp = baseTime.AddMinutes(2).AddSeconds(1)
    }
});
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "I've added headache to your symptoms. Let me analyze your condition.",
    StatusInformationJson = headacheStatusJson,
    CreatedAt = baseTime.AddMinutes(2).AddSeconds(1)
});

// Message 8: Assistant - AssessmentGeneratingStatus
var generatingStatusJson = JsonSerializer.Serialize(new[]
{
    new
    {
        type = "assessment-generating",
        message = "Generating assessment...",
        timestamp = baseTime.AddMinutes(2).AddSeconds(2)
    }
});
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "",
    StatusInformationJson = generatingStatusJson,
    CreatedAt = baseTime.AddMinutes(2).AddSeconds(2)
});

// Message 9: Assistant - AssessmentAnalyzingStatus
var analyzingStatusJson = JsonSerializer.Serialize(new[]
{
    new
    {
        type = "assessment-analyzing",
        message = "Analyzing assessment...",
        timestamp = baseTime.AddMinutes(2).AddSeconds(3)
    }
});
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "",
    StatusInformationJson = analyzingStatusJson,
    CreatedAt = baseTime.AddMinutes(2).AddSeconds(3)
});

// Message 10: Assistant - AssessmentCreatedStatus (will be updated after assessment is created)
var assessmentCreatedStatusJson = JsonSerializer.Serialize(new[]
{
    new
    {
        type = "assessment-created",
        assessmentId = 0, // Will be updated after assessment is saved
        hypothesis = "Influenza-like illness",
        confidence = 0.75m,
        timestamp = baseTime.AddMinutes(2).AddSeconds(4)
    }
});
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "",
    StatusInformationJson = assessmentCreatedStatusJson,
    CreatedAt = baseTime.AddMinutes(2).AddSeconds(4)
});

// Message 11: Assistant - Assessment summary
messages.Add(new Message
{
    Id = Guid.NewGuid(),
    ConversationId = conversation.Id,
    Role = "assistant",
    Content = "Based on your symptoms (fever, dry cough, and headache), I've assessed your condition as an influenza-like illness with 75% confidence. I recommend seeing your GP for further evaluation.",
    CreatedAt = baseTime.AddMinutes(2).AddSeconds(5)
});

// Create assessment (will be linked to episodes after they're saved)
var assessment = new Assessment
{
    Id = 0, // Will be set by DB
    UserId = sampleUser.Id,
    ConversationId = conversation.Id,
    Hypothesis = "Influenza-like illness",
    Confidence = 0.75m,
    Differentials = new List<string> { "Common cold", "COVID-19", "Other viral infection" },
    Reasoning = "Fever, dry cough, and headache suggest viral infection",
    RecommendedAction = "see-gp",
    CreatedAt = baseTime.AddMinutes(2).AddSeconds(4)
};

#endregion

// Use simple transaction without retry strategy
using var transaction = await dbContext.Database.BeginTransactionAsync();
try
{
    Console.WriteLine("[1/5] Adding and saving users...");
    dbContext.Users.AddRange(user1, user2, user3, sampleUser);
    await dbContext.SaveChangesAsync();

    Console.WriteLine("[2/5] Creating conversation and symptoms...");
    dbContext.Conversations.Add(conversation);
    dbContext.Symptoms.AddRange(feverSymptom, coughSymptom, headacheSymptom);
    await dbContext.SaveChangesAsync();

    Console.WriteLine("[3/5] Creating episodes...");
    // Update episode symptom IDs
    feverEpisode.SymptomId = feverSymptom.Id;
    coughEpisode.SymptomId = coughSymptom.Id;
    headacheEpisode.SymptomId = headacheSymptom.Id;
    dbContext.Episodes.AddRange(feverEpisode, coughEpisode, headacheEpisode);
    await dbContext.SaveChangesAsync();

    Console.WriteLine("[4/5] Creating messages and assessment...");
    // Update status JSON with actual episode IDs
    var updatedFeverStatus = new[]
    {
        new
        {
            type = "symptom-added",
            symptomName = "Fever",
            episodeId = feverEpisode.Id,
            location = (string?)null,
            timestamp = baseTime.AddSeconds(1)
        }
    };
    messages[1].StatusInformationJson = JsonSerializer.Serialize(updatedFeverStatus);

    var updatedCoughStatus = new[]
    {
        new
        {
            type = "symptom-added",
            symptomName = "Cough",
            episodeId = coughEpisode.Id,
            location = (string?)null,
            timestamp = baseTime.AddSeconds(2)
        }
    };
    messages[2].StatusInformationJson = JsonSerializer.Serialize(updatedCoughStatus);

    var updatedHeadacheStatus = new[]
    {
        new
        {
            type = "symptom-added",
            symptomName = "Headache",
            episodeId = headacheEpisode.Id,
            location = (string?)null,
            timestamp = baseTime.AddMinutes(2).AddSeconds(1)
        }
    };
    messages[6].StatusInformationJson = JsonSerializer.Serialize(updatedHeadacheStatus);

    dbContext.Messages.AddRange(messages);
    dbContext.Assessments.Add(assessment);
    await dbContext.SaveChangesAsync();

    Console.WriteLine("[5/5] Creating assessment episode links...");
    // Create AssessmentEpisodeLink records
    var assessmentLinks = new List<AssessmentEpisodeLink>
    {
        new AssessmentEpisodeLink
        {
            AssessmentId = assessment.Id,
            EpisodeId = feverEpisode.Id,
            Weight = 0.85m,
            Reasoning = "High fever is primary indicator"
        },
        new AssessmentEpisodeLink
        {
            AssessmentId = assessment.Id,
            EpisodeId = coughEpisode.Id,
            Weight = 0.70m,
            Reasoning = "Dry cough consistent with viral infection"
        },
        new AssessmentEpisodeLink
        {
            AssessmentId = assessment.Id,
            EpisodeId = headacheEpisode.Id,
            Weight = 0.60m,
            Reasoning = "Common accompanying symptom"
        }
    };
    dbContext.AssessmentEpisodeLinks.AddRange(assessmentLinks);

    // Update assessment created status with actual assessment ID
    var updatedAssessmentStatus = new[]
    {
        new
        {
            type = "assessment-created",
            assessmentId = assessment.Id,
            hypothesis = "Influenza-like illness",
            confidence = 0.75m,
            timestamp = baseTime.AddMinutes(2).AddSeconds(4)
        }
    };
    messages[9].StatusInformationJson = JsonSerializer.Serialize(updatedAssessmentStatus);
    await dbContext.SaveChangesAsync();

    await transaction.CommitAsync();
    Console.WriteLine("All data saved successfully");
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    Console.WriteLine($"Error seeding database: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    throw;
}

Console.WriteLine("Verifying data...");
var userCount = await dbContext.Users.CountAsync();
var conversationCount = await dbContext.Conversations.CountAsync();
var messageCount = await dbContext.Messages.CountAsync();
var symptomCount = await dbContext.Symptoms.CountAsync();
var episodeCount = await dbContext.Episodes.CountAsync();
var assessmentCount = await dbContext.Assessments.CountAsync();
var linkCount = await dbContext.AssessmentEpisodeLinks.CountAsync();

Console.WriteLine($"\n✓ Successfully seeded database!");
Console.WriteLine($"  - Users: {userCount}");
Console.WriteLine($"  - Conversations: {conversationCount}");
Console.WriteLine($"  - Messages: {messageCount}");
Console.WriteLine($"  - Symptoms: {symptomCount}");
Console.WriteLine($"  - Episodes: {episodeCount}");
Console.WriteLine($"  - Assessments: {assessmentCount}");
Console.WriteLine($"  - Assessment Episode Links: {linkCount}");

Console.WriteLine($"\nSample Login Credentials:");
Console.WriteLine($"  alice@example.com   | Password123!");
Console.WriteLine($"  bob@example.com     | Password123!");
Console.WriteLine($"  charlie@example.com | Password123!");
Console.WriteLine($"  miguelbryancarlo3434@gmail.com | Kuyapogi3434!");

Console.WriteLine("\nHello, World!");

// Explicitly dispose the context
await dbContext.DisposeAsync();
return 0;
