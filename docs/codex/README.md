# Codex Working Context

This folder is the durable context entry point for Codex and other contributors.
Repository-wide instructions live in `/AGENTS.md`; keep this document focused on
how to navigate and evolve StreamForge.

## Read before changing code

1. Read `/AGENTS.md` for repository conventions.
2. Read `docs/architecture/README.md` for service ownership.
3. Review `docs/architecture/decisions/` for accepted constraints.
4. Read the relevant setup guide, API notes, or runbook for the task.

## Change checklist

- Treat the current service folders as proposals rather than fixed decisions.
- Keep code and configuration placeholders empty until implementation is requested.
- Update tests and documentation with behavioral changes.
- Never expose secrets, raw credentials, signing keys, or private media.
- Do not invent build commands, infrastructure products, or runtime assumptions.

## Handoff notes

For incomplete work, record the goal, files changed, decisions made, verification
performed, remaining risks, and exact next command. Prefer an ADR for lasting
architectural decisions rather than burying rationale in a prompt or commit.

Official guidance on repository instructions is available in the
[Codex AGENTS.md documentation](https://developers.openai.com/codex/guides/agents-md).
