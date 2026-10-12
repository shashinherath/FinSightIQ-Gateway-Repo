# FinSightIQ API Gateway

The single external entry point and reverse proxy for all client traffic in the FinSightIQ financial intelligence platform.

Built on **ASP.NET Core (.NET 8)** and **YARP (Yet Another Reverse Proxy)**.

---

## 1. Overview & Architecture

In the FinSightIQ polyrepo microservices architecture, internal business services are not publicly addressable. The API Gateway serves as the single perimeter, enforcing:

- **Perimeter Routing:** Reverse proxy routing via YARP under `/api/v1/`.
- **Authentication:** RS256 JWT validation using the User Service's public key only (the gateway never sees the private key).
- **Identity Propagation:** Strips spoofed client headers and attaches verified `X-User-Id` and `X-User-Role` headers downstream.
- **Role-Based Authorization:** Evaluates `Viewer`, `Analyst`, and `Administrator` policies at the edge. (Resource ownership remains verified by owning services).
- **Rate Limiting:** Sliding-window limits for anonymous callers (60 req/min) and authenticated callers (600 req/min), plus rolling 24-hour prediction quota (50 req/24h per user).
- **Observability:** Distributed correlation tracking using `X-Correlation-Id` (reusing `FinSightIQ.BuildingBlocks.Middleware.CorrelationId`).
- **Health Aggregation:** Granular `/health`, `/health/live`, and `/health/ready` endpoints with downstream timeout safeguards.
- **Error Standards:** Consistent RFC 7807 Problem Details error responses with stable machine-readable error codes.

```
+-------------------------------------------------------------+
|                     React Frontend                          |
+-------------------------------------------------------------+
                               |
                   HTTP / WebSocket (5000)
                               v
+-------------------------------------------------------------+
|                 FinSightIQ API Gateway (YARP)               |
|   - Correlation ID & Logging     - Rate Limiting            |
|   - Security Headers & CORS      - RS256 JWT Auth           |
|   - Identity Propagation         - RFC 7807 Problem Details |
+-------------------------------------------------------------+
        |               |              |              |
        v (5101)        v (5102)       v (5103)       v (5104/5105)
   User Service    Market Data    Prediction     Portfolio &
                     Service        Service        Sentiment
```

---

## 2. API Routes & Clusters

The public base URL is `http://localhost:5000/api/v1`.

| Module | Route Pattern | Target Cluster | Auth Policy | Rate Limit |
|---|---|---|---|---|
| **Auth (Public)** | `POST /api/v1/auth/register`<br>`POST /api/v1/auth/login`<br>`POST /api/v1/auth/refresh`<br>`POST /api/v1/auth/logout` | `user-service` | Anonymous | 60/min (IP) |
| **Auth (Protected)** | `/api/v1/auth/{**catch-all}` | `user-service` | `ViewerPolicy` | 600/min (User) |
| **Watchlists** | `/api/v1/watchlists/{**catch-all}`<br>`/api/v1/watchlist/{**catch-all}` | `user-service` | `ViewerPolicy` | 600/min (User) |
| **User Admin** | `/api/v1/users/{**catch-all}` | `user-service` | `AdminPolicy` | 600/min (User) |
| **Market Data** | `/api/v1/market/{**catch-all}` | `market-data-service` | `ViewerPolicy` | 600/min (User) |
| **Market Hub** | `/hubs/market/{**catch-all}` | `market-data-service` | Query string / Bearer | Handshake |
| **Predictions** | `POST /api/v1/predictions` | `prediction-service` | `AnalystPolicy` | 50 / 24h per user |
| **Retrain** | `POST /api/v1/predictions/retrain` | `prediction-service` | `AdminPolicy` | 600/min (User) |
| **Predictions (Read)** | `/api/v1/predictions/{**catch-all}` | `prediction-service` | `ViewerPolicy` | 600/min (User) |
| **Portfolios** | `/api/v1/portfolio` & `/{**catch-all}` | `portfolio-service` | `AnalystPolicy` | 600/min (User) |
| **Alerts** | `/api/v1/alerts` & `/{**catch-all}` | `portfolio-service` | `AnalystPolicy` | 600/min (User) |
| **Jobs** | `/api/v1/jobs/{**catch-all}` | `portfolio-service` | `ViewerPolicy` | 600/min (User) |
| **Sentiment** | `/api/v1/sentiment/{**catch-all}` | `sentiment-service` | `ViewerPolicy` | 600/min (User) |

---

## 3. Security & Identity Propagation

