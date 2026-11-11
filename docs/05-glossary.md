# Glossary — AI/RAG terms for a .NET developer

Every term you'll meet in this repo, in plain language, with a .NET analogy where
one helps. Nothing here requires prior AI knowledge.

## Models and generation

| Term | What it actually is | Analogy / intuition |
|---|---|---|
| **LLM** (large language model) | A function `text -> text` trained to predict the next token. No memory between calls, no database, no awareness of your files. | A stateless HTTP service. Every request must carry everything it needs. |
| **Token** | The unit models read/write: roughly ¾ of a word in English (~4 characters). | Like a string chunked by a tokenizer; it's the billing and context unit. |
| **Context window** | Max tokens one request may contain (prompt + answer). | Like a fixed-size request buffer. Exceed it and the request fails or gets truncated. |
| **Prompt** | The text you send. In chat models it's a list of messages with roles (`system`, `user`, `assistant`). | The request body. |
| **System prompt** | Instructions that apply to the whole conversation ("answer only from the context"). | Middleware/config applied before the user's input. |
| **Completion** | The model's output. | The response body. |
| **Temperature** | Randomness knob: 0 ≈ deterministic, 1 ≈ creative. | Retry policy vs. jitter. For RAG you want it low (~0.1). |
| **Grounding** | Forcing the model to answer from supplied context instead of its training data. | "Only read from this table, don't guess." |
| **Hallucination** | A confident, fluent, wrong answer. The model isn't lying — it's predicting plausible text. | A method that returns a plausible-looking value instead of throwing. |
| **Fine-tuning** | Retraining a model on your data. Expensive, slow to update, and **not** what this repo does. | Like baking data into the binary vs. querying it at runtime. |

## Embeddings and similarity

| Term | What it actually is | Analogy / intuition |
|---|---|---|
| **Embedding** | A function `text -> float[N]` where similar meanings produce similar vectors. Deterministic. | A *semantic hash*. Not for equality — for closeness. |
| **Dimension** | Length of that vector (`N`), fixed per model: 384, 768, 1024, 1536… | The width of the row you store. |
| **Vector space** | The N-dimensional space all embeddings of one model live in. | A coordinate system. **Vectors from different models don't share one.** |
| **Cosine similarity** | The cosine of the angle between two vectors: `1.0` same direction, `0` unrelated, `-1` opposite. Ignores magnitude. | Comparing direction only. Two vectors can have very different lengths and still score 1.0. |
| **Cosine distance** | `1 - cosine similarity`. What pgvector's `<=>` returns (0 = identical). | The "distance" version of the same number. |
| **L2 distance** | Straight-line (Euclidean) distance between vector endpoints. Sensitive to magnitude. | The other common metric; pgvector `<->`. |
| **Inner product** | Dot product; favors vectors pointing the same way *and* large magnitude. | Used by some embedding models; pgvector `<#>`. |
| **Normalization** | Scaling a vector to length 1. Makes cosine and inner product agree. | Like normalizing units before comparing. |

## Chunking and retrieval

