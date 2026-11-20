using Microsoft.EntityFrameworkCore;
namespace ApiSentinel.Api;
public sealed class SentinelDb(DbContextOptions<SentinelDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RoleDefinition> Roles => Set<RoleDefinition>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ApiDefinition> Apis => Set<ApiDefinition>();
    public DbSet<ApiRequestHeader> ApiRequestHeaders => Set<ApiRequestHeader>();
    public DbSet<ApiQueryParameter> ApiQueryParameters => Set<ApiQueryParameter>();
    public DbSet<ApiCollection> Collections => Set<ApiCollection>();
    public DbSet<ApiEnvironment> Environments => Set<ApiEnvironment>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestAssertion> Assertions => Set<TestAssertion>();
    public DbSet<TestRun> Runs => Set<TestRun>();
    public DbSet<TestRunAssertion> RunAssertions => Set<TestRunAssertion>();
    public DbSet<TestRunResponseHeader> RunResponseHeaders => Set<TestRunResponseHeader>();
    public DbSet<MonitorConfiguration> Monitors => Set<MonitorConfiguration>();
    public DbSet<MonitorHistory> MonitorHistory => Set<MonitorHistory>();
    public DbSet<Alert> Alerts => Set<Alert>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<RoleDefinition>().HasIndex(x => x.Name).IsUnique();
        b.Entity<RoleDefinition>().HasData(new RoleDefinition { Id = 1, Name = "User" }, new RoleDefinition { Id = 2, Name = "Admin" });
        b.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        b.Entity<UserRole>().HasOne(x => x.User).WithMany(x => x.Roles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserRole>().HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserRole>().HasIndex(x => x.RoleId);
        b.Entity<RefreshToken>().HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RefreshToken>().Property(x => x.TokenHash).HasMaxLength(64);
        b.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<RefreshToken>().HasIndex(x => new { x.UserId, x.ExpiresAt });
        b.Entity<RefreshToken>().HasIndex(x => new { x.UserId, x.RevokedAt });
        b.Entity<RefreshToken>().Property(x => x.RowVersion).IsRowVersion();
        b.Entity<ApiDefinition>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApiDefinition>().Property(x => x.Url).HasMaxLength(2048);
        b.Entity<ApiDefinition>().Property(x => x.TimeoutSeconds).HasDefaultValue(20);
        b.Entity<ApiCollection>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApiCollection>().HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        b.Entity<ApiEnvironment>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApiEnvironment>().HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        b.Entity<ApiEnvironment>().HasIndex(x => x.UserId).IsUnique().HasFilter("[IsActive] = 1");
        b.Entity<EnvironmentVariable>().HasOne(x => x.Environment).WithMany(x => x.Variables).HasForeignKey(x => x.EnvironmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<EnvironmentVariable>().HasIndex(x => new { x.EnvironmentId, x.Key }).IsUnique();
        b.Entity<ApiDefinition>().HasOne(x => x.Collection).WithMany(x => x.Apis).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ApiDefinition>().HasIndex(x => new { x.UserId, x.CollectionId });
        b.Entity<ApiRequestHeader>().HasOne(x => x.Api).WithMany(x => x.Headers).HasForeignKey(x => x.ApiId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApiRequestHeader>().HasIndex(x => new { x.ApiId, x.Name }).IsUnique();
        b.Entity<ApiQueryParameter>().HasOne(x => x.Api).WithMany(x => x.QueryParameters).HasForeignKey(x => x.ApiId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ApiQueryParameter>().HasIndex(x => new { x.ApiId, x.SortOrder });
        b.Entity<TestCase>().HasOne(x => x.Api).WithMany().HasForeignKey(x => x.ApiId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestAssertion>().HasOne(x => x.TestCase).WithMany(x => x.Assertions).HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestRunAssertion>().HasOne(x => x.Run).WithMany(x => x.Assertions).HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestRunAssertion>().HasIndex(x => x.TestRunId);
        b.Entity<TestRunResponseHeader>().HasOne(x => x.Run).WithMany(x => x.ResponseHeaders).HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestRunResponseHeader>().HasIndex(x => new { x.TestRunId, x.Name }).IsUnique();
        b.Entity<TestRun>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<TestRun>().HasOne(x => x.TestCase).WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MonitorConfiguration>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MonitorConfiguration>().HasOne(x => x.TestCase).WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MonitorConfiguration>().HasIndex(x => x.TestCaseId);
        b.Entity<MonitorHistory>().HasOne(x => x.Monitor).WithMany().HasForeignKey(x => x.MonitorId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MonitorHistory>().HasIndex(x => new { x.MonitorId, x.CreatedAt });
        b.Entity<Alert>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Alert>().HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
public sealed class User { public int Id { get; set; } public string Email { get; set; } = ""; public string PasswordHash { get; set; } = ""; public int TokenVersion { get; set; } public List<RefreshToken> RefreshTokens { get; set; } = []; public List<UserRole> Roles { get; set; } = []; }
public sealed class RoleDefinition { public int Id { get; set; } public string Name { get; set; } = ""; }
public sealed class UserRole { public int UserId { get; set; } public User User { get; set; } = null!; public int RoleId { get; set; } public RoleDefinition Role { get; set; } = null!; }
public sealed class RefreshToken { public int Id { get; set; } public int UserId { get; set; } public User User { get; set; } = null!; public string TokenHash { get; set; } = ""; public int TokenVersion { get; set; } public DateTime CreatedAt { get; set; } public DateTime ExpiresAt { get; set; } public DateTime? RevokedAt { get; set; } public byte[] RowVersion { get; set; } = []; }
public sealed class ApiCollection { public int Id { get; set; } public int UserId { get; set; } public string Name { get; set; } = ""; public DateTime CreatedAt { get; set; } public List<ApiDefinition> Apis { get; set; } = []; }
public sealed class ApiEnvironment { public int Id { get; set; } public int UserId { get; set; } public string Name { get; set; } = ""; public bool IsActive { get; set; } public DateTime CreatedAt { get; set; } public List<EnvironmentVariable> Variables { get; set; } = []; }
public sealed class EnvironmentVariable { public int Id { get; set; } public int EnvironmentId { get; set; } public ApiEnvironment Environment { get; set; } = null!; public string Key { get; set; } = ""; public string? Value { get; set; } public string? EncryptedValue { get; set; } public bool IsSecret { get; set; } }
public sealed class ApiDefinition { public int Id { get; set; } public int UserId { get; set; } public int? CollectionId { get; set; } public ApiCollection? Collection { get; set; } public string Name { get; set; } = ""; public string Url { get; set; } = ""; public string Method { get; set; } = "GET"; public string? RequestBody { get; set; } public string ContentType { get; set; } = "application/json"; public int TimeoutSeconds { get; set; } = 20; public int SortOrder { get; set; } public string AuthenticationType { get; set; } = "None"; public string? EncryptedCredential { get; set; } public string? AuthHeaderName { get; set; } public string? ExtractionJsonPath { get; set; } public string? ExtractionVariableName { get; set; } public List<ApiRequestHeader> Headers { get; set; } = []; public List<ApiQueryParameter> QueryParameters { get; set; } = []; public DateTime CreatedAt { get; set; } }
public sealed class ApiRequestHeader { public int Id { get; set; } public int ApiId { get; set; } public ApiDefinition Api { get; set; } = null!; public string Name { get; set; } = ""; public string? Value { get; set; } public string? EncryptedValue { get; set; } public bool IsSecret { get; set; } }
public sealed class ApiQueryParameter { public int Id { get; set; } public int ApiId { get; set; } public ApiDefinition Api { get; set; } = null!; public string Name { get; set; } = ""; public string? Value { get; set; } public string? EncryptedValue { get; set; } public bool IsSecret { get; set; } public bool IsEnabled { get; set; } = true; public int SortOrder { get; set; } }
public sealed class TestCase { public int Id { get; set; } public int ApiId { get; set; } public ApiDefinition Api { get; set; } = null!; public string Name { get; set; } = ""; public int? ExpectedStatus { get; set; } public int? MaxDurationMs { get; set; } public string? SchemaJson { get; set; } public List<TestAssertion> Assertions { get; set; } = []; }
public sealed class TestAssertion { public int Id { get; set; } public int TestCaseId { get; set; } public TestCase TestCase { get; set; } = null!; public string JsonPath { get; set; } = ""; public string Operator { get; set; } = "Equals"; public string? ExpectedJson { get; set; } }
public sealed class TestRun { public int Id { get; set; } public int UserId { get; set; } public int? TestCaseId { get; set; } public TestCase? TestCase { get; set; } public string Url { get; set; } = ""; public string Method { get; set; } = "GET"; public int Status { get; set; } public long DurationMs { get; set; } public string Body { get; set; } = ""; public string Error { get; set; } = ""; public bool Passed { get; set; } public DateTime CreatedAt { get; set; } public List<TestRunAssertion> Assertions { get; set; } = []; public List<TestRunResponseHeader> ResponseHeaders { get; set; } = []; }
public sealed class TestRunAssertion { public int Id { get; set; } public int TestRunId { get; set; } public TestRun Run { get; set; } = null!; public string JsonPath { get; set; } = ""; public string Operator { get; set; } = ""; public string? Actual { get; set; } public string? Expected { get; set; } public bool Passed { get; set; } public string? Error { get; set; } }
public sealed class TestRunResponseHeader { public int Id { get; set; } public int TestRunId { get; set; } public TestRun Run { get; set; } = null!; public string Name { get; set; } = ""; public string Value { get; set; } = ""; }
public sealed class MonitorConfiguration { public int Id { get; set; } public int UserId { get; set; } public int TestCaseId { get; set; } public TestCase TestCase { get; set; } = null!; public int FrequencyMinutes { get; set; } public bool Enabled { get; set; } = true; public DateTime CreatedAt { get; set; } }
public sealed class MonitorHistory { public int Id { get; set; } public int? MonitorId { get; set; } public MonitorConfiguration? Monitor { get; set; } public string MonitorName { get; set; } = ""; public string Url { get; set; } = ""; public int Status { get; set; } public long DurationMs { get; set; } public bool Passed { get; set; } public string Error { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public sealed class Alert { public int Id { get; set; } public int UserId { get; set; } public string Title { get; set; } = ""; public string Message { get; set; } = ""; public bool IsRead { get; set; } public DateTime CreatedAt { get; set; } }
public record AuthInput(string Email, string Password);
public record BootstrapInput(string? Key);
public record PasswordChangeInput(string CurrentPassword, string NewPassword);
public record EmailChangeInput(string Email, string CurrentPassword);
public record ApiInput(string Name, string Url, string Method = "GET", int? CollectionId = null, int SortOrder = 0, string AuthenticationType = "None", string? Credential = null, string? AuthHeaderName = null, string? ExtractionJsonPath = null, string? ExtractionVariableName = null, string? RequestBody = null, string ContentType = "application/json", List<ApiHeaderInput>? Headers = null, int? TimeoutSeconds = null, List<ApiQueryParameterInput>? QueryParameters = null);
public record ApiHeaderInput(string Name, string Value, bool IsSecret = false);
public record ApiQueryParameterInput(string Name, string Value, bool IsSecret = false, bool IsEnabled = true);
public record ApiExtractionInput(string? JsonPath, string? VariableName);
public record OpenApiImportInput(string? Specification, int? CollectionId = null);
public record CollectionInput(string Name);
public record EnvironmentInput(string Name, bool IsActive = false);
public record EnvironmentVariableInput(string Key, string Value, bool IsSecret = false);
public record CollectionRunResult(int Id, string Name, int Status, long DurationMs, bool Passed, string? Error);
public record MonitorInput(int TestCaseId, int FrequencyMinutes, bool Enabled = true);
public record AlertReadInput(bool IsRead = true);
public record UserRoleInput(string Role);
public record MonitorHistoryDto(string MonitorName, string Url, int Status, long DurationMs, bool Passed, string Error, DateTime CreatedAt);
public record ExecuteInput(string Url, string Method = "GET", string? Body = null, string? ContentType = null, Dictionary<string, string>? Headers = null, int? ExpectedStatus = null, int? TestCaseId = null, string AuthenticationType = "None", string? Credential = null, string? AuthHeaderName = null, int? TimeoutSeconds = null, List<ApiQueryParameterInput>? QueryParameters = null);
public record AssertionInput(string JsonPath, string Operator, System.Text.Json.JsonElement? Expected = null);
public record TestCaseInput(int ApiId, string Name, int? ExpectedStatus = null, int? MaxDurationMs = null, List<AssertionInput>? Assertions = null, string? SchemaJson = null);
public record AiInput(string Description);