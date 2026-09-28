# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Toolchain

`global.json` pins .NET SDK 10.0.401. On this machine SDK 10 lives in `~/.dotnet` (installed via `dotnet-install.sh`), while the `dotnet` on the default PATH is `/usr/local/share/dotnet`, which only has SDKs 6–9 — so plain `dotnet` fails with "SDK not found". Prefix commands with:

```sh
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH
```

## Commands

Run from `TodoApi/`:

```sh
dotnet build
dotnet run                      # "http" launch profile → http://localhost:5010, Development env
dotnet run --launch-profile https   # https://localhost:7001 + http://localhost:5010
```

- There is no test project yet. If one is added, run a single test with `dotnet test --filter "FullyQualifiedName~<Name>"`.
- `TodoApi/TodoApi.http` has a request for every endpoint (VS Code REST Client / Rider / VS).
- OpenAPI document is at `/openapi/v1.json` (Development only).
- zsh does not word-split unquoted variables: `H='Content-Type: application/json'; curl -H "$H" …` works, `curl $H …` silently drops the header.

## Architecture

Single ASP.NET Core Web API project (`TodoApi`, `net10.0`) using controllers, not minimal APIs. Packages: `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `FluentValidation.DependencyInjectionExtensions`, `Microsoft.AspNetCore.OpenApi`.

### Request flow

A request passes through these stages in order (`Program.cs`); each can end it with a ProblemDetails response:

1. **Kestrel limits** — bodies over 64 KB (`MaxRequestBodySize`) fail while being read → 413 via `GlobalExceptionHandler`. `AddServerHeader = false` removes `Server: Kestrel`.
2. **`UseAIAgentHeader()` / `UseSecurityHeaders()`** (`Infrastructure/`) — outermost; add `AIAgent: claudecode` plus nosniff, `X-Frame-Options`, CSP, `Referrer-Policy`, COOP and `Cache-Control: no-store` to every response. Both set headers in a `Response.OnStarting` callback on purpose: `UseExceptionHandler` clears headers before writing error responses, so setting them directly would drop them from 500s.
3. **`UseExceptionHandler()`** — unhandled exceptions → `Infrastructure/GlobalExceptionHandler`. `UseStatusCodePages()` turns bare 401/404/405 into ProblemDetails.
4. **`UseHsts()`** (non-Development only) and `UseHttpsRedirection()`.
5. **`UseCors()`** — default policy; origins from `Cors:AllowedOrigins` (empty in `appsettings.json`, localhost:3000/5173 in Development). No credentials; exposes `ETag`, `Location`, `Retry-After`.
6. **`UseAuthentication()`** — ASP.NET Core Identity **bearer tokens only** (`AddBearerToken`, no cookie scheme, so no CSRF surface). Tokens are opaque (Data Protection), not JWTs. Because no cookie scheme exists, an endpoint filter on the `/api/auth` group rejects `?useCookies=true` / `?useSessionCookies=true` with 400; without it, `MapIdentityApi`'s login throws a 500.
7. **`UseRateLimiter()`** — after authentication so the global limiter partitions per user (`user:<id>`), falling back to `ip:<addr>` for anonymous calls. `/api/auth/*` also has the stricter per-IP `auth` policy. Limits come from the `RateLimiting` config section (`Infrastructure/RateLimiting.cs`). 429 responses carry `Retry-After`.
8. **`UseAuthorization()`** — `MapControllers().RequireAuthorization()` makes every controller require a signed-in user; `/api/auth/*` (from `MapIdentityApi<AppUser>()`) handles its own auth.
9. **JSON binding** — malformed JSON or unknown enum *names* (`"Urgent"`) → automatic 400. Outside Development, `AllowInputFormatterExceptionMessages = false` hides messages that name internal .NET types.
10. **`Infrastructure/FluentValidationFilter`** (global MVC filter) — runs `IValidator<T>` for each action argument (including `[FromQuery] TodoListQuery`) → 400.
11. **`Controllers/TodosController`**.

All error bodies share one shape: `AddProblemDetails` stamps `instance` (`"POST /api/todos"`) and `traceId` on every one.

### Layers

- **`Models/`** — `AppUser : IdentityUser`, the `TodoItem` entity, and the `TodoStatus`/`TodoPriority` enums. Entities are never returned from the API directly.
- **`Dtos/`** — request/response records, `TodoListQuery` (list query string) and `PagedResponse<T>`. `TodoResponse.From(entity)` is the only entity→response mapping. DTOs carry **no** validation attributes.
- **`Validators/`** — FluentValidation validators, one per request DTO. Shared rules (`ValidTitle`, `ValidDescription`, `ValidEnum`) are extension methods on `TodoRules`, which also holds the length constants. `TodoListQueryValidator` holds the paging/search limits (pageSize ≤ 100, search ≤ 100 chars, allowed `sortBy` values).
- **`Data/`** — `TodoDbContext : IdentityDbContext<AppUser>` (EF Core **InMemory**, db name `"Todos"`; users and todos are lost on restart) and `SeedData`, which in Development only creates `demo@example.com` / `Demo-Passw0rd!` owning 10 sample todos. No repository/service layer: the controller uses `TodoDbContext` directly.
- **`Infrastructure/`** — cross-cutting plumbing: exception handler, validation filter, header middlewares, rate limiting setup.

### Conventions and invariants

- **Usings**: all namespace imports live in `TodoApi/GlobalUsings.cs` (on top of the SDK's `ImplicitUsings`). Source files have no `using` directives — add new namespaces there instead of per file.
- **Ownership**: every todo has an `OwnerId` (FK to `AppUser`). All controller queries go through `MyTodos`, which filters by the caller's `ClaimTypes.NameIdentifier`. Another user's todo returns **404, not 403**, so ids can't be probed. Never query `db.Todos` directly in the controller.
- **Optimistic concurrency (required)**: `TodoItem.RowVersion` is an app-managed `Guid` concurrency token, regenerated in `TodoDbContext.SaveChanges[Async]` on every insert/update (portable across providers, unlike a SQL Server `rowversion`). It's exposed as the `ETag` header (`"<guid>"`, same format as `rowVersion` in the body). PUT/PATCH/DELETE go through `LoadForWriteAsync`: missing `If-Match` → 428, mismatch or weak ETag → 412, `If-Match: *` matches any version. A write racing between that check and `SaveChanges` throws `DbUpdateConcurrencyException` → also 412. Any new write endpoint must use `LoadForWriteAsync` and call `SetETag`.
- **List endpoint is always paged** (`PagedResponse<T>`) and sorts with a `.ThenBy(t => t.Id)` tie-breaker so pages are stable. Keep the tie-breaker on any new sort.
- **Adding a request DTO**: write an `AbstractValidator<T>` in `Validators/` and it's picked up automatically (`AddValidatorsFromAssemblyContaining<Program>()` + the global filter). The deprecated `FluentValidation.AspNetCore` package is intentionally not used.
- **Validation is FluentValidation only.** `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` is on, so a missing `Title` arrives as `null` and the validator must catch it. Don't reintroduce DataAnnotations on DTOs.
- **Create vs update rules differ on purpose**: `CreateTodoRequestValidator` requires `DueDate` in the future; `UpdateTodoRequestValidator` does not, because existing todos can be overdue.
- **Enums** are serialized as strings via a global `JsonStringEnumConverter`, but the converter still accepts integers, so every enum field needs `ValidEnum()` to reject undefined values like `99`. Sorting by priority/status uses enum declaration order — reordering members changes sort results.
- **Title trimming**: the controller stores `request.Title.Trim()`, so `ValidTitle` measures the *trimmed* length. Keep the two in sync.
- **Length limits** have one source: `TodoRules.TitleMaxLength`/`DescriptionMaxLength`, used by both validators and `TodoDbContext`.
- **Status changes** all go through `TodosController.SetStatus`, which sets/clears `CompletedAt` when entering/leaving `Done` and stamps `UpdatedAt`.
- **Time**: use the injected `TimeProvider` (registered as `TimeProvider.System`), never `DateTime.UtcNow` — in the controller, `SeedData`, and validators.
- **Exception mapping** (`GlobalExceptionHandler`): `BadHttpRequestException`→its own status (413 for oversized bodies), `KeyNotFoundException`→404, `DbUpdateConcurrencyException`→412, `DbUpdateException`→409, client abort→499, everything else→500. Stack traces go into `detail` only in Development. `InvalidOperationException`/`ArgumentException` are deliberately **not** mapped to 400, since the framework throws them for server-side bugs.

### Production configuration

- `AllowedHosts` is `localhost` in `appsettings.json`; set the real domain(s) per environment or every request gets 400.
- `Cors:AllowedOrigins` is empty by default; list front-end origins explicitly. Never `AllowAnyOrigin`.
- Tune `RateLimiting:*` via config/env (e.g. `RateLimiting__PermitLimit=5` for testing). Behind a reverse proxy, add `UseForwardedHeaders` or every client shares the proxy's IP bucket.
- Bearer tokens are protected with Data Protection keys; with multiple instances or containers, persist the key ring to shared storage or tokens break across instances/restarts.
- Identity's email sender is the default no-op, so confirmation/password-reset emails are not actually sent until an `IEmailSender<AppUser>` is registered.
