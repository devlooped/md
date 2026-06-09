# Implementation progress

## Summary

The `md` global dotnet tool is **substantially implemented**. Core `build` and `test` commands work with binlog/TRX parsing and minimal markdown output. Round 1 review findings were addressed. Round 2 fixes are **mostly in code** but the `/implement` loop was interrupted before full test verification and re-review.

## Tool source (`src/md/`)

| File | Status | Description |
|------|--------|-------------|
| `Program.cs` | done | Root `--version`; help pre-scan before SCL; custom `WriteRootHelp()` |
| `BuildCommand.cs` | done | Binlog inject/parse; markdown on success/failure; `CommandOutput.Finish` |
| `TestCommand.cs` | done | TRX inject/parse; non-trx logger rejection |
| `HelpDetector.cs` | done | Case-insensitive subcommand + help flag detection |
| `DotnetMuxer.cs` | done | Copied from dotnet-retest |
| `DotnetRunner.cs` | done | `RunCapturedAsync`, `RunPassthroughAsync`, `ReplayCapturedOutput` |
| `DotnetBuildArguments.cs` | done | `-bl` path resolution; tool-created flag |
| `DotnetTestArguments.cs` | done | TRX/results-directory injection; combined logger allowed when trx present |
| `CommandOutput.cs` | done | Replay stdout on success w/o markdown; fallback on failure |
| `Formatting/NameShortener.cs` | done | Shared prefix → `[n]Suffix` + footer |
| `Formatting/MarkdownWriter.cs` | done | Build/test success/failure + fenced stacks |
| `Parsing/BinlogReader.cs` | done | `Serialization.Read`; Project→GetTargetPath→TargetOutputs; Error nodes |
| `Parsing/TrxReader.cs` | done | TRX aggregation; full FQN failures |
| `md.csproj` | done | net10, PackAsTool, ToolCommandName=md, icon.png |
| `readme.md` | done | Usage + output examples (incl. fallbacks) |

## Tests (`src/Tests/`)

| File | Status |
|------|--------|
| `NameShortenerTests.cs` | done |
| `BinlogReaderTests.cs` | done (success + failure fixtures) |
| `TrxReaderTests.cs` | done (basic; edge cases pending) |
| `HelpDetectorTests.cs` | done |
| `MarkdownWriterTests.cs` | done (8 tests; line-ending fixes applied at handoff) |
| `DotnetBuildArgumentsTests.cs` | done |
| `DotnetTestArgumentsTests.cs` | done |
| `CommandOutputTests.cs` | done |
| `CliIntegrationTests.cs` | done (5 tests; build prereq added at handoff) |

### Fixtures

- `Fixtures/build-success.binlog`
- `Fixtures/build-failure.binlog`
- `Fixtures/trx/sample.trx`

## Documentation

| File | Status |
|------|--------|
| `AGENTS.md` | done — architecture, `dnx md -y` commands, fallbacks |
| `readme.md` (root) | done — overview |
| `src/md/readme.md` | done — detailed examples |
| `.github/copilot-instructions.md` | done — pointer to AGENTS.md |

## Verification (last known)

| Command | Result |
|---------|--------|
| Round 1 `dotnet test` | 35 passed (after round 1 fixes) |
| Round 2 partial run | 3 failures: `MarkdownWriterTests` (2), `CliIntegrationTests` (1) |
| Post-handoff fixes | Line-ending + `[2]` failure ref + CliIntegration build prereq — **not re-run** |

Run on resume:

```bash
dotnet build md.slnx
dotnet test md.slnx -v:n
```

Manual smoke tests:

```bash
src/md/bin/Debug/net10.0/md.exe build --help
src/md/bin/Debug/net10.0/md.exe TEST --help
src/md/bin/Debug/net10.0/md.exe build --version
src/md/bin/Debug/net10.0/md.exe test src/Tests/Tests.csproj --no-build
```

## Design notes (post-implementation)

- **Failure ref indices**: Assembly names and test FQNs use separate `ShortenMany` calls on the same `NameShortener` instance, so assemblies get `[1]` and test namespaces may get `[2]` when prefixes differ (matches plan examples).
- **Fallbacks**: `❌Build` / `❌Tests` emitted when exit ≠ 0 and parsing yields nothing — documented in readme/AGENTS (extension beyond original plan contract).
- **Informational replay**: `CommandOutput.Finish` replays captured dotnet stdout/stderr when exit 0 and no markdown written (e.g. `md build --version`).