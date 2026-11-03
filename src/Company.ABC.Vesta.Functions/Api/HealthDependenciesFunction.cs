// Purpose: Provides an authenticated dependency/configuration readiness endpoint without returning secrets.
// Creates a JSON response containing the current Vesta configuration summary.

using Company.ABC.Vesta.Functions.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using System.Net;

namespace Company.ABC.Vesta.Functions.Api;

public sealed class HealthDependenciesFunction(IOptions<VestaOptions> options)
{
    [Function("HealthDependencies")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "health/dependencies")]
        HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(
    SettingsSummaryFactory.Create(
        options.Value,
        Array.Empty<ContainerHealth>()))
    .ConfigureAwait(false);

        return response;
    }
}