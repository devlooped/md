# md — Agent Instructions

**Always reference these instructions first.**

## Working Effectively

### Essential Commands

- **Restore dependencies**: `dotnet restore`
- **Build the solution**: `dnx md -y build`
- **Run tests**: `dnx md -y test`

The `md` tool wraps `dotnet build` / `dotnet test`, swallows verbose SDK output, and emits token-minimal markdown for AI consumption.

### Build Validation

- **Always run before committing**:
  - `dnx md -y test`
  - `dotnet format whitespace -v:diag --exclude ~/.nuget`
  - `dotnet format style -v:diag --exclude ~/.nuget`

### Project Structure

| Directory | Description |
|-----------|-------------|
| `src/md/` | Global tool source (`build` and `test` subcommands) |
| `src/Tests/` | xUnit tests and fixtures (binlog, trx) |
| `bin/` | Built NuGet packages |

### Architecture

- **CLI**: System.CommandLine with `build` and `test` subcommands
- **Build**: injects `-bl:<temp.binlog>` if absent; parses via MSBuild.StructuredLogger `Serialization.Read`
- **Build success**: `Project` → `GetTargetPath` (Succeeded) → `TargetOutputs` items → filenames
- **Build failure**: `Error` nodes grouped by project; `❌[n]Project` + blockquoted errors (`file:line CODE: message`)
- **Build fallback**: `❌Build` when non-zero exit and no parseable binlog output
- **Test**: injects `--logger trx` + `--results-directory <temp>` if missing; TRX only (combined loggers allowed when trx present)
- **Test success**: per-assembly counts with ✅/❌/⏩ (omit zero counts)
- **Test failure**: summary lines + `❌[n]` shortened full test FQNs with fenced stack traces
- **Test fallback**: `❌Tests` when non-zero exit and no parseable TRX output
- **NameShortener**: shared prefix on full names → `[n]Suffix` lines + `[n]: Prefix.` footer
- **Passthrough**: informational dotnet output replayed when no markdown is written; help forwarded before capture

### Invocation

```
dnx md -y build [dotnet build args...]
dnx md -y test  [dotnet test args...]
```

- Forwards all remaining args to `dotnet`; no `--` separator
- Only handles `--version` on root (`md --version`); `md build --version` forwards to dotnet
- `-?`/`-h`/`--help` passthrough to underlying dotnet command without output capture

### Code Style

Follow `.editorconfig` at repo root: 4-space C# indentation, LF line endings, `var` when apparent, language keywords over framework types.

### CI/CD

- **Build workflow**: `.github/workflows/build.yml` — do not modify unless asked
- Tests must pass on net10.0