---
name: sdk-container-publishing
description: "Publish .NET services as container images with the built-in SDK
  tooling (dotnet publish /t:PublishContainer, Microsoft.NET.Build.Containers) -
  no Dockerfile required. Covers the MSBuild property surface, base-image
  inference, self-contained and AOT implications, and CI/CD publishing. USE FOR:
  configuring or troubleshooting SDK-built container images and their CI publish
  step. DO NOT USE FOR: Dockerfile builds, deploying an Aspire AppHost
  (aspire-deployment), running containers in tests
  (testcontainers-integration-tests), or turning on PublishAot in an existing
  project."
invocable: false
user-invocable: false
---

# .NET SDK Container Publishing

## When to Use This Skill

Use this skill when:
- You want a container image for a .NET service and do not want to hand-write a Dockerfile
- You want to know how to tag, name, or expose ports on an SDK-built image without a Dockerfile
- You are wiring container publishing into CI / CD or a release pipeline
- An SDK-built image pushes to the wrong registry, gets the wrong tag, or picks the wrong base image
- You need a tarball of an image for scanning or air-gapped loading

Do not use it when:
- You need a `RUN` step or arbitrary layer customization in the image. The SDK tooling cannot emulate `RUN`; write a Dockerfile instead.

Push to a registry, run `docker login`, or edit CI configuration only when the user asked for it in this
task. To verify a change locally, publish to the local daemon or write a tarball with
`ContainerArchiveOutputPath`.

## What This Is

Since .NET 7, `dotnet publish` builds a container image natively. The `Microsoft.NET.Build.Containers` tooling ships with the SDK, so no Dockerfile is required and no Docker install is needed to produce an image. Docker or Podman is only required to run the image locally. This is documented in the [containerize with dotnet publish tutorial](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish).

The quickest path is one command:

```bash
dotnet publish /t:PublishContainer -c Release
```

That compiles the app and emits an image in one step. The image name defaults to the project's `AssemblyName`. Override it with `ContainerRepository` (renamed from `ContainerImageName` in earlier versions - see the [sdk-container-builds customization notes](https://github.com/dotnet/sdk-container-builds/blob/main/docs/ContainerCustomization.md)).

## The Three Output Modes

The tooling writes the image in one of three ways. Pick based on where the image ends up.

| Mode | When to use |
|------|-------------|
| Local daemon (default) | Local dev. Pushes to the running Docker or Podman daemon with no extra config. |
| Tarball | You want a file to scan, transfer, or load later. Set `ContainerArchiveOutputPath` to a `.tar.gz` path, then `docker load -i` or `podman load -i`. Useful in security scanning workflows. |
| Registry push | Shipping. Set `ContainerRegistry` (for example `ghcr.io` or an internal registry) and the image is pushed directly. |

