using RideHailingApp.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
namespace RideHailingApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<GeoLocatorService>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<ApiService>();
        var app = builder.Build();
        Services = app.Services;
        return app;
    }

    public static IServiceProvider Services { get; private set; } = null!;
}