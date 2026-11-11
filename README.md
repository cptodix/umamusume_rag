# umamusume_rag

A .NET 10 boilerplate for practicing **Retrieval-Augmented Generation (RAG)** over
Umamusume: Pretty Derby (Global) documentation.

The RAG pipeline is hand-rolled so every stage is visible in code:
chunking, embedding, storage, vector search, prompt construction, and generation.

## Documentation

| Doc | Contents |
|---|---|
| [`docs/04-pipeline-walkthrough.md`](docs/04-pipeline-walkthrough.md) | **Start here** — every pipeline step, the exact file/method that runs it, and where to intervene |
| [`docs/05-glossary.md`](docs/05-glossary.md) | AI/RAG vocabulary explained for a .NET developer (embeddings, cosine, ANN, RRF, …) |
| [`docs/01-what-was-built.md`](docs/01-what-was-built.md) | Design decisions, verification results, RAG gotchas, deliberate gaps |
| [`docs/02-next-steps.md`](docs/02-next-steps.md) | Run it, build a baseline, experiment roadmap, troubleshooting by symptom |
| [`docs/03-rag-resources.md`](docs/03-rag-resources.md) | Curated RAG reading list: guides, papers, indexes, evaluation, implementations |
| [`AGENTS.md`](AGENTS.md) | Operating guide for AI agents (and humans): invariants, commands, recipes, verification protocol |
| [`docs/agent/teaching-notes.md`](docs/agent/teaching-notes.md) | Curriculum + diagnostic flow for an AI teacher |
| [`tools/mock-openai/`](tools/mock-openai/README.md) | Fake OpenAI API so the full pipeline can be tested without a real model |

Suggested reading order if you are new to RAG: **04 → 05 → 02 → 01 → 03**.

## Architecture

```
                 POST /api/documents/ingest | /upload
                              │
              ┌───────────────▼────────────────┐
              │  DocumentIngestionService      │   Core/Services
              │  load → chunk → embed → store  │
              └───────┬───────────┬────────────┘
                      │           │
        IDocumentLoader│           │IEmbeddingGenerator  (Microsoft.Extensions.AI)
   (.md/.txt/.docx)   │           │
                      │           ▼
                      │     OpenAI-compatible endpoint
                      │     (DeepInfra / LM Studio / ...)
                      ▼
              ┌────────────────────────┐
              │  PostgreSQL + pgvector │   chunks.embedding = vector
              └───────────┬────────────┘
                          │ cosine search (<=>)
                 POST /api/ask
                          │
              ┌───────────▼────────────┐
              │  RagQueryService       │   Core/Services
              │  embed → retrieve →    │
              │  prompt → generate     │
              └────────────────────────┘
```

| Project | Purpose |
|---|---|
| `src/UmamusumeRag.Api` | Minimal API endpoints, configuration, DI wiring |
| `src/UmamusumeRag.Core` | Entities, abstractions, chunker, ingestion + query pipeline (no DB/AI SDK dependencies) |
| `src/UmamusumeRag.Infrastructure` | EF Core + pgvector storage, document loaders, OpenAI-compatible AI clients |

## How the RAG pipeline maps to code

| RAG stage | Where |
|---|---|
| Load documents | `Infrastructure/Ingestion/TextDocumentLoader.cs`, `DocxDocumentLoader.cs` |
| Chunk text | `Core/Services/RecursiveTextChunker.cs` |
| Embed chunks | `Core/Services/DocumentIngestionService.cs` (via `IEmbeddingGenerator`) |
| Store chunks + vectors | `Infrastructure/Persistence/PgVectorChunkRepository.cs` (EF Core + pgvector) |
| Embed the question | `Core/Services/RagQueryService.cs` |
| Vector search (top-K) | `PgVectorChunkRepository.SearchChunksAsync` (pgvector `<=>` operator) |
| Prompt + generate | `RagQueryService.AskAsync` (grounded system prompt + numbered context) |

## Prerequisites

