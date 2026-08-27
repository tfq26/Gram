# Gram Design Language and UI Guide

## Design direction

Gram should feel like a calm, intelligent workspace: editorial, restrained, dark/light theme aware, and focused on the work rather than application chrome. The interface should avoid dense dashboard styling and excessive pills, cards, badges, and decorative motion.

## Core principles

- Lead with the user’s work.
- Use hierarchy and spacing before borders and containers.
- Keep actions close to the object they affect.
- Prefer one clear primary action per area.
- Make essential state visible without hover.
- Use animation to explain state, not to decorate every interaction.
- Preserve the frameless conversational flow.

## Surfaces

Use transparent layout containers by default. Use a card only for a meaningful bounded object such as an artifact preview, compact metric group, or focused form. Avoid nested cards and repeated dashboard tiles.

## Typography

Use the existing site font and theme tokens. Maintain a clear hierarchy with regular and medium weights. Keep labels concise, secondary text muted, and body text comfortable to scan. Do not shrink essential content to fit a crowded layout.

## Color

Use theme variables rather than hardcoded palettes:

```text
--background
--foreground
--muted
--muted-foreground
--primary
--primary-foreground
--accent
--border
--destructive
--purple
```

Color should communicate state or hierarchy and must be paired with text, shape, or position. Purple is Gram’s accent, not a reason to color every surface.

## Controls

- Use native buttons, inputs, selects, and textareas.
- Keep buttons content-sized unless a full-width action is intentional.
- Use fully rounded treatment for compact status controls and icon highlights.
- Keep focus states intact.
- Make touch targets usable without enlarging the visual design unnecessarily.

## Chat and agent state

Chat remains the primary surface. Tool activity, run status, quota warnings, and validation should appear as quiet inline state near the relevant response. Detailed diagnostics belong behind an intentional expansion, not in the main conversation.

## Artifacts and presentations

Artifacts should open in a focused sidecar or workspace surface. The presentation viewer should emphasize the canvas and narrative, with source and editing controls available but visually subordinate. Generated content should not look like a generic admin dashboard.

## Motion

Use short, purposeful transitions for:

- Status changes.
- Section loading.
- Panel opening.
- Artifact generation progress.
- Mascot state changes.

Honor reduced-motion preferences. Avoid looping or distracting movement while the user is reading.

## Accessibility and responsive behavior

- Use semantic elements and ARIA only where needed.
- Announce important dynamic state with polite live regions.
- Keep content usable from 320px upward.
- Stack rather than horizontally overflow when space is limited.
- Never make hover the only way to discover an action.

## Quality bar

Every new UI element should answer three questions:

1. What user task does this help complete?
2. Why is this surface or control necessary?
3. Can the same result be achieved with less visual weight?

If the answer to the first question is unclear, do not add the element.
