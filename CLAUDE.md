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

Single ASP.NET Core Web API project (`TodoApi`, `net10.0`) using controllers, not minimal APIs. Packages: `Microsoft.EntityFrameworkCore.InMemory`, `FluentValidation.DependencyInjectionExtensions`, `Microsoft.AspNetCore.OpenApi`.

### Request flow

A write request (e.g. `POST /api/todos`) passes through these stages in order; each can end the request with a ProblemDetails response:

0. **`UseAIAgentHeader()`** (`Infrastructure/AIAgentHeaderMiddleware`) — outermost middleware; adds `AIAgent: claudecode` to every response. It sets the header in a `Response.OnStarting` callback on purpose: `UseExceptionHandler` clears headers before writing error responses, so setting it directly would drop it from 500s.
1. **`UseExceptionHandler()`** — wraps everything below it. Unhandled exceptions → `Infrastructure/GlobalExceptionHandler`.
2. **Routing** — no match → bare 404/405, converted to ProblemDetails by `UseStatusCodePages()`.
3. **JSON binding** — malformed JSON or unknown enum *names* (`"Urgent"`) → 400 from `[ApiController]`'s automatic model-state check. Validators never see these.
4. **`Infrastructure/FluentValidationFilter`** (global MVC filter) — runs `IValidator<T>` for each action argument → 400 on failure.
5. **`Controllers/TodosController`** — maps DTO → entity, applies domain logic, saves via `TodoDbContext`, returns `TodoResponse`.

All error bodies share one shape: `AddProblemDetails` in `Program.cs` stamps `instance` (`"POST /api/todos"`) and `traceId` on every one, including validation 400s.

### Layers

- **`Models/`** — `TodoItem` entity plus `TodoStatus` (Todo, InProgress, Done, Cancelled) and `TodoPriority` (Low, Medium, High, Critical). Never returned from the API directly.
- **`Dtos/`** — request/response records; `TodoResponse.From(entity)` is the only entity→response mapping. DTOs carry **no** validation attributes.
- **`Validators/`** — FluentValidation validators, one per request DTO. Shared rules (`ValidTitle`, `ValidDescription`, `ValidEnum`) are extension methods on `TodoRules`, which also holds the length constants.
- **`Data/`** — `TodoDbContext` (EF Core **InMemory**, db name `"Todos"`; all data is lost on restart) and `SeedData`, which inserts 10 sample todos only in Development and only when the table is empty. There is no repository/service layer: the controller uses `TodoDbContext` directly.
- **`Infrastructure/`** — cross-cutting ASP.NET plumbing: the exception handler and the validation filter.

### Conventions and invariants

- **Usings**: all namespace imports live in `TodoApi/GlobalUsings.cs` (on top of the SDK's `ImplicitUsings`). Source files have no `using` directives — add new namespaces there instead of per file.
- **Adding a request DTO**: write an `AbstractValidator<T>` in `Validators/` and it's picked up automatically (`AddValidatorsFromAssemblyContaining<Program>()` + the global filter). No per-action wiring. The deprecated `FluentValidation.AspNetCore` package is intentionally not used.
- **Validation is FluentValidation only.** `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` is on, so MVC does not add its own "required" errors for non-nullable strings — a missing `Title` arrives as `null` and the validator must catch it. Don't reintroduce DataAnnotations on DTOs.
- **Create vs update rules differ on purpose**: `CreateTodoRequestValidator` requires `DueDate` in the future; `UpdateTodoRequestValidator` does not, because existing todos can be overdue.
- **Enums** are serialized as strings via a global `JsonStringEnumConverter`, but the converter still accepts integers, so every enum field needs `ValidEnum()` to reject undefined values like `99`. Sorting by priority/status uses enum declaration order — reordering members changes sort results.
- **Title trimming**: the controller stores `request.Title.Trim()`, so `ValidTitle` measures the *trimmed* length. Keep the two in sync.
- **Length limits are duplicated**: `TodoRules.TitleMaxLength`/`DescriptionMaxLength` (200/2000) and `HasMaxLength(200)`/`HasMaxLength(2000)` in `TodoDbContext`. Change both together.
- **Status changes** all go through `TodosController.SetStatus`, which sets/clears `CompletedAt` when entering/leaving `Done` and stamps `UpdatedAt`. Route any new status-mutating code through it.
- **Time**: use the injected `TimeProvider` (registered as `TimeProvider.System`), never `DateTime.UtcNow` — in the controller, `SeedData`, and validators (`CreateTodoRequestValidator` takes it via constructor).
- **Exception mapping** (`GlobalExceptionHandler`): `KeyNotFoundException`→404, `DbUpdateConcurrencyException`/`DbUpdateException`→409, client abort→499, everything else→500. Stack traces go into `detail` only in Development. `InvalidOperationException`/`ArgumentException` are deliberately **not** mapped to 400, since the framework throws them for server-side bugs. The controller currently returns `NotFound()` itself rather than throwing; the mappings exist for a future service layer.
