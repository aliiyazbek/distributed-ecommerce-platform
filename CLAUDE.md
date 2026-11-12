# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

University course project (Internet Applications, Damascus University, 2026): a small distributed e-commerce platform. The point is security, performance, scalability and observability, not feature count or UI polish. Work is tracked as six numbered "Requirements" (1 API design & security, 2 gRPC between services, 3 performance & caching, 4 async/real-time, 5 scaling & load balancing, 6 monitoring). Code comments often tag which requirement a piece belongs to. README and `docs/` are in Arabic.

Workflow: `main` is protected. Work goes on a feature branch (e.g. `feature/auth`) and is merged through a reviewed PR.

## Running locally

Three processes plus infrastructure:

```bash
docker compose up -d                         # postgres:5432, redis:6379, mailpit (8025 UI / 1025 SMTP), pgadmin:5050
node inventory-service/server.js             # gRPC inventory service on :50051 (needs DB_* env vars, see below)
dotnet run --project src/Host                # ASP.NET API on http://localhost:5169 (Swagger at /swagger in Development)
cd frontend/ecommerce-store && npm run dev   # Vite React store; proxies /api and /images to :5169
```

- `inventory-service` reads `DB_USER`, `DB_HOST`, `DB_NAME`, `DB_PASSWORD`, `DB_PORT` (and optional `GRPC_PORT`) via `dotenv` from a `.env` file (git-ignored). Use the same Postgres credentials as `docker-compose.yml`. It creates its own `inventory` schema on startup.
- EF Core migrations are **not** applied on startup. Apply each module's context explicitly, e.g.:
  `dotnet ef database update --project src/Modules/Products --startup-project src/Host --context ProductsDbContext`
  (contexts: `UsersDbContext`, `ProductsDbContext`, `OrdersDbContext`). Add migrations the same way with `dotnet ef migrations add <Name> ...`; each module owns its `Migrations/` folder.
- New users always register as Customer (RoleId 2). To get an Admin, update the DB: `UPDATE users."Users" SET "RoleId"=1 WHERE "Email"='...';`
- Disable the Redis cache for before/after benchmarks with the env var `Caching__Enabled=false`.

## Build, lint, test

```bash
dotnet build ECommercePlatform.slnx
cd frontend/ecommerce-store && npm run lint
cd frontend/ecommerce-store && npm test                                   # node:test, tests/*.test.mjs
cd frontend/ecommerce-store && node --test --test-name-pattern "<name>" tests/services.test.mjs   # single test
cd frontend/ecommerce-store && ADMIN_EMAIL=... ADMIN_PASSWORD=... npm run test:last-piece        # end-to-end smoke script against a running API
```

There is no .NET test project. Load testing is being moved to JMeter (no test plans in the repo yet); see `docs/performance-testing.md` for the caching design.

## Architecture

### Backend: modular monolith (.NET 10)

`src/Host` is the only executable. It wires everything in `Program.cs`: one Postgres connection string shared by three DbContexts (each with its own schema via `HasDefaultSchema` and its own `MigrationsAssembly`), JWT bearer auth (HS256, `Jwt:Key`), API versioning, global `ProblemDetails` error handling (`Shared/GlobalExceptionHandler`), per-IP fixed-window rate limiting (`auth-limit` policy on login/register), security headers, static files (uploaded product images under `wwwroot/images`), Redis distributed cache, and MediatR scanning all loaded assemblies.

Each module in `src/Modules/{Users,Products,Orders,Notifications}` is a class library with the same layout: `Controllers`, `Contracts` (DTOs), `Entities`, `Interfaces`, `Services`, `Validators` (FluentValidation, auto-validation), `Persistence`, and an `XxxModuleExtensions.AddXxxModule()` registration method called from `Program.cs`.