- .NET 10 SDK
- Docker (for the PostgreSQL + pgvector container)
- An OpenAI-compatible endpoint. **DeepInfra is the default** (cloud; needs an API key).
  Local **LM Studio** or **Ollama** work too — same code, no key (any non-empty value).

## Quickstart

```sh
# 1. Start PostgreSQL + pgvector
docker compose up -d

# 2. Restore the repo-local dotnet-ef tool (first time only)
dotnet tool restore

# 3. Provide your API key — it is kept out of the repo (user-secrets)
dotnet user-secrets set "Llm:ApiKey" "<your DeepInfra token>" --project src/UmamusumeRag.Api
#    Base URL and models are already configured for DeepInfra in appsettings.json.
#    (LM Studio/Ollama instead? change Llm:BaseUrl and set any non-empty key.)

# 4. Run the API (applies EF Core migrations automatically in Development)
dotnet run --project src/UmamusumeRag.Api
```

Then use `src/UmamusumeRag.Api/UmamusumeRag.Api.http` (VS Code REST Client / Rider) or curl:

```sh
# Ingest everything under ./data
curl -X POST http://localhost:5033/api/documents/ingest

# Ask a question
curl -X POST http://localhost:5033/api/ask \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the URA Finals?"}'
```

## Getting your Google Docs in

1. In Google Docs: **File → Download → Microsoft Word (.docx)** (or Markdown for simple docs).
2. Drop the files into the `data/` folder (subfolders are scanned recursively).
3. `POST /api/documents/ingest` — unchanged files are skipped automatically
   (content is hashed); pass `?force=true` to re-embed everything.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/health` | Database connectivity check |
| `POST` | `/api/documents/ingest?path=&force=` | Scan a folder and ingest all supported files |
| `POST` | `/api/documents/upload` | Upload a single file (multipart) and ingest it |
| `GET` | `/api/documents` | List ingested documents + chunk counts |
| `DELETE` | `/api/documents/{id}` | Delete a document and its chunks |
| `POST` | `/api/ask` | `{ "question": "...", "topK": 5 }` → answer + sources |

OpenAPI document (Development): `http://localhost:5033/openapi/v1.json`

## Configuration (`appsettings.json`)

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Postgres` | `localhost:5433/umamusume_rag` | Matches `compose.yaml` (host port 5433) |
| `Llm:BaseUrl` | `https://api.deepinfra.com/v1/openai` | Any OpenAI-compatible URL (LM Studio: `http://localhost:1234/v1`) |
| `Llm:ApiKey` | *(empty)* | **Never commit it.** Set via user-secrets or the `Llm__ApiKey` env var |
| `Llm:ChatModel` | `mistralai/Mistral-Small-24B-Instruct-2501` | Cheap + good instruction-following (see `docs/02-next-steps.md` §1) |
| `Llm:EmbeddingModel` | `BAAI/bge-m3` | 1024-dim, 8k ctx, multilingual, no prefixes needed |
| `Rag:ChunkSize` | `800` | Target chunk length (characters) |
| `Rag:ChunkOverlap` | `120` | Repeated characters between neighboring chunks |
| `Rag:TopK` | `5` | Chunks retrieved per question (overridable per request) |
| `Rag:DocumentPrefix` | *(empty)* | Prepended to chunks before embedding (e.g. `passage: ` for E5/nomic) |
| `Rag:QueryPrefix` | *(empty)* | Prepended to the question (e.g. `query: `) |
| `Rag:EmbeddingDimensions` | *(null)* | Matryoshka truncation, e.g. `512` — re-ingest after changing |
| `Rag:DataPath` | `../../data` | Folder scanned by `/ingest`, relative to the API project directory (the app content root) — so the default points at the repo's `data/` folder. Absolute paths also work. |

## Things to try next

See [`docs/02-next-steps.md`](docs/02-next-steps.md) for the experiment roadmap (chunking,
retrieval depth, embedding models, HNSW indexing, hybrid search, reranking, query
transformation, evaluation) and [`docs/03-rag-resources.md`](docs/03-rag-resources.md) for the
reading list behind each step.