| Term | What it actually is | Analogy / intuition |
|---|---|---|
| **Chunk** | A slice of a document that is embedded and stored as one row. | The row in your search index. |
| **Chunk size / overlap** | Target characters per chunk; how much of the previous chunk is repeated at the start of the next. | Page size and a small repeat to avoid losing sentences at page boundaries. |
| **Vector search** | Find the rows whose embedding is closest to the query embedding. | `ORDER BY similarity LIMIT k` — a search index query. |
| **Exact search** | Compare against every row (sequential scan). 100% recall. | `table scan`. Fine for thousands of chunks. |
| **ANN** (approximate nearest neighbor) | Index that trades a little accuracy for a lot of speed. | A B-tree for vectors: fast lookup, tiny chance of missing a row. |
| **HNSW** | The most common ANN index in pgvector: a multi-layer graph you walk toward the query. | Skip lists / hierarchical routing. |
| **IVFFlat** | ANN index that clusters vectors into lists and only scans nearby lists. | Partition pruning. Needs data before you build it. |
| **Recall@k** | Fraction of queries where the correct row is in the top k. | Your index's "hit rate". |
| **top-K** | How many chunks you retrieve per question. | `Take(k)` on the search results. |
| **Hybrid search** | Combine vector search with keyword/lexical search and merge the rankings. | Union of two indexes, fused by rank. |
| **BM25 / full-text search** | Classic keyword relevance ranking. PostgreSQL has it via `tsvector`/`tsquery`. | `LIKE` with ranking, on steroids. Strong at exact names. |
| **RRF** (Reciprocal Rank Fusion) | Merge multiple ranked lists using `1/(k + rank)`. No score calibration needed. | Merge two sorted lists by position, not by value. |
| **Reranking** | Retrieve many candidates cheaply, then re-score the top ones with a stronger model. | Two-phase query: index scan, then in-memory sort. |
| **Cross-encoder** | A reranker that reads (query, document) together — accurate but slow. | Full comparison vs. comparing precomputed hashes. |

## RAG and evaluation

| Term | What it actually is | Analogy / intuition |
|---|---|---|
| **RAG** (retrieval-augmented generation) | Retrieve relevant text, put it in the prompt, generate an answer from it. | An open-book exam: the model may only use the pages you hand it. |
| **Citation** | Pointing each claim at the retrieved passage it came from (`[2]`). | Trace IDs from output back to source rows. |
| **Faithfulness** | Whether every claim in the answer is supported by the retrieved context. | No unsupported data in the response. |
| **Answer relevance** | Whether the answer actually addresses the question. | Correct output, not just valid output. |
| **Context precision / recall** | Whether the retrieved chunks are relevant / whether all needed chunks were retrieved. | Precision/recall of your search. |
| **Hit rate@k / MRR** | Simple retrieval metrics: was the right chunk in the top k / how high did it rank. | Unit-testable retrieval quality, no LLM needed. |
| **RAGAS / LLM-as-judge** | Frameworks that use a model to score faithfulness/relevance automatically. | Snapshot/approval tests, but probabilistic. |
| **Evaluation set** | A fixed list of questions with known correct sources/answers. | Your regression test suite for the pipeline. |

## Multimodal (for the "what about graphs?" question)

| Term | What it actually is | Analogy / intuition |
|---|---|---|
| **OCR** | Extracting text pixels into characters. | Reading a screenshot. Useless for chart *meaning*. |
| **VLM** (vision-language model) | An LLM that also accepts images in the prompt. | Same API, image content instead of text. |
| **Multimodal embedding** (CLIP-style) | One model that maps images *and* text into a shared vector space. | A hash function that accepts two input types. |
| **Image captioning for RAG** | Ask a VLM to describe a figure, then index that description as text. | Convert binary to text at the ETL boundary. |

## Infrastructure and configuration

| Term | What it actually is | Analogy / intuition |
|---|---|---|
| **OpenAI-compatible API** | The de-facto HTTP contract: `POST /v1/embeddings`, `POST /v1/chat/completions`. Many servers implement it. | A standard interface many providers implement. |
| **Base URL** | Root of that API, e.g. `http://localhost:1234/v1`. | Connection string for the model server. |
| **Model ID** | The exact name the server uses for a model, e.g. `text-embedding-nomic-embed-text-v1.5`. | Like a package ID — must match exactly. |
| **LM Studio** | Desktop app that serves local models with an OpenAI-compatible API. | Local dev server for models. |
| **Ollama** | CLI/server for local models, also OpenAI-compatible. | Same role as LM Studio, different UX. |
| **pgvector** | PostgreSQL extension adding the `vector` type, distance operators and ANN indexes. | A column type + index plugin for Postgres. |

## The three numbers that matter most

1. **`score`** in API responses = cosine similarity (higher is better).
2. **`chunkCount`** = how many rows one file produced (chunking sanity check).
3. **`topK`** = how many rows the model actually saw.

If an answer is wrong, check them in that order.
