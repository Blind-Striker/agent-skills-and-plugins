# textkit

Small text and utility helpers for internal tools.

## API

- `parseDuration(text)`: parses `1h2m3s`-style text into milliseconds.
- `slugify(text)`: turns text into a lowercase, dash-separated slug.
- `getUsr(users, id)`: finds a user record by id.
- `formatUserLine(users, id)`: renders one user as a report line.
- `toTsv(rows)`: renders rows as tab-separated text.
- `disableUser(users, id)`: marks a user as disabled.
- `backoff(attempt, base)`: the delay before a retry attempt.
- `parseHost(text)`: validates a host name.
- `parsePort(text)`: parses a TCP port.
- `findDuplicates(values)`: lists the values that appear more than once.

## Releasing

Bump the version in package.json, add a CHANGELOG.md entry, then run `npm run release`.
