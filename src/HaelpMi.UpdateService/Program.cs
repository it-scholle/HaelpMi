using HaelpMi.Core.Diagnostics;
using HaelpMi.UpdateService;
using Microsoft.Extensions.Hosting;

// Läuft als SYSTEM/LocalService - ein unbehandelter Fehler hier wuerde die gesamte
// Update-Pipeline für alle Geräte lahmlegen, ohne dass irgendwo eine Spur bleibt (Event
// Log wird nur von den ILogger-Aufrufen in UpdateServiceWorker gefüllt, nicht von einer
// Ausnahme ausserhalb davon). Gleiches Muster wie Agent/Config (CrashLogger).
CrashLogger.InstallProcessWideHooks(nameof(HaelpMi.UpdateService));

var builder = Host.CreateApplicationBuilder(args);
// Name muss zum [Run]-Eintrag im Installer passen ("sc create HaelpMiUpdateService...").
builder.Services.AddWindowsService(options => options.ServiceName = "HaelpMiUpdateService");
builder.Services.AddHostedService<UpdateServiceWorker>();

var host = builder.Build();
host.Run();
