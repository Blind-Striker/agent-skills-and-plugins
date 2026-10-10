---
name: sdk-container-publishing-troubleshooting
description: Common failures when publishing SDK-built container images and how to fix them.
---

# Troubleshooting SDK Container Publishing

## Tag Drift

**Symptom:** Your pipeline publishes an image but you cannot find the version you expect, only `latest`.

**Cause:** Since .NET 8 the SDK defaults the image tag to `latest`, not to `$(Version)`, when neither `ContainerImageTag` nor `ContainerImageTags` is set. `VersionPrefix` stamps the assembly version but not the image tag.

**Fix:** Pass `ContainerImageTag` explicitly in the publish step:

```bash
dotnet publish ... -p:ContainerImageTag=${{ github.ref_name }} /t:PublishContainer
```

If the project already sets `ContainerImageTags`, do not pass `ContainerImageTag` as well: setting both fails with `CONTAINER2008`. Override the list with `-p:ContainerImageTags=<tag>` instead, or let a `$(VersionPrefix)` entry in the list pick up `-p:VersionPrefix`.

## Wrong Image Name or Registry

**Symptom:** The image pushes somewhere unexpected.

**Cause:** The SDK derives the repository name from `AssemblyName` and pushes to the local daemon by default.

**Fix:** Set `ContainerRepository` and `ContainerRegistry` explicitly.

## RUN Unavailable

**Symptom:** You need to run a command inside the image and the SDK will not do it.

**Cause:** The SDK tooling cannot execute `RUN` steps without a Dockerfile.

**Fix:** Use a custom base image via `ContainerBaseImage`, or write a Dockerfile.

## No Daemon on the Build Host

**Symptom:** Publishing fails because no Docker / Podman daemon is reachable.

**Cause:** The default output mode pushes to a local daemon, which must be present.

**Fix:** Set `ContainerArchiveOutputPath` to write a tarball instead. No daemon needed.

## Property Name Confusion

**Symptom:** You used `ContainerImageName` and it does nothing.

**Cause:** `ContainerImageName` was the early name. It was renamed to `ContainerRepository`.

**Fix:** Use `ContainerRepository`. See the [sdk-container-builds customization notes](https://github.com/dotnet/sdk-container-builds/blob/main/docs/ContainerCustomization.md).

## Insecure Registry Rejected

**Symptom:** Pushing to an internal HTTP registry fails.

**Cause:** The SDK defaults to HTTPS.

**Fix:** Set `DOTNET_CONTAINER_INSECURE_REGISTRIES` to a comma-separated list of domains (SDK 9.0.100+), or configure the registry in Docker / Podman config (SDK 8.0.400+ reads it to decide HTTP vs HTTPS).

## Multi-Architecture Publish Race

**Symptom:** A multi-architecture publish (several RIDs in `ContainerRuntimeIdentifiers`) fails with build race errors in the per-RID builds, for example in a Blazor WebAssembly project.

**Cause:** The SDK publishes the per-RID images of one multi-RID publish in parallel by default.

**Fix:** Set `ContainerPublishInParallel=false` (SDK 8.0.408, 9.0.300, 10.0 and later) to publish the RIDs one after another. The property affects only the per-RID publishes of one multi-RID publish; it does not serialize container publishes across projects.

## Multi-Target Publishing

**Symptom:** Publishing a project that sets `<TargetFrameworks>` fails with `NETSDK1129`, and no image is built.

**Cause:** One publish builds one image for one target framework. Without a selected framework, `dotnet publish` stops before any container work, and the container targets also reject several target frameworks (`CONTAINERS0666`).

**Fix:** Select one framework with `-f <tfm>` or `-p:TargetFramework=<tfm>`, and run one publish per framework if you need an image for each:

```bash
dotnet publish src/MyService/MyService.csproj \
  -p:TargetFramework=net10.0 \
  /t:PublishContainer
```

For multi-arch (not multi-TFM) output you want `ContainerRuntimeIdentifiers`, not multiple TFMs — see [references/ci-cd.md](ci-cd.md).
