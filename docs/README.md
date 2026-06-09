# md — implementation docs

Portable handoff for resuming work on the `md` global dotnet tool (minimal markdown `build` / `test` wrappers).

## Start here

| Doc | Purpose |
|-----|---------|
| [plan.md](plan.md) | Approved design and output contract |
| [tasks.md](tasks.md) | Plan task checklist with completion status |
| [progress.md](progress.md) | What is implemented, file inventory, verification |
| [reviews.md](reviews.md) | Code-review rounds, findings, and pending items |
| [session.md](session.md) | `/implement` session metadata for Grok resume |

## Quick resume

```bash
cd <repo-root>
dotnet build md.slnx
dotnet test md.slnx
```

Dogfood the tool locally:

```bash
dotnet run --project src/md/md.csproj -- build md.slnx
dotnet run --project src/md/md.csproj -- test src/Tests/Tests.csproj --no-build
```

Or after pack/install:

```bash
dnx md -y build
dnx md -y test
```

## Reference project

Architecture and TRX patterns were adapted from [dotnet-retest](https://github.com/devlooped/dotnet-retest) at `C:\Code\dotnet-retest` (local path on original machine).

## Last updated

2026-06-09 — `/implement` effort 3, round 2 in progress (tests may need re-verification after handoff).