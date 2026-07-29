# Contracts: Chat Message Pipeline (R1 subset)

**Feature**: 004-chat-message-pipeline | **Date**: 2026-07-29

This feature exposes a small **HTTP route surface** (create thread, send message with a streamed response) plus **internal service contracts** for the pipeline stages (preflight, redaction, safety, model invocation). R1 ships no admin/UI surface — the Blazor chat page is a follow-up task.

See also: [contracts/service-interfaces.md](./service-interfaces.md), [contracts/route-table.md](./route-table.md).
