using SeoDomainFinder.Api;
using SeoDomainFinder.Infrastructure;
using SeoDomainFinder.Infrastructure.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSeoDomainFinderInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

var corsOrigins = builder.Configuration.GetSection(CorsOptions.SectionName)["AllowedOrigins"]
    ?? "http://localhost:3000";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseExceptionHandler();

app.MapDomainEndpoints();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();
