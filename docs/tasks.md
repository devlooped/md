# Task checklist

Status as of 2026-06-09 handoff. See [progress.md](progress.md) for detail.

## Plan implementation tasks

| ID | Task | Status | Notes |
|----|------|--------|-------|
| `configure-csproj` | net10 Exe, PackAsTool, dependencies, icon | **done** | `src/md/md.csproj` |
| `cli-help-passthrough` | System.CommandLine, `--version`, help passthrough | **done** | Pre-scan in `Program.cs`; `HelpOption` removed from subcommands |
| `shared-infra` | DotnetMuxer, DotnetRunner, NameShortener, MarkdownWriter | **done** | Plus `DotnetBuildArguments`, `DotnetTestArguments`, `CommandOutput` |
| `binlog-reader` | GetTargetPath / TargetOutputs + Error nodes | **done** | `src/md/Parsing/BinlogReader.cs` |
| `trx-reader` | TRX counts + failures with NameShortener | **done** | `src/md/Parsing/TrxReader.cs` |
| `unit-tests` | Parser/formatter tests + fixtures | **mostly done** | 9 test classes; some edge cases remain (see [reviews.md](reviews.md)) |
| `agents-md` | AGENTS.md + readme updates | **done** | `copilot-instructions.md` is a pointer |

## `/implement` orchestration tasks

| ID | Phase | Status | Notes |
|----|-------|--------|-------|
| `setup` | Memory + reviewer config (effort 3) | **done** | general + tests + plan reviewers |
| `implement` | Round 1 implementation | **done** | Subagent `019ead99-8fa3-75d1-bd03-c2a804c44359` |
| `review-round-1` | 3 parallel reviewers | **done** | 26 issues found |
| `fix-round-1` | Address round 1 | **done** | All 26 marked fixed |
| `rereview-round-1` | Round 2 review | **done** | 8 new issues (3 bugs) + test/plan nits |
| `fix-round-2` | Address round 2 | **in progress** | Partial; test fixes applied locally, full verify pending |
| `rereview-round-2` | Confirm 0 open issues | **pending** | |
| `memory-flush` | `/implement` memory update | **blocked** | `memory.py` needs `fcntl` (unavailable on Windows) |
| `final-report` | Completion summary | **pending** | |

## Next actions (priority)

1. Run `dotnet test md.slnx` and fix any failures (see [reviews.md](reviews.md) — known flaky areas: `MarkdownWriterTests` line endings, `CliIntegrationTests` build prereq).
2. Complete round 2 fix pass if re-review still finds open issues.
3. Run round 2 re-review until 0 open issues.
4. Optionally dogfood: `dnx md -y test` in CI later (explicitly out of scope for plan).