See the [containerize a .NET app reference](https://learn.microsoft.com/en-us/dotnet/core/containers/publish-configuration) for the full property surface.

## Key Facts

- **No Docker required to build.** The SDK creates the image itself; a runtime is only needed to run it. See the [SDK publish tutorial](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish).
- **Image name defaults to `AssemblyName`.** Set `ContainerRepository` to override.
- **Base image is inferred.** Self-contained projects get `mcr.microsoft.com/dotnet/runtime-deps`, ASP.NET Core gets `dotnet/aspnet`, other apps get `dotnet/runtime`, tagged for the TFM. Since SDK 8.0.200 the inference is size and security aware: musl RIDs pick Alpine variants, `PublishAot=true` picks the chiseled AOT runtime-deps variant. See the [base image inference notes](https://learn.microsoft.com/en-us/dotnet/core/containers/publish-configuration).
- **Since .NET 8 the SDK tags images `latest`, not the version,** when neither `ContainerImageTag` nor `ContainerImageTags` is set. If your publish relies on a version tag, set one of the two explicitly, never both: setting both fails the publish with `CONTAINER2008`. This is a real footgun in CI - see the [tag drift entry in references/troubleshooting.md](references/troubleshooting.md).
- **Windows images need an explicit base image.** Microsoft no longer includes Windows variants in the manifest list. To target Windows, set `ContainerBaseImage` to a specific nanoserver tag (for example `mcr.microsoft.com/dotnet/aspnet:8.0-nanoserver-ltsc2022`). See the [Windows note](https://learn.microsoft.com/en-us/dotnet/core/containers/publish-configuration).
- **Multi-architecture images** come from `ContainerRuntimeIdentifiers` (semicolon-delimited, a subset of `RuntimeIdentifiers`). The output is an OCI image index. Supported from SDK 8.0.405, 9.0.102, and 9.0.2xx onward.
- **Insecure registries** are passed via the `DOTNET_CONTAINER_INSECURE_REGISTRIES` env var (comma-separated) starting in SDK 9.0.100. Since .NET 8.0.400 the SDK reads standard Docker / Podman config to decide HTTP vs HTTPS.

## The Minimal Property Set (the pattern we use)

Most of our services only override what they need and let the SDK infer the rest. A minimal csproj looks like this:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ContainerRepository>my-service</ContainerRepository>
    <ContainerImageTags>$(VersionPrefix);latest</ContainerImageTags>
  </PropertyGroup>

  <ItemGroup>
    <ContainerPort Include="8080" Type="tcp" />
  </ItemGroup>
</Project>
```

Notes:
- No `ContainerRegistry`, no `ContainerBaseImage`, no `ContainerUser`. The SDK defaults these correctly for most ASP.NET services.
- `ContainerPort` items expose ports without a Dockerfile.
- The version tag needs a `VersionPrefix` set before `ContainerImageTags` is evaluated: on the command line (`-p:VersionPrefix=...`, as in the CI command below), in `Directory.Build.props`, or earlier in this file. The SDK sets its `1.0.0` default only after the project body, so a publish without one tags the image `latest` only.

For the full property catalog, see [references/property-reference.md](references/property-reference.md).

## CI / CD Publishing

In CI, publish against a registry and tag with the release name. The tag-driven pattern we use:

```bash
dotnet publish src/MyService/MyService.csproj \
  -p:VersionPrefix=${{ github.ref_name }} \
  -p:ContainerRegistry=docker.example.internal \
  -c Release /t:PublishContainer
```

The minimal project above sets `ContainerImageTags` to `$(VersionPrefix);latest`, so `-p:VersionPrefix` puts the release name in the tag list and the image gets both tags. Do not also pass `-p:ContainerImageTag`: setting both `ContainerImageTag` and `ContainerImageTags` fails the publish with `CONTAINER2008`. For a project that sets neither tag property, pass `-p:ContainerImageTag=${{ github.ref_name }}` instead, because the SDK otherwise tags the image `latest`. See [references/ci-cd.md](references/ci-cd.md) for the full workflow and registry auth notes.

If you centralize image settings across a repo, put them in a `Directory.Build.props` with CI conditionals. We do this for version tags and labels. See [references/ci-cd.md](references/ci-cd.md).

## Troubleshooting

The common failures (tag drift, wrong image name or registry, unavailable `RUN`, no daemon on the build host, insecure registry rejection, multi-architecture publish races, and multi-target publishing) are covered in depth in [references/troubleshooting.md](references/troubleshooting.md). The short version is: set one tag property explicitly, set `ContainerRepository` and `ContainerRegistry` explicitly, and use `ContainerArchiveOutputPath` when no daemon is available.

## Self-Contained and AOT

Self-contained projects default to the `runtime-deps` base image. AOT projects get a chiseled AOT runtime-deps variant for smaller, more secure images. See [references/self-contained-and-aot.md](references/self-contained-and-aot.md).

This skill does not turn `PublishAot` on in an existing project. To make an existing project
AOT-compatible, ask the user to invoke `/deniz-dotnet-general:dotnet-aot-compat`.

## Verification

Confirm the image built and exists:

```bash
# Image in the local daemon
docker images | grep <repository>

# Tarball produced
ls -la <ContainerArchiveOutputPath>

# Push succeeded (registry mode)
docker pull <ContainerRegistry>/<ContainerRepository>:<tag>
```

## Related Skills

- `deniz-dotnet-general:testcontainers-integration-tests` - running containers in tests
- `deniz-dotnet-general:aot-trimming` - AOT and trimming, relevant to base-image selection
