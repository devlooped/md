# md

[![Version](https://img.shields.io/nuget/vpre/md.svg?color=royalblue)](https://www.nuget.org/packages/md)
[![Downloads](https://img.shields.io/nuget/dt/md.svg?color=darkmagenta)](https://www.nuget.org/packages/md)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](https://github.com/devlooped/oss/blob/main/osmfeula.txt)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/devlooped/oss/blob/main/license.txt)

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
<!-- #content -->
## Usage

`md` wraps `dotnet build` and `dotnet test`, swallowing verbose SDK output and emitting token-minimal markdown for AI agents.

```bash
dnx md -y build
dnx md -y test
dnx md -y build --configuration Release
dnx md -y test --no-build
```

When parsing yields no markdown, `md` emits minimal fallbacks (`❌Build` / `❌Tests`) on failure, or replays captured dotnet stdout for informational switches like `--version`.

See [src/md/readme.md](src/md/readme.md) for output examples.
<!-- #content -->
---
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->