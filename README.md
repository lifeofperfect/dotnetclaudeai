# Todo API

A secure REST API for managing personal todo lists, built with ASP.NET Core on .NET 10.

Each user signs up, logs in, and manages their own todos with a **status** and a **priority**. The API is hardened for production use: bearer-token authentication, per-user data isolation, optimistic concurrency with ETags, rate limiting, request size limits, CORS and security headers.

## Features

- **Todos with status and priority**: filter, search, sort and page through them.
- **Accounts and sign-in**: register, log in, refresh tokens (ASP.NET Core Identity).
- **Per-user data**: users only ever see their own todos.
- **Safe concurrent edits**: every write must send the version it last read, so edits are never silently overwritten.
- **Validation** with clear error messages (FluentValidation).
- **Consistent errors**: every error is [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457) JSON with a `traceId`.
- **Hardening**: rate limits, 64 KB request cap, CORS allow-list, security headers, no server fingerprinting.

## Tech stack

| | |
|---|---|
| Runtime | .NET 10, ASP.NET Core (controllers) |
| Auth | ASP.NET Core Identity, bearer tokens |
| Data | Entity Framework Core, in-memory provider |
| Validation | FluentValidation 12 |
| API docs | OpenAPI (`/openapi/v1.json`, Development only) |

> **Note:** data is stored **in memory** and is lost whenever the app restarts. See [Production checklist](#production-checklist).

## Getting started

### Prerequisites

- [.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0) or a later 10.0 feature band (pinned in `global.json`)

Check with:

```sh
dotnet --list-sdks
```

### Run

```sh
git clone https://github.com/lifeofperfect/dotnetclaudeai.git
cd dotnetclaudeai/TodoApi
dotnet run
```

The API listens on **http://localhost:5010** in the Development environment. Use `dotnet run --launch-profile https` to also listen on https://localhost:7001.

In Development the app seeds a demo account with 10 sample todos:

| Email | Password |
|---|---|
| `demo@example.com` | `Demo-Passw0rd!` |

### Try it

[`TodoApi/TodoApi.http`](TodoApi/TodoApi.http) contains a ready-made request for every endpoint. It works with the VS Code [REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) extension, JetBrains Rider and Visual Studio. Run the login request first; the token and ETag are then filled in automatically.

Or with curl:

```sh
# 1. Log in and keep the access token
TOKEN=$(curl -s http://localhost:5010/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"demo@example.com","password":"Demo-Passw0rd!"}' | jq -r .accessToken)

# 2. List your todos, most urgent first
curl -s "http://localhost:5010/api/todos?sortBy=priority&descending=true" \
  -H "Authorization: Bearer $TOKEN"
```

## Authentication

Authentication endpoints live under `/api/auth` and are provided by ASP.NET Core Identity:

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/auth/register` | Create an account |
| POST | `/api/auth/login` | Get an access token and refresh token |
| POST | `/api/auth/refresh` | Exchange a refresh token for a new access token |
| GET / POST | `/api/auth/manage/info` | Read or update the signed-in account |
| POST | `/api/auth/forgotPassword`, `/api/auth/resetPassword` | Password reset flow |

A successful login returns:

```json
{
  "tokenType": "Bearer",
  "accessToken": "…",
  "expiresIn": 3600,
  "refreshToken": "…"
}
```

Send the access token on every `/api/todos` request:

```
Authorization: Bearer <accessToken>
```

**Password rules:** at least 12 characters, with an uppercase letter, a lowercase letter, a digit and a symbol. Each email can register only once. After 5 failed logins the account is locked for 15 minutes.

Only bearer tokens are supported. Cookie login (`?useCookies=true` or `?useSessionCookies=true`) is intentionally disabled and returns `400 Bad Request`.

## Todos API

All `/api/todos` endpoints require authentication. Users can only access their own todos: another user's todo returns `404 Not Found`.

| Method | Route | Purpose | Needs `If-Match` |
|---|---|---|---|
| GET | `/api/todos` | List, filter, search, sort, page | |
| GET | `/api/todos/{id}` | Get one todo (returns an `ETag`) | |
| POST | `/api/todos` | Create a todo | |
| PUT | `/api/todos/{id}` | Replace a todo | ✔ |
| PATCH | `/api/todos/{id}/status` | Change status only | ✔ |
| PATCH | `/api/todos/{id}/priority` | Change priority only | ✔ |
| DELETE | `/api/todos/{id}` | Delete a todo | ✔ |

### The todo object

```json
{
  "id": 1,
  "title": "Fix login bug on mobile",
  "description": "Users on iOS get logged out after 5 minutes.",
  "status": "InProgress",
  "priority": "Critical",
  "dueDate": "2026-10-01T09:00:00Z",
  "createdAt": "2026-09-25T09:00:00Z",
  "updatedAt": "2026-09-25T09:00:00Z",
  "completedAt": null,
  "rowVersion": "846886f4-ed88-49a6-ac02-0eddb58e12bb"
}
```

| Field | Values |
|---|---|
| `status` | `Todo` (default), `InProgress`, `Done`, `Cancelled` |
| `priority` | `Low`, `Medium` (default), `High`, `Critical` |
| `completedAt` | Set automatically when status becomes `Done`, cleared when it changes away |
| `rowVersion` | Current version, the same value as the `ETag` header |

### Listing todos

`GET /api/todos` accepts these query parameters:

| Parameter | Description | Default |
|---|---|---|
| `status` | Filter by status | all |
| `priority` | Filter by priority | all |
| `search` | Text in the title or description (max 100 characters) | none |
| `sortBy` | `createdat`, `priority`, `status` or `duedate` | `createdat` |
| `descending` | `true` to reverse the sort | `false` |
| `page` | Page number, starting at 1 | `1` |
| `pageSize` | Items per page, 1–100 | `20` |

The response is paged:

```json
{
  "items": [ { "id": 1, "title": "…" } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 10,
  "totalPages": 1
}
```

### Creating and updating

```http
POST /api/todos
Content-Type: application/json

{
  "title": "Write quarterly report",
  "description": "Include Q3 numbers",
  "priority": "High",
  "dueDate": "2027-01-15T17:00:00Z"
}
```

Validation rules:

- `title` is required: 1–200 characters after trimming.
- `description` is optional, at most 2000 characters.
- `status` and `priority` must be one of the values listed above.
- `dueDate` must be in the future **when creating**. Updates may keep a past due date, since existing todos can be overdue.

`PUT` takes the full object (`title`, `description`, `status`, `priority`, `dueDate`). The `PATCH` endpoints take just `{ "status": "Done" }` or `{ "priority": "Low" }`.

### Concurrency: ETag and If-Match

Every todo has a version that changes on every save. Reads return it in the `ETag` response header and as `rowVersion` in the body. Every `PUT`, `PATCH` and `DELETE` **must** send the version it is based on:

```sh
# Read the todo and its current ETag
curl -si http://localhost:5010/api/todos/1 -H "Authorization: Bearer $TOKEN"
# ETag: "846886f4-ed88-49a6-ac02-0eddb58e12bb"

# Write, sending that ETag back
curl -X PATCH http://localhost:5010/api/todos/1/status \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -H 'If-Match: "846886f4-ed88-49a6-ac02-0eddb58e12bb"' \
  -d '{"status":"Done"}'
```

| Situation | Response |
|---|---|
| `If-Match` missing | `428 Precondition Required` |
| Someone changed the todo since you read it | `412 Precondition Failed`: re-read it and retry |
| Version matches | Success, with the new `ETag` |

`If-Match: *` skips the version check (for example, "delete regardless of changes"), but the header is still required.

## Errors

Every error uses the same Problem Details shape:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "POST /api/todos",
  "errors": {
    "Title": ["Title is required."],
    "Priority": ["Priority must be one of: Low, Medium, High, Critical."]
  },
  "traceId": "00-4991686ebcb548f79e70d32b52b65349-4e7edfce252272a2-00"
}
```

| Status | Meaning |
|---|---|
| 400 | Validation failed or malformed JSON |
| 401 | Missing, invalid or expired access token |
| 404 | Todo doesn't exist or belongs to another user |
| 405 | HTTP method not supported on this route |
| 412 | `If-Match` doesn't match the current version |
| 413 | Request body over 64 KB |
| 428 | `If-Match` header missing on a write |
| 429 | Rate limit exceeded: wait for the `Retry-After` seconds |
| 500 | Unexpected server error: quote the `traceId` when reporting it |

Stack traces are included in the error `detail` only in the Development environment.

## Security

| Protection | Details |
|---|---|
| Authentication | Bearer tokens only, so there is no cookie-based CSRF surface |
| Authorization | Every todo endpoint requires sign-in; data is scoped to the owner |
| Rate limiting | 100 requests/minute per user (per IP when anonymous); 10/minute per IP on `/api/auth/*` |
| Request size | Bodies over 64 KB rejected with 413 |
| CORS | Only origins listed in `Cors:AllowedOrigins` (none by default) |
| Security headers | `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`, `Referrer-Policy`, `Cross-Origin-Opener-Policy`, `Cache-Control: no-store`; HSTS outside Development |
| Fingerprinting | No `Server` header; production error messages never name internal .NET types |
| Host filtering | Only hosts in `AllowedHosts` are served |

Every response also carries an `AIAgent: claudecode` header.

## Configuration

Settings live in `TodoApi/appsettings.json`, with Development overrides in `appsettings.Development.json`. Any value can be overridden with an environment variable, using `__` for nesting (e.g. `RateLimiting__PermitLimit=200`).

| Setting | Default | Description |
|---|---|---|
| `AllowedHosts` | `localhost` | Host names the API responds to (`;`-separated) |
| `Cors:AllowedOrigins` | `[]` (Development: `http://localhost:3000`, `http://localhost:5173`) | Browser origins allowed to call the API |
| `RateLimiting:PermitLimit` | `100` | Requests per window, per user or IP |
| `RateLimiting:WindowSeconds` | `60` | Window length |
| `RateLimiting:AuthPermitLimit` | `10` | Requests per window to `/api/auth/*`, per IP |
| `RateLimiting:AuthWindowSeconds` | `60` | Window length for auth endpoints |

## Production checklist

The in-memory setup is meant for development. Before deploying:

- [ ] **Use a real database** (PostgreSQL or SQL Server) with EF Core migrations. The in-memory provider loses all users and todos on restart and doesn't enforce constraints.
- [ ] **Set `AllowedHosts`** to your real domain(s); otherwise every request returns 400.
- [ ] **Set `Cors:AllowedOrigins`** to your front-end origin(s), if a browser app calls the API.
- [ ] **Configure forwarded headers** (`UseForwardedHeaders`) when running behind a reverse proxy or load balancer, so HTTPS detection and per-IP rate limits see the real client.
- [ ] **Persist the Data Protection key ring** to shared storage when running more than one instance or in containers. Access tokens are protected with these keys and break across instances and restarts otherwise.
- [ ] **Register an email sender** (`IEmailSender<AppUser>`) so account confirmation and password reset emails are actually sent.
- [ ] **Tune rate limits** for your expected traffic.
- [ ] Run with `ASPNETCORE_ENVIRONMENT=Production` (the default when unset). This disables seed data, OpenAPI and detailed errors.

## Project structure

```
.
├── global.json                  # pins the .NET SDK version
├── CLAUDE.md                    # architecture notes for AI coding assistants
└── TodoApi/
    ├── Program.cs               # service registration and middleware pipeline
    ├── GlobalUsings.cs          # all namespace imports (files have no using directives)
    ├── Controllers/             # TodosController
    ├── Models/                  # TodoItem, AppUser, status/priority enums
    ├── Dtos/                    # request/response contracts
    ├── Validators/              # FluentValidation rules
    ├── Data/                    # TodoDbContext (EF Core + Identity), development seed data
    ├── Infrastructure/          # exception handling, validation filter, rate limiting, header middleware
    └── TodoApi.http             # sample requests
```

## Development

```sh
cd TodoApi
dotnet build
dotnet run
```

There is no automated test suite yet. `TodoApi.http` covers every endpoint for manual testing.
