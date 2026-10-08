# Security

Millrace is a simulator. It is meant for development, testing, training and
demonstration, not for controlling real equipment, and it is not safety-rated.
Never connect it to a live plant or put it on an operational (OT) network.

## What to know before you run it

- **`millrace serve` has no authentication.** Its Modbus TCP server accepts
  reads and writes from any client that can reach the port, as Modbus TCP
  does. It listens on `127.0.0.1` unless you pass `--bind`. Bind it to
  another address only on a network you trust, and never expose it to the
  internet.
- **The FUXA stack in `hmi/fuxa/`** publishes its ports on `127.0.0.1` only.
  FUXA runs with its default settings and no login. Treat it as a local demo.
- **`--assembly` loads and runs .NET code.** A catalogue module is ordinary
  code with full access to your machine. Load only modules you built or
  trust.
- **Plant and scenario files** are data. Millrace validates them before it
  builds anything and reports a diagnostic rather than running a bad file.

## Supported versions

Only the latest release receives fixes.

| Version | Supported |
|---|---|
| 1.1.x | yes |
| < 1.1 | no |

## Reporting a vulnerability

Report it privately through GitHub's
[private vulnerability reporting](https://github.com/abramsdg79/Millrace/security/advisories/new)
(the **Security** tab, then **Report a vulnerability**). Please do not open a
public issue for it. Say what you found, how to reproduce it, and the version
or commit you tested. You will get a reply within a week. A fix ships in a
patch release, with credit in the changelog unless you would rather not be
named.

Weaknesses that are inherent to the protocol, such as Modbus TCP having no
authentication, are documented above. They are not vulnerabilities in
Millrace.
