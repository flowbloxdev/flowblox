# Vector indexing and retrieval

`VectorStore` provides a generic document-indexing contract: `SourceId` identifies a document,
`Title` is display metadata, and `Content` is indexed text. The storage adapter owns its database
schema. Source discovery, conversion, caching policies and application-specific tables belong to
the consuming flow, not the storage contract.

`VectorSourceTable` exposes indexed source IDs and content hashes for the selected collection and
embedding/chunking profile. Consumers can use this information to decide which documents need updates.

`VectorTable` embeds `Question` with its configured `Embeddings` provider and retrieves up to `TopK`
matching chunks from `Store`. It reuses the store's collection and profile configuration.

`SourceFilter` is optional. When configured, it accepts an `IReadableTable`; `SourceIdColumn`
selects the column containing allowed source IDs and defaults to `SourceId`. Empty filter results
allow no sources. Without a filter, retrieval searches the stored collection/profile without an
additional source-ID restriction. Filtering results does not delete stored documents.

These components can be composed with existing readers, converters and collectors without depending
on a particular local or Project Space project.
