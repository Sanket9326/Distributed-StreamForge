# API and Event Contracts

Store human-readable API notes and exported OpenAPI documents here. Version
integration events in `src/backend/shared/contracts/`, preserving backward
compatibility while producers and consumers deploy independently.

Every external API or event change must describe authentication, idempotency,
error behavior, pagination where relevant, and an example request or payload.
