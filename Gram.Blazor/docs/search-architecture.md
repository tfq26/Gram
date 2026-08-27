# Gram Search Architecture

## Goal

Separate live retrieval, evidence evaluation, and answer writing so search-backed answers are current, relevant, source-supported, and cost-controlled.

## Canonical pipeline

```text
Question
  → GroundingPolicy
  → SearchPlan
  → provider adapter
  → normalized sources
  → relevance/authority/freshness ranking
  → evidence packet
  → claim/source ledger
  → model synthesis
  → citation validator
```

## Grounding policy

The policy is topic-independent. It decides whether a question needs no search, live web search, or primary-source research. It also chooses fast versus advanced mode, query count, freshness, and source restrictions.

Recognized topic families include regulation, finance, health, science, software, sports, news, and general research. Unknown topics use open-web retrieval with ranking rather than an arbitrary hard allowlist.

## Provider strategy

- Parallel is the primary provider.
- Tavily is an interchangeable fallback.
- Built-in sources are fallback evidence only.

Parallel receives an objective, focused queries, mode, model, session ID, and—when required—a hard source-policy allowlist. Tavily receives the equivalent plan using its own parameter names. Providers never leak their response shape into the rest of Gram.

## Source evaluation

Each source is normalized with title, URL, content, provider, dates, provider score, authority score, freshness score, and relevance score. Primary-domain status is not enough: an authoritative source must also address the requested topic and claim.

For legal, tax, regulatory, financial, medical, and safety claims, primary sources are required whenever available. Secondary sources may discover leads but should not support the final claim when an authoritative source exists.

## Evidence packet

The packet contains only the strongest relevant source excerpts, URLs, dates, source type, and retrieval metadata. It has a bounded character/token budget and a bounded number of fetched pages.

## Claim validation

The final answer is checked for:

- Citations that point to retrieved URLs.
- Material claims with supporting evidence.
- Unsupported numbers and dates.
- Current versus expired status.
- Contradictions between sources.
- Relevant, not merely authoritative, sources.

If validation fails, Gram repairs the answer from the existing evidence or reports that the evidence is insufficient.

## Cost and latency

Use cache keys for plans, searches, extracted pages, and evidence packets. Prefer one well-designed Parallel request over many redundant searches. Use shallow extraction for ordinary questions and deeper extraction only for complex research.
