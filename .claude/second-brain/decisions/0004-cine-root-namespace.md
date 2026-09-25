---
tags: [decision, dotnet, conventions]
created: 2026-09-25
updated: 2026-09-25
status: active
supersedes:
---
# 0004: CINE root namespace for all .NET code

**Context.** The owner wants every application and library in the monorepo under one root namespace,
`CINE` (so `GroceryTracker.Domain` becomes `CINE.GroceryTracker.Domain`).

**Decision.** Rename namespaces only. Each app's `Directory.Build.props` sets
`<RootNamespace>CINE.$(MSBuildProjectName)</RootNamespace>`. Project, assembly and folder names keep no
prefix. For GroceryTracker that file is `Applications/GroceryTracker/Directory.Build.props`.

**Alternatives.**
- *A repo-root `Directory.Build.props`.* Rejected: the container builds use the app directory as
  their context, so they can't see a file above it. Razor then puts components in the unprefixed
  namespace and `_Imports.razor` fails to compile.
- *Also rename the assemblies and csproj files.* Rejected for now: it would touch the Dockerfiles, the
  systemd unit (`GroceryTracker.Api.dll`), `InternalsVisibleTo` and the deploy runbooks for no
  functional gain.

**Consequences.**
- Every Dockerfile must `COPY Directory.Build.props ./` before it builds, or Razor namespaces break.
- New apps need their own `Directory.Build.props` with the same property.
- The EF migration designers and the model snapshot reference entity types by full name. They were
  rewritten to `CINE.*` together with the code. The migration IDs, which are what
  `__EFMigrationsHistory` stores, didn't change, so existing databases are unaffected.

Related: [[grocerytracker]]