Conventions that span modules:
- **Decorator pattern for cross-cutting concerns.** The concrete class (e.g. `ProductService`, `ProductRepository`) is registered as itself, and the interface resolves to a decorator wrapping it via a factory lambda (`Logging*Decorator` for services, `CachingProductRepositoryDecorator` for the Products repository). Add logging, caching and similar concerns as decorators in the module's `Decorators/` folder, not inside services.
- **Modules never reference each other.** They talk only through `src/Shared` contracts over MediatR: queries/commands (`Shared/Queries/GetProductsByIdsQuery`, handled in Products and sent from Orders) and integration events published with `IPublisher` (`ProductCreated`, `UserRegistered`, `OrderStatusChanged`), handled by `INotificationHandler`s (e.g. in Notifications).
- Controllers derive from `Shared.Controllers.BaseController` and route as `api/v{version:apiVersion}/[controller]` (except Users: `api/users`). Roles are `Admin` and `Customer` (`[Authorize(Roles = ...)]`).

### Inventory: separate Node.js gRPC service

Stock quantities do **not** live in the Products schema (they were migrated out). `inventory-service/server.js` owns the `inventory.stock` and `inventory.reservations` tables and implements `Protos/inventory.proto`: `CheckQuantity` (one product), `CheckQuantities` (batched stock for a whole list page), `ReserveItems` (all lines of an order in one transaction, all-or-nothing, rows locked in `product_id` order to avoid deadlocks; business failures come back as `Success=false` with `Shortages`/`MissingProductIds`, not as gRPC errors), `ReleaseOrder` (idempotent release of everything reserved for an order) and `InitializeStock`. Always use the batched RPCs for lists/orders; never call the inventory service once per item (N+1). The .NET side gets generated client stubs (`Grpc.Tools`, namespace `Modules.Orders.Integration.Protos`) and both Products and Orders register named gRPC clients pointing at `http://127.0.0.1:50051`. Products initializes stock on create and fetches live quantity on reads (one `CheckQuantities` call per list page); Orders makes one `ReserveItems` call per order and compensates with `ReleaseOrder` (also after a gRPC timeout, since the reservation may have committed). The order error message `Insufficient stock for '<name>'. Requested: X, Available: Y.` is parsed by the frontend (`parseStockError`), so keep that format. The proto file is the shared contract: changing it affects both the Node server (loaded at runtime) and the C# build.

### Caching (Requirement 3)

Caching is a decorator over the Products repository, not part of the service:

```
IProductService    → LoggingProductServiceDecorator    → ProductService (business logic + live stock via gRPC)
IProductRepository → CachingProductRepositoryDecorator → ProductRepository (EF Core)
```

`ProductsModuleExtensions` only registers the caching decorator when `Caching:Enabled` is true (bound to `ProductCacheOptions`), so the "caching off" baseline never touches Redis. The decorator is cache-aside over `IDistributedCache`: `product:{id}` (TTL 300s) and list pages under `products:list:{version}:{category}:{page}:{pageSize}` (TTL 60s). Every repository write commits first, then deletes the product key and bumps the list version (no key scanning). Available quantity is never cached; the repository only returns `Product` rows. On a Redis error the decorator falls back to the database and the singleton `Shared/Caching/CacheCircuitBreaker` skips Redis for `Caching:FailureCooldownSeconds` (30s); invalidation is still always attempted. The decorator records HIT/MISS/BYPASS in the scoped `Shared/Caching/CacheStatus`, and `Shared/Http/CacheStatusHeaderMiddleware` writes it as `X-Cache` (no header when nothing was looked up). `GetProductsByIdsQueryHandler` (used by Orders) reads the DB directly on purpose so order prices are never cached. GET product endpoints also use `[ETag]` (`Shared/Http/ETagFilter`): SHA-256 of the full body, `304` on matching `If-None-Match`, `Cache-Control: private, no-cache`. The product list rejects `page < 1`, `pageSize` outside 1–100 and `category` longer than 100 chars with 400.

### Frontend (`frontend/ecommerce-store`)

React 19 + React Router 7 + Vite, plain JS/JSX with per-component CSS. All HTTP goes through `src/services/apiClient.js` (`request()`: attaches the bearer token from `session.js`, 8s timeout, throws `ApiError` with `status`/`fields`; `status 0` or `503` means unreachable; a 401 triggers the registered unauthorized handler). Domain logic lives in `src/services/*Service.js` and form helpers (`productForm.js`, `accountForm.js`) as pure functions so they can be unit-tested with `node:test` without a browser. Auth and cart state are React contexts in `src/context`; role-gated routes use `RequireRole`.
