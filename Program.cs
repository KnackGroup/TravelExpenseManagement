using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TravelExpense.Api.Auth;
using TravelExpense.Api.Data;
using TravelExpense.Api.Services;
using TravelExpense.Api.Services.Ai;
using TravelExpense.Api.Services.Notifications;
using TravelExpense.Api.Services.Security;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

var jwtOptions = new JwtOptions
{
    SigningKey = builder.Configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey is not configured."),
    Issuer = builder.Configuration["Jwt:Issuer"] ?? "TravelExpenseApi",
    Audience = builder.Configuration["Jwt:Audience"] ?? "TravelExpenseClient",
    ExpiryMinutes = int.TryParse(builder.Configuration["Jwt:ExpiryMinutes"], out var m) ? m : 480
};

// ---------- Services ----------
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<PolicyEngine>();
builder.Services.AddScoped<ApprovalRoutingService>();

// Secrets stored in EmailSettings/AiSettings (SMTP password, AI API key) are encrypted at
// rest with Data Protection. Keys persist to /app/keys (mount a volume in production so a
// container restart doesn't lose the ability to decrypt existing secrets - see docker-compose.yml).
builder.Services.AddDataProtection()
    .SetApplicationName("TravelExpense")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyPath"] ?? "./keys"));
builder.Services.AddScoped<ISecretProtector, SecretProtector>();

// Notifications: dynamic SMTP config + templates + outbox queue + background dispatcher.
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddHostedService<EmailDispatcherHostedService>();
builder.Services.AddHostedService<DailyAutomationHostedService>();

// AI: a no-key "AI-lite" insight engine that always works (computed from this database), plus
// a pluggable bring-your-own-key provider for LLM-backed features (receipt OCR, natural-
// language tour plan entry, AI trip summaries) - see Services/Ai for details.
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiInsightService>();
builder.Services.AddScoped<IAiProviderFactory, AiProviderFactory>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Travel & Expense API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ---------- Middleware ----------
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
