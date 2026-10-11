using Microsoft.EntityFrameworkCore;
using Pulse.Web.Components;
using Pulse.Web.Data;
using Pulse.Web.Identity;
using Pulse.Web.Services;
using Pulse.Web.Domain;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var demoRoles = new DemoRoleSwitching(builder.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("Demo:EnableRoleSwitching"));
builder.Services.AddSingleton(demoRoles);

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
builder.Services.AddScoped<EventChainVerifier>();

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
        options.Events.OnValidatePrincipal = context =>
        {
            // A development demo cookie must not grant privileges after demo mode is disabled.
            if (!demoRoles.Enabled && context.Principal?.HasClaim(DemoRoleSwitching.DemoClaim, "true") == true)
            {
                var actorId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, actorId ?? string.Empty),
                    new Claim(ClaimTypes.Role, nameof(PulseRole.Participant))
                }, ParticipantIdentityMiddleware.Scheme)));
                context.ShouldRenew = true;
            }
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// services
builder.Services.AddScoped<QuestionService>();
builder.Services.AddScoped<VoteService>();
builder.Services.AddScoped<ResultService>();
builder.Services.AddScoped<EvidenceService>();
builder.Services.AddScoped<TimelineService>();

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

if (demoRoles.Enabled)
    app.MapPost("/demo/role", DemoRoleSwitching.SwitchAsync);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
