using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ArgsTests
{
    [Test]
    [Arguments("run", true)]
    [Arguments("plan", true)]
    [Arguments("status", true)]
    [Arguments("selfcheck", true)]
    [Arguments("run", false)]
    [Arguments("plan", false)]
    [Arguments("status", false)]
    [Arguments("selfcheck", false)]
    public async Task Timeout_is_only_for_wait_and_names_the_task_file_field(string command, bool hasValue)
    {
        await Assert.That(() => Args.Parse(hasValue ? [command, "--timeout", "7"] : [command, "--timeout"]).ValidatePaths())
            .ThrowsExactly<HarnessException>().WithMessage("--timeout is only for wait; use timeoutMinutes in the task file for worker timeouts");
    }

    [Test]
    public async Task Wait_timeout_consumes_one_value()
    {
        var args = Args.Parse(["wait", "z-run", "--timeout", "7", "--ledger", Path.GetTempPath()]);
        await Assert.That(args.Command).IsEqualTo("wait");
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "z-run" }, CollectionOrdering.Matching);
        await Assert.That(args.Get("timeout")).IsEqualTo("7");
        await Assert.That(args.Get("ledger")).IsEqualTo(Path.GetTempPath());
        await Assert.That(() => Args.Parse(["wait", "z-run", "--timeout"]))
            .ThrowsExactly<HarnessException>().WithMessage("--timeout needs a value");
    }

    [Test]
    public async Task No_arguments_yields_an_empty_command_and_collections()
    {
        var args = Args.Parse([]);

        await Assert.That(args.Command).IsEqualTo("");
        await Assert.That(args.Positional).IsEmpty();
        await Assert.That(args.Options).IsEmpty();
    }

    [Test]
    public async Task Positionals_and_flags_can_be_interleaved_without_reordering()
    {
        var args = Args.Parse(["reply", "run-id", "--message", "two words", "ask-id", "--all", "reject"]);

        await Assert.That(args.Command).IsEqualTo("reply");
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "run-id", "ask-id", "reject" }, CollectionOrdering.Matching);
        await Assert.That(args.Options.Count).IsEqualTo(2);
        await Assert.That(args.Get("message")).IsEqualTo("two words");
        await Assert.That(args.Has("all")).IsTrue();
        await Assert.That(args.Get("all")).IsNull();
        await Assert.That(args.Has("absent")).IsFalse();
        await Assert.That(args.Get("absent")).IsNull();
        await Assert.That(args.Require(1, "ask id")).IsEqualTo("ask-id");
    }

    [Test]
    [Arguments("dir")]
    [Arguments("repo")]
    [Arguments("max")]
    [Arguments("run")]
    [Arguments("policy")]
    [Arguments("message")]
    public async Task Each_value_option_consumes_exactly_one_token(string name)
    {
        var args = Args.Parse(["run", $"--{name}", "value", "task"]);

        await Assert.That(args.Get(name)).IsEqualTo("value");
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "task" }, CollectionOrdering.Matching);
        await Assert.That(() => Args.Parse(["run", $"--{name}"]))
            .ThrowsExactly<HarnessException>().WithMessage($"--{name} needs a value");
    }

    [Test]
    [Arguments("all")]
    [Arguments("force")]
    [Arguments("continue")]
    [Arguments("unknown")]
    public async Task Non_value_options_are_accepted_as_flags_Today(string name)
    {
        var args = Args.Parse(["status", $"--{name}", "value"]);

        await Assert.That(args.Has(name)).IsTrue();
        await Assert.That(args.Get(name)).IsNull();
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "value" }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("--force")]
    [Arguments("")]
    public async Task Value_options_accept_flag_shaped_and_empty_values_Today(string value)
    {
        var args = Args.Parse(["run", "--repo", value]);

        await Assert.That(args.Get("repo")).IsEqualTo(value);
        await Assert.That(args.Has("force")).IsFalse();
        await Assert.That(args.Positional).IsEmpty();
    }

    [Test]
    public async Task Repeated_options_keep_the_last_value_Today()
    {
        var args = Args.Parse(["run", "--max", "1", "--force", "--max", "3", "--force"]);

        await Assert.That(args.Options.Count).IsEqualTo(2);
        await Assert.That(args.Get("max")).IsEqualTo("3");
        await Assert.That(args.Has("force")).IsTrue();
        await Assert.That(args.Get("force")).IsNull();
    }

    [Test]
    public async Task Option_names_are_case_sensitive_Today()
    {
        var args = Args.Parse(["run", "--Repo", "value", "--repo", "lower"]);

        await Assert.That(args.Has("Repo")).IsTrue();
        await Assert.That(args.Get("Repo")).IsNull();
        await Assert.That(args.Get("repo")).IsEqualTo("lower");
        await Assert.That(args.Has("REPO")).IsFalse();
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "value" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Equals_syntax_and_the_double_dash_terminator_are_just_flags_Today()
    {
        var args = Args.Parse(["run", "--max=3", "--", "--force", "task"]);

        await Assert.That(args.Has("max=3")).IsTrue();
        await Assert.That(args.Get("max=3")).IsNull();
        await Assert.That(args.Has("max")).IsFalse();
        await Assert.That(args.Has("")).IsTrue();
        await Assert.That(args.Has("force")).IsTrue();
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "task" }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("-f")]
    [Arguments("-")]
    [Arguments("")]
    public async Task Tokens_without_two_leading_dashes_are_positionals_Today(string token)
    {
        var args = Args.Parse(["run", token]);

        await Assert.That(args.Positional).IsEquivalentTo(new[] { token }, CollectionOrdering.Matching);
        await Assert.That(args.Options).IsEmpty();
        await Assert.That(args.Require(0, "task file")).IsEqualTo(token);
    }

    [Test]
    public async Task The_first_token_is_always_the_command_Today()
    {
        var args = Args.Parse(["--force", "run"]);

        await Assert.That(args.Command).IsEqualTo("--force");
        await Assert.That(args.Options).IsEmpty();
        await Assert.That(args.Positional).IsEquivalentTo(new[] { "run" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Require_reports_the_missing_positional()
    {
        var args = Args.Parse(["reply", "run-id"]);

        await Assert.That(() => args.Require(1, "ask id"))
            .ThrowsExactly<HarnessException>().WithMessage("missing ask id");
    }

    [Test]
    public async Task Contract_paths_require_absolute_paths_and_preserve_repeated_order()
    {
        var zulu = Path.Combine(AppContext.BaseDirectory, "zulu.md");
        var alpha = Path.Combine(AppContext.BaseDirectory, "alpha.md");
        var args = Args.Parse(["plan", "--contract", zulu, "--contract", alpha, "--contract", zulu]);
        await Assert.That(() => args.ValidatePaths()).ThrowsNothing();
        await Assert.That(args.Contracts).IsEquivalentTo(new[] { zulu, alpha, zulu }, CollectionOrdering.Matching);
        foreach (var entry in new[] { "@worker-contract", "@sol-worker", "@unknown" })
            await Assert.That(() => Args.Parse(["plan", "--contract", entry]).ValidatePaths())
                .ThrowsExactly<HarnessException>().WithMessage($"contract aliases were removed; name the file path: {entry}");
        await Assert.That(() => Args.Parse(["plan", "--contract", "contract.md"]).ValidatePaths())
            .ThrowsExactly<HarnessException>().WithMessage("--contract must be an absolute path");
    }

    [Test]
    [Arguments("repo")]
    [Arguments("dir")]
    [Arguments("policy")]
    public async Task Path_options_require_fully_qualified_paths(string name)
    {
        foreach (var relative in new[] { "relative/path", "../path", ".", "" })
        {
            var args = Args.Parse(["status", $"--{name}", relative]);
            await Assert.That(() => args.ValidatePaths())
                .ThrowsExactly<HarnessException>().WithMessage($"--{name} must be an absolute path");
        }

        var absolute = Path.Combine(AppContext.BaseDirectory, "not-required-to-exist", "path");
        var valid = Args.Parse(["status", $"--{name}", absolute]);
        await Assert.That(() => valid.ValidatePaths()).ThrowsNothing();
        await Assert.That(valid.Get(name)).IsEqualTo(absolute);
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    public async Task Plan_and_run_require_an_absolute_first_task_path(string command)
    {
        var invalid = Args.Parse([command, "tasks.jsonc"]);
        await Assert.That(() => invalid.ValidatePaths())
            .ThrowsExactly<HarnessException>().WithMessage("task file must be an absolute path");

        var absolute = Path.Combine(AppContext.BaseDirectory, "tasks.jsonc");
        var valid = Args.Parse([command, absolute]);
        await Assert.That(() => valid.ValidatePaths()).ThrowsNothing();
        await Assert.That(valid.Require(0, "task file")).IsEqualTo(absolute);
    }

    [Test]
    [Arguments("status")]
    [Arguments("resume")]
    [Arguments("reply")]
    [Arguments("Run")]
    public async Task Other_commands_do_not_validate_positional_paths_Today(string command)
    {
        var args = Args.Parse([command, "relative/path"]);

        await Assert.That(() => args.ValidatePaths()).ThrowsNothing();
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    public async Task Path_validation_does_not_require_a_task_or_check_extra_positionals_Today(string command)
    {
        await Assert.That(() => Args.Parse([command]).ValidatePaths()).ThrowsNothing();
        var args = Args.Parse([command, AppContext.BaseDirectory, "relative-extra.jsonc"]);
        await Assert.That(() => args.ValidatePaths()).ThrowsNothing();
    }

    [Test]
    public async Task Rooted_paths_without_a_drive_are_absolute_only_on_Unix_Today()
    {
        var args = Args.Parse(["status", "--repo", "/repo"]);

        if (OperatingSystem.IsWindows())
        {
            await Assert.That(() => args.ValidatePaths())
                .ThrowsExactly<HarnessException>().WithMessage("--repo must be an absolute path");
        }
        else
        {
            await Assert.That(() => args.ValidatePaths()).ThrowsNothing();
        }
    }

    [Test]
    public async Task Drive_relative_paths_are_not_absolute()
    {
        var args = Args.Parse(["status", "--repo", "C:relative"]);

        await Assert.That(() => args.ValidatePaths())
            .ThrowsExactly<HarnessException>().WithMessage("--repo must be an absolute path");
    }
}
