using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Application Insights
builder.Services.AddApplicationInsightsTelemetryWorkerService();

// Firebase ID トークン検証（署名鍵キャッシュのため Singleton）
builder.Services.AddSingleton<MoneyBoardApi.FirebaseAuth>();

// Cosmos DB クライアントを DI に登録
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
