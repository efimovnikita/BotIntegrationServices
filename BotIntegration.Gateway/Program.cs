using System.Collections;
using BotIntegration.Shared;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Serilog;

const string ocelotJson = "ocelot.json";

try
{
    var builder = WebApplication.CreateBuilder(args);
    
    builder.Configuration.AddEnvironmentVariables();

    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.Seq(builder.Configuration["Urls:LogServer"] ?? string.Empty)
        .CreateLogger();
    
    builder.Host.UseSerilog((_, configuration) =>
    {
        configuration.WriteTo.Console();
        configuration.WriteTo.Seq(builder.Configuration["Urls:LogServer"] ?? string.Empty);
    });

    ReplaceOcelotConfigPlaceholders();

    builder.Configuration.AddJsonFile(ocelotJson, optional: false, reloadOnChange: true);
    builder.Services.AddOcelot(builder.Configuration);

    builder.Services.AddJwtAuthentication(builder.Configuration);

    builder.Services.AddControllers();

    var app = builder.Build();

    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    await app.UseOcelot();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

return;

void ReplaceOcelotConfigPlaceholders()
{
    var ocelotFilePath = Path.Combine(Directory.GetCurrentDirectory(), ocelotJson);

    if (!File.Exists(ocelotFilePath))
    {
        return;
    }

    var json = File.ReadAllText(ocelotFilePath);
    var environmentVariables = Environment.GetEnvironmentVariables();

    foreach (DictionaryEntry env in environmentVariables)
    {
        var placeholder = $"${{{env.Key}}}";
        if (env.Value != null)
        {
            json = json.Replace(placeholder, env.Value.ToString());
        }
    }

    File.WriteAllText(ocelotFilePath, json);
}