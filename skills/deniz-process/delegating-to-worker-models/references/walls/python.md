# Python walls

Defaults; the coordinator may change them with a recorded reason.

| Wall | Command | Identity ratchet |
|---|---|---|
| ruff | `uvx ruff@0.17.1 check --config <walls>/ruff.toml --output-format json --exit-zero <files>` | compare (code, filename, message) on the base and the head; ruff has no native baseline file |
| pyright | `npx -y pyright@1.1.414 -p <walls>/pyrightconfig.json --outputjson <files>` | compare (rule, file, message) |
| vulture | `uvx vulture@2.16 --min-confidence 80 <files>` | compare (file, message) |
| bandit | `uvx bandit@1.9.4 -q -ll -f json <files>` | compare (test_id, filename, issue_text) |

`ruff.toml`:

```toml
target-version = "py39"   # the oldest Python the code must run on
line-length = 120

[lint]
select = ["E", "F", "W", "B", "UP", "C90", "PL", "SIM", "RET", "ERA"]

[lint.mccabe]
max-complexity = 10
```

`pyrightconfig.json`: `{"typeCheckingMode": "basic", "pythonVersion": "3.9"}`. Use `basic`, not `strict`:
strict mode forces annotations on unannotated code and inflates line counts for reasons unrelated to
quality.

Platform code: pyright evaluates for the host platform, so POSIX-only calls must sit behind a
`sys.platform` check, not behind a suppression.
