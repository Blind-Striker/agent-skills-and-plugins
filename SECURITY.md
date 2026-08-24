# Security policy

## Reporting a vulnerability

Do not open a public issue for suspected vulnerabilities, credentials, tokens, private keys, or
other sensitive material. Use GitHub's private vulnerability reporting for this repository instead:

<https://github.com/Blind-Striker/agent-skills-and-plugins/security/advisories/new>

This is a personal project with no response-time SLA. Reports that include a reproducible impact on
the curation compiler, generated distributions, or OpenCode installer are the most actionable.

## Scope

Security-sensitive surfaces include the build and transformation tooling, generated Plugin and
Bundle contents, Package integrity, and the OpenCode installer's Plan, Apply, Ownership, and Recovery
boundaries. Upstream skill content should normally be reported to its upstream project unless this
repository's transformation introduced the issue.
