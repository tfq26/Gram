# Gram Presentation Architecture

## Goal

Create high-quality, visual, editable presentations through independent section work, with HTML as the first artifact format. PowerPoint and PDF export are deferred until the HTML design process is proven.

## Workflow

```text
Presentation request
  → brief and audience
  → narrative/section plan
  → research and evidence
  → section workers
  → asset selection
  → HTML composition
  → validation
  → interactive artifact
```

## Ownership model

Each section has an owner capability, version, dependencies, worker task, content JSON, visual brief, speaker notes, and source IDs. Revising one section regenerates only that section and then re-runs assembly validation.

## Data layers

- `PresentationBrief`: audience, communication job, title, slide count, design direction.
- `PresentationSectionBrief`: narrative purpose, capability owner, dependencies.
- `PresentationSlideContent`: takeaway, subtitle, body points, visual brief, layout, notes, sources.
- `ContentAsset`: local path, type, alt text, source, license, attribution.
- `ContentSource`: URL, label, claim, retrieval timestamp.

## First artifact

The first renderer creates a Gram-managed interactive HTML artifact:

```text
presentation/
  index.html
  assets/
  manifest.json
```

It supports slide navigation, keyboard controls, responsive layout, images, source listings, and future audio/video asset slots. The renderer consumes structured content and escapes generated text before insertion.

## Asset policy

Use user-provided assets and approved licensed web assets. Every external asset requires a source page, creator when available, license, and attribution metadata. Assets are cached locally before assembly.

## Quality gates

- Every section passes its JSON schema.
- Claims have supporting sources.
- Assets have provenance.
- No broken local assets exist.
- Content fits its layout budget.
- The HTML artifact loads and navigation works.
- Section revisions do not alter unrelated sections.

## Later extensions

Add rich visuals, audio narration, video, interactive charts, section comments, artifact versioning, and PPTX/PDF renderers behind the same presentation model. Do not make output format-specific decisions part of section generation.
