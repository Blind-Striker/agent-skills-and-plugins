#!/usr/bin/env -S dotnet --
#:package OpenCodeDotNet.Sdk@0.9.0-preview.13
#:include src/Usings.cs
#:include src/Contracts/Errors.cs
#:include src/Contracts/Constants.cs
#:include src/Contracts/ConfigFiles.cs
#:include src/Contracts/AskFiles.cs
#:include src/Contracts/TaskFile.cs
#:include src/Contracts/LedgerShapes.cs
#:include src/Contracts/Json.cs
#:include src/Config/Locations.cs
#:include src/Config/AllowList.cs
#:include src/Config/Policy.cs
#:include src/Config/Wildcard.cs
#:include src/Ledger/Ledger.cs
#:include src/Ledger/SupervisorLock.cs
#:include src/Workspace/Scope.cs
#:include src/Workspace/Proc.cs
#:include src/Workspace/Executables.cs
#:include src/Workspace/Git.cs
#:include src/Scheduler/Plan.cs
#:include src/Scheduler/Planner.cs
#:include src/Scheduler/TaskRun.cs
#:include src/Scheduler/Supervisor.cs
#:include src/Scheduler/Settlement.cs
#:include src/Escalation/AskQueue.cs
#:include src/Escalation/Escalation.cs
#:include src/Cli/Cli.cs
#:include src/Cli/Args.cs
#:include src/Cli/Commands.cs
#:include src/Cli/Commands.Control.cs
#:include src/Cli/Commands.SelfCheck.cs
#:include src/Backends/OpenCode/Catalog.cs
#:include src/Backends/OpenCode/Ready.cs
#:include src/Backends/OpenCode/HarnessServer.cs

return await Cli.MainAsync(args);
