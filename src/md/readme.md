# md

Minimal markdown build/test output for AI consumption.

## Install

```bash
dotnet tool install -g md
```

Or run without installing:

```bash
dnx md -y build
dnx md -y test
```

## Usage

```bash
# Build solution with minimal markdown output
dnx md -y build

# Build with forwarded dotnet args
dnx md -y build --configuration Release --no-restore

# Run tests with minimal markdown output
dnx md -y test

# Run tests for a specific project
dnx md -y test src/Tests/Tests.csproj --no-build
```

## Output examples

**Build success:**

```
✅[1]Api.dll
✅[1]Web.dll
✅[1]Tests.dll

[1]: Microsoft.Data.Ingestion.
```

**Build failure:**

```
❌md
	src/Api/Program.cs:12 CS1002: ; expected
```

**Build fallback** (non-zero exit, no parseable binlog output):

```
❌Build
```

**Test success:**

```
[1]Tests.dll ✅23 ⏩5
[1]IntegrationTests.dll ✅10

[1]: MyCompany.MyApp.
```

**Test failure:**

```
[1]Tests.dll ✅10 ❌2 ⏩1

❌[2]Fails
	Assert.True() Failure
	   at MyCompany.MyApp.Tests.UnitTests.Fails() in src/Tests/SampleTests.cs:line 10
❌[2]AlsoFails
	Expected 1
	   at MyCompany.MyApp.Tests.UnitTests.AlsoFails() in src/Tests/SampleTests.cs:line 11

[1]: MyCompany.MyApp.
[2]: MyCompany.MyApp.Tests.
```

**Test fallback** (non-zero exit, no parseable TRX output):

```
❌Tests
```

## How it works

- `build` injects a binary log (`-bl`) when none is provided, then parses it with [MSBuild.StructuredLogger](https://www.nuget.org/packages/MSBuild.StructuredLogger). No console text parsing.
- `test` injects `--logger trx` and a temp `--results-directory` when missing, then parses TRX files only.
- Help requests (`-h`, `-?`, `--help`) are passed through to `dotnet` unchanged.
- Informational dotnet switches (`--version`, `--list-tests`, etc.) replay captured stdout when no markdown is emitted.