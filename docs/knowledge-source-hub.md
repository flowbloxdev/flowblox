# Shared knowledge source search

`Search Knowledge Sources (SQLite)` owns source discovery, path normalization, the source cache,
per-document conversion and vector indexing, and retrieval. The three KnowledgeResearch projects
call this shared project, collect its relevant excerpts, and retain their existing CloudAI, ONNX GenAI,
or extractive QA answer blocks and model scripts.

Project Space hub: `ac43f8d2b7b41490a633860a98bd9145` (public).
The existing CloudAI and GenAI projects remain public; the QA project remains private.

- `SourcesFile` defaults to the caller's generated `KnowledgeSources.xlsx`. Pass an absolute workbook
  path to share sources across projects. Relative document and directory paths resolve beside the workbook.
- `RefreshExisting` defaults to `false`, both as an input and as a workbook column.
  An indexed source is reused without reading or downloading its contents unless either setting is true.
  Workbooks without that column remain supported through optional column mappings.
- Sources are deduplicated by SourceId before fetching. Confluence discovery requests metadata;
  page bodies are retrieved only for sources that need loading. URL and Confluence contents become
  hashed files under the hub's `Input/remoteContents` directory.
- The shared file read precedes the raw fingerprint. It includes the title and the actual file bytes
  encoded as base64. An unchanged fingerprint skips conversion and embedding. A new embedding/chunking
  profile forces reindexing even when the raw content is unchanged.
- Conversion and indexing complete for each file before proceeding to the next file. Collections contain
  paths and metadata, rather than all document contents. Empty converted text is skipped.
- Source metadata is upserted by collection and SourceId, with a session marker limiting retrieval to
  this run's active source list. No destructive SQL application option is required.
- The hub returns one `Matches` dataset per relevant chunk: `Rank`, `SourceId`, `Title`, `Content`,
  `Score`, and `ChunkIndex`. Callers use a Join Collector to prepare their existing answer context.
- `EmbeddingProvider` defaults to CPU (`Default`), independently of the answer model's provider.
  `EmbeddingModelFolder` can share an embedding model. `UseOcr` enables the PDF empty-page fallback.

`VectorStore` retains a generic document indexing contract: SourceId identifies a document, Title is
display metadata, and Content is indexed text. Its internal vector schema is adapter-owned, rather
than a KnowledgeResearch-specific business schema. `VectorSourceTable` exposes indexed IDs for the
current collection/profile; `VectorTable.SourceFilter` accepts any readable table with a configurable
source-ID column. Project-specific mappings and session tables remain in the flow project.

PDF page outputs and OCR modes are described in [pdf-ocr.md](pdf-ocr.md). The distributed
`GetFileContent` skill and `Pdf2Text_SequenceDetection` example use a document collector and are
saved locally only. Manually edited projects must be opened, automatically aligned, saved through
the Avalonia application, and closed before upload; see `--layout-save` in the desktop integration runner.

The local `(SQLite)` rename creates new Input directories. Existing CloudAI/QA workbooks were preserved
there, with missing `RefreshExisting` headers added; SQLite online backups included committed WAL data.
The original directories remain intact. Local embedding folders are reused through directory junctions:
keep their original targets, or configure `EmbeddingModelFolder` to another complete model directory.
