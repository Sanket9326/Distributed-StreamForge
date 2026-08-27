using StreamForge.Upload.Api.Health;
using StreamForge.Upload.Api.Middleware;
using StreamForge.Upload.Api.Options;
using StreamForge.Upload.Api.Services;

const long maximumRequestBodyBytes = 1_074_790_400;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maximumRequestBodyBytes);

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services
    .AddOptions<UploadStorageOptions>()
    .BindConfiguration(UploadStorageOptions.SectionName)
    .Validate(options => options.MaxFileSizeBytes > 0, "MaxFileSizeBytes must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton<VideoFileValidator>();
builder.Services.AddSingleton<UploadStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddHealthChecks()
    .AddCheck<UploadStorageHealthCheck>("upload-storage");

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
