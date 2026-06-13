using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlantMonitor.Backend;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? throw new InvalidOperationException("Connection string 'Db' is not configured.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
// EF (writes + migrations) shares the same data source the read API queries.
builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddHostedService<IngestWorker>();
builder.Services.AddCors();

var app = builder.Build();

// In production Kestrel serves the frontend same-origin, so CORS is dev-only
// (Angular dev server runs on its own port).
if (app.Environment.IsDevelopment())
    app.UseCors(p => p.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod());

app.MapApi();

// Serve the Angular bundle from wwwroot when it was built into the image.
// index.html and ngsw.json must never be cached or service-worker updates
// stall; all other Angular assets are content-hashed and safe to cache.
if (app.Environment.WebRootPath is { } webRoot && File.Exists(Path.Combine(webRoot, "index.html")))
{
    var staticFiles = new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            if (ctx.File.Name is "index.html" or "ngsw.json")
                ctx.Context.Response.Headers.CacheControl = "no-store";
        }
    };
    app.UseDefaultFiles();
    app.UseStaticFiles(staticFiles);
    app.MapFallbackToFile("index.html", staticFiles);
}

app.Run();
