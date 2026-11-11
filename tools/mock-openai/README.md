# mock-openai

A ~100-line, dependency-free mock of the OpenAI API used by this repo. It exists
so the whole RAG pipeline can be tested **without LM Studio or any real model**.

- `POST /v1/embeddings` → deterministic pseudo-embeddings (seeded by the text hash)
- `POST /v1/chat/completions` → a fixed canned answer

The vectors are meaningless for retrieval quality. This validates *plumbing*
(ingest → embed → store → retrieve → prompt → answer), not answer quality.

## Usage

```sh
python3 tools/mock-openai/mock_openai.py --port 8123 --dim 768
```

In another terminal, run the API pointed at it:

```sh
Llm__BaseUrl=http://127.0.0.1:8123/v1 \
Llm__ApiKey=mock \
Llm__ChatModel=mock-chat \
Llm__EmbeddingModel=mock-embed \
  dotnet run --project src/UmamusumeRag.Api
```

```sh
curl -X POST localhost:5033/api/documents/ingest
curl -X POST localhost:5033/api/ask \
  -H 'Content-Type: application/json' \
  -d '{"question": "What is the URA Finals?"}'
```

`--dim` must match your embedding dimension expectations (768 is the default in
this repo). Environment variables override `appsettings.json`; the `__` maps to
`:` in configuration keys.

## Clean up afterwards

Mock embeddings are random. Delete anything ingested during the test:

```sh
curl -s localhost:5033/api/documents | grep -o '"id":"[^"]*"'
curl -X DELETE localhost:5033/api/documents/<id>
```

Or wipe everything: `docker compose down -v && docker compose up -d`.

Otherwise the random vectors stay in the index and the content-hash skip will
prevent re-embedding those files with a real model unless you pass `?force=true`.
