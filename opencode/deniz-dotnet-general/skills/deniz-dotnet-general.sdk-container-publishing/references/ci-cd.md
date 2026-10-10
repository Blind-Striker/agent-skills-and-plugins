---
name: sdk-container-publishing-ci-cd
description: Patterns for publishing SDK-built container images in CI / CD, including tag-driven release workflows, registry auth, and the explicit-tag footgun.
---

# CI / CD Container Publishing

## Tag-Driven Release Workflow

Publish images off a git tag. Tag the workflow with the release name, not `latest`, because since .NET 8 the SDK defaults the image tag to `latest` when no tag property is set.

```yaml
name: Publish Container Images

on:
  push:
    tags:
      - '*'

jobs:
  publish-containers:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Set up Docker daemon
        uses: docker/setup-docker-action@v4

      - name: Build and push
        run: |
          dotnet publish src/MyService/MyService.csproj \
            -p:VersionPrefix=${{ github.ref_name }} \
            -p:ContainerRegistry=docker.example.internal \
            -p:ContainerImageTag=${{ github.ref_name }} \
            -c Release /t:PublishContainer
```

Two things to keep in mind:
- **`ContainerImageTag` is explicit.** These `-p:` flags are MSBuild container properties passed on the command line; `ContainerImageTag` is what controls the produced image tag. Without it the image lands on `latest` and a release can never be pulled by version. If the project sets `ContainerImageTags`, drop this flag (see the `Directory.Build.props` notes below).
- **The push itself needs no daemon.** With `ContainerRegistry` set, the SDK pushes the image directly to the registry; only the default local output, with neither `ContainerRegistry` nor `ContainerArchiveOutputPath` set, needs a Docker or Podman daemon. Keep the daemon step only if the job runs other `docker` commands.

## Registry Auth

The SDK reads Docker / Podman config to decide HTTP vs HTTPS and to authenticate. In CI, log into the registry first, passing the token on standard input so it stays out of the shell history and logs:

```bash
echo "$REGISTRY_TOKEN" | docker login <registry> -u <user> --password-stdin
```

In GitHub Actions, map the secret into the step's `env` (`REGISTRY_TOKEN: ${{ secrets.REGISTRY_TOKEN }}`) instead of writing it into the command. Without `docker login`, set `DOTNET_CONTAINER_REGISTRY_UNAME` and `DOTNET_CONTAINER_REGISTRY_PWORD` for the publish step (SDK 8.0.400 and later; older SDKs read the `SDK_CONTAINER_REGISTRY_` prefix).

For insecure (HTTP) registries, set `DOTNET_CONTAINER_INSECURE_REGISTRIES` to a comma-separated list of domains. See the [registry authentication docs](https://aka.ms/dotnet/containers/auth).

## Centralizing Settings in Directory.Build.props

When a repo has many container projects, centralize the image settings with CI conditionals. This is the pattern we use for version tags, labels, and parallel-build safety:

```xml
<PropertyGroup Label="ContainerSettings" Condition=" '$(CI)' == 'true' ">
  <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  <ContainerImageTags>github-$(GITHUB_RUN_NUMBER);latest;$(VersionPrefix)</ContainerImageTags>
  <ContainerVendor>$(GITHUB_REPOSITORY_OWNER)</ContainerVendor>
  <ContainerVersion>$(GITHUB_SHA)</ContainerVersion>
</PropertyGroup>

<PropertyGroup>
  <!-- Multi-RID publishes: build the per-RID images one after another -->
  <ContainerPublishInParallel>false</ContainerPublishInParallel>
</PropertyGroup>

<ItemGroup Condition=" '$(CI)' == 'true' ">
  <ContainerLabel Include="com.example.commit" Value="$(GITHUB_SERVER_URL)/$(GITHUB_REPOSITORY)/commit/$(GITHUB_SHA)" />
</ItemGroup>
```

Key points:
- `ContainerImageTags` dual or triple tags: floating `latest`, the version, and a run number for CI.
- `ContainerPublishInParallel=false` builds the per-RID images of a multi-architecture publish one after another, for example against Blazor WebAssembly build races. It does not serialize publishes across projects.
- This block sets `ContainerImageTags` under CI, so the publish step must not also pass `-p:ContainerImageTag`: setting both fails with `CONTAINER2008`. Drop that flag from the workflow above and let `-p:VersionPrefix` feed the `$(VersionPrefix)` tag.
- `ContainerVendor` / `ContainerVersion` become OCI labels from CI values.

## Tarball Output for Scanning or Air-Gapped Transfer

If the build host has no daemon or you need a portable file, write a tarball and load it elsewhere:

```bash
dotnet publish /t:PublishContainer -c Release \
  -p:ContainerArchiveOutputPath=./images/my-service.tar.gz

# Elsewhere, no daemon needed on the build host:
docker load -i ./images/my-service.tar.gz   # or podman load -i
```

This fits security scanning and air-gapped loading workflows.

## Multi-Architecture and Multi-Image Builds

Building for more than one OS / arch (for example `linux-x64`, `linux-arm64` for Apple Silicon) needs no special CI setup beyond the container properties. Set `ContainerRuntimeIdentifiers` to a semicolon-delimited list of RIDs that is a subset of `RuntimeIdentifiers`, and the SDK emits a single multi-arch OCI image index:

```bash
dotnet publish src/MyService/MyService.csproj \
  -p:RuntimeIdentifiers='"linux-x64;linux-arm64"' \
  -p:ContainerRuntimeIdentifiers='"linux-x64;linux-arm64"' \
  /t:PublishContainer
```

Key points:
- Quote each semicolon-delimited value as shown. Unquoted, Bash ends the command at the `;`, and MSBuild splits a `-p:` value at `;` and fails with `MSB1006`.
- The RIDs must be a subset of `RuntimeIdentifiers`, or the publish fails.
- Multi-arch output is always an OCI image index (Docker format is unavailable for it).
- Multi-arch support requires SDK 8.0.405, 9.0.102, or 9.0.2xx or later.
- If you instead need several *separate* images (for example an API and an MCP server in one release), keep them as distinct publish steps or projects with their own `ContainerRepository` — the SDK builds one image per project per publish invocation.

See [references/property-reference.md](property-reference.md) for the full `ContainerRuntimeIdentifiers` details.
