# Decision Log

Append-only. A changed decision gets a new entry; the old one is only marked `Superseded` with a forward reference.

## DEC-001 - MCP server is a separate, optional service that calls the API

**Date:** 2026-10-01
**Status:** Decided

Added `Klods.Mcp`, its own project, image, and compose service under the `mcp` profile. It holds no database or storage credentials; every tool call is an HTTP request to `Klods.Api` carrying the caller's own key.

**Why:** Self-hosters who don't want agent access simply don't run the container. Going through the API reuses the validation, ownership scoping, and DoS caps that live in the endpoint handlers, and keeps a single owner of the schema and migrations — the same model `Klods.Web` already follows.

**Alternatives considered:** `MapMcp()` inside `Klods.Api` behind a flag - rejected, the feature should be absent from a deployment rather than switched off. MCP referencing `Klods.Core` and querying Postgres directly - rejected, it would duplicate endpoint logic and need DB credentials. A separate repository - rejected, one clone should build everything; the project is simply left out of a deployment.

**Consequences:** Each tool call costs one API round-trip. The API owns the security boundary; the MCP server's own key check is only for early 401s.

## DEC-002 - Per-user static bearer keys instead of OAuth

**Date:** 2026-10-01
**Status:** Decided

Users create `klods_…` keys (256-bit random, stored as SHA-256) on their profile. Keys never expire; the owner or an admin revokes them, which deletes the row. An admin setting (`mcp.enabled`, off by default) gates creation and use; turning it off rejects every key without deleting any.

**Why:** Ties every agent action to a real user without building an OAuth authorization server. Expiring keys were rejected by the owner as friction; revocation and the site toggle cover the compromise case.

**Alternatives considered:** OAuth 2.1 per the MCP authorization spec - deferred, Klods is only an OAuth *client* today; revisit if a must-have client (e.g. claude.ai custom connectors) refuses header-based auth. Expiring keys - rejected by the owner. Per-user grant toggles - rejected, one site-wide toggle is enough. Soft-delete with `RevokedAt` - rejected, nothing reads revoked keys.

**Consequences:** Clients that only speak OAuth need a header-injecting proxy such as `mcp-remote`.

## DEC-003 - Keys are deny-by-default, never admin, and never reach Rebrickable

**Date:** 2026-10-01
**Status:** Decided

The default authorization policy stays JWT-only; endpoints opt in to keys with `.AllowApiKey()`. Key principals always carry role `User`. The allowlist is exactly the endpoints the MCP tools use: no admin, account, export, or Rebrickable-backed endpoints (`sets/resolve`, `sets/import`, `minifigs/import`, `POST minifigs/owned`, `bricks/resolve`). `POST bricks/owned` is also excluded because it creates shared catalog rows from caller-supplied data, and `bulk-bricks` because one call rewrites a whole set copy.

**Why:** A new endpoint can't accidentally become agent-reachable, and an agent can't spend the shared Rebrickable quota or act with admin rights even on an admin's key.

**Alternatives considered:** Deny-list on a default policy that accepts keys - rejected, fails open for new endpoints. Admin tools behind extra gates - deferred by the owner; revisit with an explicit tool list, audit trail, and separate admin-scoped keys. Rebrickable-backed import tools - deferred by the owner; imports stay manual.

**Consequences:** Adding a tool means adding `.AllowApiKey()` to its endpoint. The three catalog-dependent writes now return 404 for unknown catalog items instead of a 500/400.

## DEC-004 - Key rate limits are per user, in memory, and configurable

**Date:** 2026-10-01
**Status:** Decided

Key requests get fixed one-minute windows partitioned by user: reads (`GET`, default 60) and writes (default 30), overridable via `MCP_RATE_READS_PER_MIN` / `MCP_RATE_WRITES_PER_MIN`. JWT (browser) traffic on the same endpoints is unlimited. Rejections return 429 with `Retry-After`; `/api/auth/whoami` is exempt so a spent budget never looks like a bad key.

**Why:** Per-key partitions would let a user multiply their budget by minting keys, and every MCP call arrives from the MCP container's IP, so IP partitions would pool all users. In-memory matches the existing `auth` limiter and the single-replica deployment.

**Alternatives considered:** Durable daily quotas in Postgres - deferred; revisit if the API runs multiple replicas or restarts are used to dodge limits. Limiting inside the MCP server - rejected, the API is where identity and cost are.

**Consequences:** Counters reset when the API restarts. `UseRateLimiter` now runs after authorization so the limiter sees the key's user.

## DEC-005 - Replace MinIO with RustFS for the image cache

**Date:** 2026-10-01
**Status:** Decided

The bundled object store is now `rustfs/rustfs` on a fresh volume. The app keeps the MinIO .NET client and the `MINIO_*` variable names; compose maps the credentials onto RustFS and points `MINIO_ENDPOINT` at the `rustfs` service.

**Why:** MinIO's images can no longer be pulled (Docker Hub repo archived and refusing pulls, quay.io no longer anonymous), which broke CI and fresh installs. RustFS is maintained, Apache-2.0, reached 1.0.0 GA on 2026-09-16, and works with the existing client unchanged (bucket creation, public-read policy, put/get). The store only holds read-through copies of Rebrickable images, so starting empty costs only re-downloads.

**Alternatives considered:** Mirroring the last MinIO image to our GHCR - rejected, frozen with no security fixes. Garage or SeaweedFS - not needed once RustFS worked unchanged; fallbacks if RustFS stalls. Renaming the variables to `S3_*` - deferred, it would break every existing `.env` for no functional gain. Switching to `AWSSDK.S3` - deferred until the MinIO .NET client stops working.

**Consequences:** Upgrading installs run `up -d --remove-orphans` to drop the old `minio` container, and can delete the old MinIO volume afterwards. `rustfs/rustfs:latest` follows upstream stable releases.

## DEC-006 - Other users' collections are read through user-scoped GET routes; writes stay token-scoped

**Date:** 2026-10-01
**Status:** Decided

Any signed-in user can browse another Active user's sets, parts lists, bricks, and minifigs. The API exposes this as GET-only routes under `/api/users/{userId}/…` that call the same query methods as the caller's own `my` routes, with the user id taken from the route instead of the token. Unknown and Pending users return 404. Location and notes (set copies, loose bricks, substitutions) are never returned on these routes. The web app reuses the My Sets / My Bricks / My Minifigs pages and their detail dialogs with an `OwnerId` parameter that switches them to a read-only mode, reached from the Users page. These routes and `GET /api/users` accept MCP keys, with read-only `list_users` / `list_user_*` tools, extending the DEC-003 allowlist.

**Why:** Every write endpoint already takes the owner from the token, so read-only access to others needs no new authorization logic — there is simply no route that writes to someone else's data. Sharing one query method per read keeps the owner's view and the visitor's view from drifting. The owner chose to keep locations and notes private for now, and wants agents able to compare inventories across users.

**Alternatives considered:** Separate read-only copies of the three pages - rejected, ~1,400 duplicated lines of filter/sort/paging that would drift. A `?userId=` parameter on the existing `my` routes - rejected, it mixes "whose data" into endpoints that also write and makes a missed check fail open. Exposing locations and notes - deferred by the owner; revisit if users ask to share them (likely as a per-user opt-in). Per-user privacy opt-out - not requested; every Active user's collection is visible to every signed-in user.

**Consequences:** In the UI, a write control left visible while viewing someone else would act on the viewer's own data, so read-only mode must hide every write affordance. A new read added to a `my` route needs its user-route twin added deliberately; nothing generates it.
