# ADR-0020: Upgrade to .NET 11 (C# 15) and Migrate Test Assertions to Shouldly

## Status
**Accepted** (Date: 2026-11-16)

## Context
Carnot Cycle Circus previously targeted `.NET 10` (`10.0.302` SDK, `Microsoft.Extensions.*` / `Microsoft.AspNetCore.*` `10.0.12`) and used `FluentAssertions` (`8.x`) for test assertions:
1. **.NET 11 GA & Security Remediation**: `.NET 11.0` (`11.0.100` SDK, C# 15) launched on November 10, 2026. The pinned `10.0.12` `Microsoft.AspNetCore.SignalR.Client` dependency pulled in `Microsoft.AspNetCore.SignalR.Common` `10.0.12`, which triggered high-severity vulnerability `NU1903` (`GHSA-334f-vvp4-7g6p`) under `TreatWarningsAsErrors`.
2. **.NET 11 Package Pruning & Razor Analyzers**: .NET 11 moves core `Microsoft.Extensions.*` abstractions into the shared framework (raising `NU1510` on explicit `Microsoft.Extensions.Hosting.Abstractions` references) and enforces `BL0012` on redundant `StateHasChanged()` calls before `await` in Blazor event handlers.
3. **Test Assertion Licensing**: `FluentAssertions` 8.x emits an Xceed commercial-license warning during every test run and restricts commercial usage without a paid subscription, whereas `Shouldly` provides expressive, readable assertions under the permissive MIT license.

## Decision
1. **Upgrade Runtime & SDK to .NET 11 / C# 15**:
   - Pin `global.json` to `11.0.100`, target `net11.0` across `Directory.Build.props` and all solution projects, and update CI (`ci.yml`, `codeql.yml`) and `Dockerfile` to `.NET 11`.
   - Upgrade all `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` packages to `11.0.0`, remove redundant `Microsoft.Extensions.Hosting.Abstractions` (`NU1510`), and update test tooling (`Microsoft.NET.Test.Sdk` `18.12.0`, `xunit.runner.visualstudio` `4.0.1`, `coverlet.collector` `10.1.1`).
   - Remove redundant `StateHasChanged()` calls flagged by `BL0012` rather than suppressing analyzer diagnostics.
2. **Migrate Test Suite from FluentAssertions to Shouldly**:
   - Replace `FluentAssertions` with `Shouldly` (`4.3.0`) in `Directory.Packages.props` and `CarnotCycleCircus.Tests.csproj`.
   - Convert all test suites in `tests/CarnotCycleCircus.Tests/` to idiomatic `Shouldly` assertions (`ShouldBe`, `ShouldNotBe`, `ShouldContain`, `ShouldAllBe`, `ShouldHaveSingleItem`, `ShouldBeSubsetOf`, `Should.Throw<T>`, `Should.ThrowAsync<T>`, `Should.NotThrowAsync`).

## Alternatives Considered
- **Stay on .NET 10 LTS (`10.0.15`)**: Would patch `GHSA-334f-vvp4-7g6p`, but leaves the platform behind C# 15 and .NET 11 runtime/shared-framework improvements.
- **Keep FluentAssertions 8.x or downgrade to 7.x**: Retaining 8.x introduces commercial licensing friction for contributors and downstream users; pinning 7.x freezes the dependency on an unmaintained major branch.
- **Suppress `NU1510` / `BL0012` warnings**: Violates the repository's zero-warning, non-suppression quality standard.

## Consequences

### Positive
- ✅ Eliminates `NU1903` (`GHSA-334f-vvp4-7g6p`) and keeps all NuGet dependencies current with 0 outdated and 0 vulnerable packages.
- ✅ Eliminates FluentAssertions commercial-license friction while preserving readable, diagnostic-rich test assertions under MIT licensing.
- ✅ Cleaner Blazor components (`BL0012` compliant) and leaner `Core` dependencies via .NET 11 shared framework inclusion.

### Negative / Trade-offs
- ⚠️ Contributors and CI runners must have the `.NET 11 SDK` (`11.0.100+`) installed.
- ⚠️ .NET 11 is a Standard Term Support (STS) release (supported until November 14, 2028) rather than LTS.
