using CarnotCycleCircus.Core.Extensions;
using CarnotCycleCircus.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add Razor components with Interactive Server mode
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Carnot Cycle Circus Core Engine & Domain Services
builder.Services.AddCarnotCycleCircusCore();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
