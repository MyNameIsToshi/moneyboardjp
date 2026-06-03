using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Cosmos DB クライアントを DI に登録
// Azure Functions Isolated では環境変数から直接読む
builder.Services.AddSingleton(_ =>
{
    var connStr = Environment.GetEnvironmentVariable("CosmosDb__ConnectionString")
        ?? throw new InvalidOperationException("CosmosDb__ConnectionString is not set");
    return new CosmosClient(connStr, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });
});

builder.Build().Run();
