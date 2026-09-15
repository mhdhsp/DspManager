using HotelConfigAnalyser.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialise C# enums as their string names (e.g. "Warning", "Error")
        // so the frontend receives a string instead of an integer.
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Register the pipeline services — each has a single, well-defined responsibility
builder.Services.AddScoped<JsonConfigurationParser>();
builder.Services.AddScoped<ConfigurationMapper>();
builder.Services.AddScoped<ConfigurationValidator>();
builder.Services.AddScoped<ConfigurationAnalyzer>();
builder.Services.AddScoped<SqlGenerator>();

// Swagger/OpenAPI — useful for manual testing and future integration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title   = "Hotel Config Analyser API",
        Version = "v1",
        Description =
            "Phase 1: JSON configuration analyser and MySQL INSERT script generator. " +
            "No database connection or SQL execution in this phase.",
    });
});

// CORS — allow the Vite dev server (port 5173) and any localhost port in development
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ── App pipeline ──────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Hotel Config Analyser API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("FrontendDev");

app.UseHttpsRedirection();

app.MapControllers();

// Health check — useful for container/proxy liveness probes
app.MapGet("/health", () => Results.Ok(new { status = "healthy", phase = "1", timestamp = DateTime.UtcNow }));

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