### 3.1 RS256 JWT Validation
- Access tokens must be signed with RS256 by the User Service private key.
- The gateway verifies the signature using `Jwt:PublicKeyPem`.
- In `Development` mode, if `Jwt:PublicKeyPem` is omitted, the gateway generates a local ephemeral RSA key pair for testing convenience.
- In `Staging` and `Production`, missing public keys cause startup to fail immediately.

### 3.2 Trusted Identity Propagation
- **Untrusted Header Stripping:** Any incoming client-supplied `X-User-Id` or `X-User-Role` is unconditionally stripped by middleware and proxy transforms.
- **Validated Claim Injection:** On authenticated requests, `X-User-Id` is extracted from `sub` (or `NameIdentifier`) and `X-User-Role` from `role`. These are injected into the downstream request.
- Unauthenticated requests never receive identity headers.

---

## 4. Rate Limiting & Prediction Quota

| Tier | Window | Limit | Partitioning Key | Rejection Code |
|---|---|---|---|---|
| **Anonymous** | Sliding 1 minute | 60 req | Client IP (`X-Forwarded-For` / Remote IP) | `RATE_LIMITED` |
| **Authenticated** | Sliding 1 minute | 600 req | Validated User ID | `RATE_LIMITED` |
| **Predictions** | Rolling 24 hours | 50 req | Validated User ID | `QUOTA_EXCEEDED` |

Every response carries rate limit headers:
```http
X-RateLimit-Limit: 600
X-RateLimit-Remaining: 573
X-RateLimit-Reset: 1745049600
```

When limits are exceeded:
- HTTP status `429 Too Many Requests`
- Header `Retry-After: <seconds>`
- RFC 7807 Problem Details response body

### Quota Store Architecture
Prediction quota tracking is abstracted behind `IPredictionQuotaStore`:
- **Redis (`RedisPredictionQuotaStore`):** Uses Redis sorted sets with an atomic Lua script to track rolling timestamps across multiple gateway replicas when `Redis__ConnectionString` is set.
- **In-Memory (`InMemoryPredictionQuotaStore`):** Thread-safe fallback used when Redis is unavailable or in local developer environments.

---

## 5. Health Endpoints

- `GET /health/live`: Process liveness check (200 OK `{"status":"Healthy"}`).
- `GET /health/ready`: Process readiness check (200 OK `{"status":"Healthy"}`).
- `GET /health`: Aggregates downstream dependencies (`userService`, `marketDataService`, `predictionService`, `portfolioService`, `sentimentService`, and optional `redis`). Returns `200 OK` when all critical dependencies are healthy, or `503 Service Unavailable` if a critical service is down. Timeouts (3s default) prevent hung requests.

---

## 6. Error Handling (RFC 7807)

All non-2xx responses follow RFC 7807 Problem Details:

```json
{
  "type": "https://finsightiq.io/errors/unauthenticated",
  "title": "Authentication failed",
  "status": 401,
  "code": "UNAUTHENTICATED",
  "detail": "Missing, invalid or unverified access token.",
  "instance": "/api/v1/market/AAPL/history",
  "traceId": "c928424177d54983b6f8f7ca5e46be98",
  "errors": null
}
```

Stable codes used:
- `UNAUTHENTICATED` (401)
- `TOKEN_EXPIRED` (401)
- `FORBIDDEN` (403)
- `RATE_LIMITED` (429)
- `QUOTA_EXCEEDED` (429)
- `UPSTREAM_UNAVAILABLE` (503)
- `PREDICTION_UNAVAILABLE` (503)
- `INTERNAL_ERROR` (500)

---

## 7. Local Development & Testing

### Prerequisites
- .NET 8.0 SDK (version `8.0.408` or higher)

### Build Solution
```bash
dotnet restore FinSightIQ.Gateway.sln
dotnet build FinSightIQ.Gateway.sln --configuration Release
```

### Run Tests
```bash
dotnet test FinSightIQ.Gateway.sln --configuration Release --verbosity normal
```

### Run Gateway Locally
```bash
dotnet run --project src/FinSightIQ.Gateway/FinSightIQ.Gateway.csproj
```
The gateway listens at `http://localhost:5000`.

---

## 8. Docker Build & Orchestration

### Build Container
```bash
docker build -t finsightiq-gateway .
```

When building with BuildKit secrets for private GitHub Packages access:
```bash
docker build --secret id=nuget_token,src=/path/to/token.txt -t finsightiq-gateway .
```

### Run in FinSightIQ Platform
Within `finsightiq-platform`:
```bash
docker compose up -d gateway
```
