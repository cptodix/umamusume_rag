# RAG resources

A curated, verified reading list for the RAG concepts this repo exercises. Every link below was
checked and resolves. Ordered by "read this to do the corresponding experiment in
`02-next-steps.md`".

## Start here — what RAG is

| Resource | Why |
|---|---|
| [Retrieval Augmented Generation for Knowledge-Intensive NLP Tasks (2020)](https://arxiv.org/abs/2005.11401) | The original paper. Defines the retrieve-then-generate pattern this repo implements, and the distinction between RAG-sequence and RAG-token |
| [RAG with .NET (Microsoft Learn)](https://learn.microsoft.com/dotnet/ai/conceptual/rag) | The same pipeline described in .NET terms; maps closely to this repo's structure |
| [LangChain: RAG concepts](https://python.langchain.com/docs/concepts/rag/) | The clearest conceptual breakdown of index → retrieve → generate, including the "two-phase" mental model |
| [LlamaIndex: Understanding RAG](https://docs.llamaindex.ai/en/stable/understanding/rag/) | Another well-structured walkthrough; useful for spotting stages this repo doesn't have yet |
| [Pinecone: Retrieval-Augmented Generation guide](https://www.pinecone.io/learn/retrieval-augmented-generation/) | Vendor guide, but a good survey of variants (hybrid, reranking, evaluation) |

## Chunking

| Resource | Why |
|---|---|
| [Pinecone: Chunking strategies](https://www.pinecone.io/learn/chunking-strategies/) | The standard practical guide: fixed-size, recursive, sentence/paragraph, and when each fails |
| [Anthropic: Contextual Retrieval](https://www.anthropic.com/news/contextual-retrieval) | Shows how much retrieval improves when each chunk is prefixed with document-level context before embedding — a concrete next step after `3.1` |

Experiment: `3.1` in `02-next-steps.md`.

## Embeddings

| Resource | Why |
|---|---|
| [MTEB leaderboard](https://huggingface.co/spaces/mteb/leaderboard) | Pick an embedding model with evidence instead of vibes. Filter by dimension, size, and task |
| [MTEB blog post](https://huggingface.co/blog/mteb) | Explains what the leaderboard actually measures, and why retrieval scores matter more than overall averages for RAG |
| [OpenAI: Embeddings guide](https://platform.openai.com/docs/guides/embeddings) | Practical notes on what embeddings capture, distance metrics, and why models must not be mixed |
| [Sentence Transformers docs](https://www.sbert.net/) | The reference implementation for open embedding models (including `nomic-embed-text`, `bge`, `e5` families) |

Key facts to internalise: similarity is about **direction** (cosine), dimensions differ per model,
and **switching models invalidates every stored vector**.

Experiment: `3.4`.

## Vector search and indexes

| Resource | Why |
|---|---|
| [pgvector — README](https://github.com/pgvector/pgvector) | The extension behind this repo's storage: types, operators, and limits |
| [pgvector — Querying (`<=>`, `<->`, `<#>`)](https://github.com/pgvector/pgvector#querying) | The three distance operators and why cosine (`<=>`) is what you want for text embeddings |
| [pgvector — HNSW / indexing](https://github.com/pgvector/pgvector#hnsw) | How to add the approximate index this repo doesn't have yet, and what `m` / `ef_construction` do |
| [pgvector — Performance](https://github.com/pgvector/pgvector#performance) | Index tuning, `ef_search`, and when an exact scan is actually fine |
| [HNSW paper (2016)](https://arxiv.org/abs/1603.09320) | The algorithm underneath HNSW indexes — worth reading once to understand the recall/latency knob |
| [pgvector-dotnet](https://github.com/pgvector/pgvector-dotnet) | The .NET integration used here: `UseVector()`, `Vector` mapping, `CosineDistance`/`L2Distance` LINQ functions |

Experiments: `3.5`.

## Hybrid search and reranking

| Resource | Why |
|---|---|
| [Reciprocal Rank Fusion (Cormack et al., 2009)](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf) | The short paper behind the RRF formula in `3.6`; it's a 4-page read and needs no score calibration |
| [PostgreSQL full-text search controls](https://www.postgresql.org/docs/current/textsearch-controls.html) | `to_tsvector`, `websearch_to_tsquery`, ranking functions — everything you need for the lexical half, in the database you already run |

Why hybrid matters here: vector search alone is bad at exact strings (skill names, character
names, "URA Finals"); lexical search alone is bad at paraphrases.

Experiments: `3.6`, `3.7`.

## Query transformation

| Resource | Why |
|---|---|
| [Precise Zero-Shot Dense Retrieval without Relevance Labels (HyDE, 2022)](https://arxiv.org/abs/2212.10496) | The hypothetical-document technique: embed a generated answer instead of the question |
| [RAG-Fusion (2024)](https://arxiv.org/abs/2402.03367) | Multi-query generation plus rank fusion — the theory behind the multi-query experiment |

Experiment: `3.8`.

## Prompting and grounding

| Resource | Why |
|---|---|
| [Lost in the Middle (2023)](https://arxiv.org/abs/2307.03172) | Empirical evidence that models attend most to the start and end of a long context — directly relevant to how you order retrieved chunks |

Practical rules this repo already follows: restrict the model to the provided context, require
citations, allow "I don't know", and keep temperature low.

Experiment: `3.3`.

## Evaluation

| Resource | Why |
|---|---|
| [RAGAS paper (2023)](https://arxiv.org/abs/2309.15217) | Defines faithfulness, answer relevance, and context precision/recall — the vocabulary for evaluating RAG |
| [RAGAS docs](https://docs.ragas.io/) | Reference implementation of those metrics, including reference-free variants |
| [DeepEval](https://github.com/confident-ai/deepeval) | Alternative evaluation framework with RAG-specific metrics |
| [Azure AI Foundry: evaluation approach for generative AI](https://learn.microsoft.com/azure/ai-foundry/concepts/evaluation-approach-gen-ai) | How Microsoft frames groundedness/relevance evaluation in production |

Start simpler than these: hit rate@k and MRR over your baseline question set need no library and
catch most regressions.

Experiment: `3.9`.

## Advanced RAG

| Resource | Why |
|---|---|
| [Self-RAG (2023)](https://arxiv.org/abs/2310.11511) | The model decides *whether* to retrieve and critiques its own output with reflection tokens |
| [GraphRAG (2024)](https://arxiv.org/abs/2404.16130) | Retrieval over an extracted knowledge graph; strong for "summarise across many documents" questions that vector search handles badly |
| [Anthropic: Contextual Retrieval](https://www.anthropic.com/news/contextual-retrieval) | Also belongs here: combining contextual chunk embedding with hybrid search and reranking |
| [Semantic Kernel docs](https://learn.microsoft.com/semantic-kernel/) | The higher-level .NET framework to move to if you want built-in RAG orchestration, planners, and function calling instead of hand-rolled code |

## RAG implementations worth reading

| Resource | Why |
|---|---|
| [dotnet/eShopSupport](https://github.com/dotnet/eShopSupport) | A real, complete .NET RAG application (ingestion pipeline, chunking, evaluation harness, chat) — the best "what production-ish looks like" reference in .NET |
| [dotnet/ai-samples](https://github.com/dotnet/ai-samples) | Focused samples, including RAG patterns and evaluation |

## Plumbing you'll actually touch in this repo

Not learning material — just the pieces of this codebase that involve RAG-specific mechanics.

| Resource | Why |
|---|---|
| [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) | The `IChatClient` / `IEmbeddingGenerator` abstractions the pipeline is written against |
| [Microsoft.Extensions.AI API reference](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai) | Signatures for `GenerateAsync`, `GetResponseAsync`, `ChatOptions`, `Embedding<T>` |
| [EF Core: SQL queries](https://learn.microsoft.com/ef/core/querying/sql-queries) | `Database.SqlQuery<T>` — how the retrieval query is executed and parameterised |
| [EF Core: value conversions](https://learn.microsoft.com/ef/core/modeling/value-conversions) | The `float[]` → `pgvector.Vector` mapping |
| [EF Core: value comparers](https://learn.microsoft.com/ef/core/modeling/value-comparers) | Why the embedding property needs a comparer |

## Model endpoints (OpenAI-compatible)

| Resource | Why |
|---|---|
| [LM Studio: OpenAI-compatible endpoints](https://lmstudio.ai/docs/app/api/endpoints/openai) | The default provider here: `/v1/chat/completions` and `/v1/embeddings` against local models |
| [LM Studio: OpenAI compatibility notes](https://lmstudio.ai/docs/developer/openai-compat) | Which parameters are supported/ignored — matters when a request silently behaves differently |
| [Ollama: OpenAI compatibility](https://docs.ollama.com/api/openai-compatibility) | Drop-in alternative to LM Studio; same config keys, different `BaseUrl` |
| [OpenAI API reference](https://platform.openai.com/docs/api-reference) | The wire format every "OpenAI-compatible" server imitates |
