using Microsoft.EntityFrameworkCore;
using Pulse.Web.Components;
using Pulse.Web.Data;
using Pulse.Web.Identity;
using Pulse.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// data directory for the SQLite database
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "pulse.db");
builder.Services.AddDbContextFactory<PulseDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<EventStore>();

builder.Services
    .AddAuthentication(ParticipantIdentityMiddleware.Scheme)
    .AddCookie(ParticipantIdentityMiddleware.Scheme, options =>
    {
        options.Cookie.Name = "Pulse.Participant";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;

        options.ExpireTimeSpan = TimeSpan.FromDays(180);
        options.SlidingExpiration = false;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<QuestionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<ParticipantIdentityMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
