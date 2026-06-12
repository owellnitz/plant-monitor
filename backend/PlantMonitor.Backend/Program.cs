using Npgsql;
using PlantMonitor.Backend;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? throw new InvalidOperationException("Connection string 'Db' is not configured.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddHostedService<IngestWorker>();
builder.Services.AddCors();

var app = builder.Build();

// In production nginx proxies /api same-origin, so CORS is dev-only.
if (app.Environment.IsDevelopment())
    app.UseCors(p => p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod());

app.MapApi();

app.Run();
