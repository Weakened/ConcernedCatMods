# Security Policy

## Reporting

Use the GitHub issue tracker for ordinary bugs.

For a vulnerability that could expose user data, execute unintended code, corrupt saves, bypass multiplayer permissions, or create a serious security/privacy risk, avoid posting exploit details publicly until the maintainer can assess it.

Use the current official contact channel published by The Concerned Cat.

## Do not include in public issues

- API tokens
- account credentials
- private server passwords
- full unrelated personal logs
- private save files unless explicitly requested and sanitized
- machine-specific secrets

## Security boundaries

Concerned Cartographer should:

- never require elevated/admin privileges for normal use;
- never execute downloaded code;
- never send telemetry unless a future version explicitly documents it;
- treat network payloads as untrusted;
- bound message sizes and work queues;
- reject malformed/incompatible protocol data safely;
- never mutate Valheim world saves as private persistence.

## AI-assisted development

AI-generated code is not exempt from review.

Network, filesystem, deserialization, reflection and migration changes receive extra scrutiny before release.
