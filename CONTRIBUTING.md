# Contributing to InfraWatch

## Ground rules

1. **Read-only first.** Default to observe-only. Any code that writes to monitored
   infrastructure (canary files, etc.) must be opt-in, off by default, and clearly
   scoped. Call it out explicitly in the PR.
2. **No secrets in the repo.** Credentials, connection strings, API tokens, certs — none
   of it goes in source or committed config. Use user-secrets / a secrets manager /
   environment. `.gitignore` blocks the obvious cases; don't defeat it.
3. **Least privilege.** Collectors should ask for the narrowest access that does the job.
   Document the permissions a collector needs in that collector's `README.md`.
4. **Core stays dependency-free and I/O-free.** Domain models and interfaces only. I/O
   and third-party SDKs live in collectors, storage, alerting, etc.
5. **Normalize at the edge.** A collector's job is to turn pillar-specific reality into
   the shared `HealthRecord` / `InventoryRecord` types. The engine, dashboard, and docs
   renderer must never special-case a pillar.

## Project layout

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the project map, dependency rules,
and data flow. Each `src/` project has a `README.md` describing its responsibility.

## Workflow

- Branch off `main`; open a PR. Keep changes scoped to one concern.
- `dotnet build` and `dotnet test` must pass before review.
- Follow [`.editorconfig`](.editorconfig) (file-scoped namespaces, 4-space C# indent).
- New collector? Add it under `src/Collectors/`, implement `ICollector`, document its
  required access in its `README.md`, and register it in `InfraWatch.Service`.

## Adding a collector (checklist)

- [ ] New `classlib` project `InfraWatch.Collectors.<Pillar>` referencing only
      `InfraWatch.Core` (+ the pillar's access SDK).
- [ ] Implements `ICollector`, emitting normalized `HealthRecord` + `InventoryRecord`.
- [ ] `README.md` lists the exact privileges/service-account rights it requires.
- [ ] Degrades gracefully when access is missing (disabled, not crashing).
- [ ] Tests in `tests/InfraWatch.Tests`.
