# Payment Reconciliation API

First milestone for a C# payment reconciliation portfolio project. The API records simulated payments in PostgreSQL using ASP.NET Core and EF Core. New records start in the pending state.

## Run locally

Install the .NET 10 SDK. Start a dedicated development database with Docker:

```sh
docker run --rm --name reconciliation-postgres \
  -e POSTGRES_PASSWORD=local-development \
  -e POSTGRES_DB=payments \
  -p 127.0.0.1:5433:5432 -d postgres:17
```

Once PostgreSQL is ready, run the API. It creates its schema on startup in the development database.

```sh
export ConnectionStrings__Payments='Host=localhost;Port=5433;Database=payments;Username=postgres;Password=local-development'
dotnet run --urls http://localhost:5080
```

## Try the endpoints

Amounts use the smallest unit of the currency. For example, 2500 NGN minor units are ₦25.00.

```sh
curl -i http://localhost:5080/payments \
  -H 'Content-Type: application/json' \
  -d '{"amountMinor":2500,"currency":"NGN"}'
```

Expect HTTP 201 and a Location header. Use the returned ID to read the saved record.

```sh
curl -i 'http://localhost:5080/payments/<payment-id>'
```

Next milestone is a simulated provider webhook and integration tests for status updates and duplicate events.
