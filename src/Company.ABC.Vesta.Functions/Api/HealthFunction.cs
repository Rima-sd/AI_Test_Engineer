// Purpose: Exposes an anonymous health endpoint for the Vesta Functions host.
// Returns the application name and version without accessing external dependencies.

using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Company.ABC.Vesta.Functions.Api;

public sealed class HealthFunction
{
    private const string ApplicationName = "Vesta";
    private const string DefaultVersion = "0.1.0";
    private HealthFunction()
    {
    }

    [Function("Health")]
    public static IActionResult Run(
        [HttpTrigger(
            Microsoft.Azure.Functions.Worker.AuthorizationLevel.Anonymous,
            "get",
            Route = "health")]
        HttpRequest request)
    {
        var version = GetApplicationVersion();

        return new OkObjectResult(new
        {
            name = ApplicationName,
            version
        });
    }

    private static string GetApplicationVersion()
    {
        return typeof(HealthFunction)
            .Assembly
            .GetName()
            .Version?
            .ToString(3) ?? DefaultVersion;
    }
}