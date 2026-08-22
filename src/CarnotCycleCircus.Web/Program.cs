var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Carnot Cycle Circus - Autonomous Engineering Agent Platform");

app.Run();
