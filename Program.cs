using Microsoft.EntityFrameworkCore;
using PaymentReconciliation.Api.Data;
using PaymentReconciliation.Api.Payments;
using PaymentReconciliation.Api.Webhooks;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Payments")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Payments before starting the API.");
builder.Services.AddDbContext<PaymentsDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

app.MapPaymentEndpoints();
app.MapPost("/webhooks/provider", ProviderWebhookHandler.HandleAsync);

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await db.InitializeDevelopmentSchemaAsync();
}

await app.RunAsync();
