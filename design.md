# Gram UI/UX Design System Specification (`design.md`)

> **Version:** 2.1  
> **Design North Star:** Linear, Raycast, Vercel Geist — high-craft, keyboard-first developer ergonomics  
> **Purpose:** The definitive, persistent constitution for designing and implementing all UI components, views, layouts, and UX flows across Gram. Both human engineers and autonomous coding agents MUST follow this specification and strictly enforce the **Anti-Vibe-Coding Ban List** in Section 8.

---

## 1. Core Design Philosophy

1. **Precision Over Ornamentation**  
   Every border, corner radius, and pixel of padding is mathematically deliberate. We never use loud gradients, skeuomorphic drop shadows, floating neon blobs, or decorative filler that distracts from developer tasks.
2. **Spatial Calm & High-Density Utility**  
   Focal interaction areas (agent prompt, conversation timeline, code diff review) have generous negative space. Peripheral chrome (navigation rails, headers, status bars) is compact (`28px–32px` row heights), tabular, and scannable.
3. **Subtle Depth via Hairlines & Luminance**  
   Elevation is communicated through **1px solid micro-borders** (`rgba(255, 255, 255, 0.06)` to `0.10`) and subtle luminance steps (`#08090a` -> `#0f1012` -> `#151619`), never heavy blurred shadows or dashed boxes.
4. **Vector Iconography Only (Zero Emojis)**  
   All icons must be monochrome geometric SVGs (`14px` or `16px`, `1.5px` stroke width, `stroke="currentColor"`, `fill="none"`). Unicode emojis (`🚀`, `✨`, `⚡`, `📁`, `🔍`, `🩺`) are **strictly forbidden** in UI chrome.
5. **Monochrome Authority with Restrained Accents**  
   Surfaces, borders, and typography are achromatic/neutral. Color is reserved exclusively for semantic status (6px status dots, diff additions/deletions) and focus indicators (`--accent: #5e6ad2`).

---

## 2. Layout Architecture & Anatomy

```
+-----------------------------------------------------------------------------------------+
|                                    OUTER APP SHELL                                      |
|  +--------------------+  +-----------------------------------------------------------+  |
|  |                    |  |                   FLOATING INSET STAGE                    |  |
|  |  WORKSPACE HEADER  |  |  (Inset container: radius 14px, 1px hairline border)      |  |
|  |  (Badge + Switcher)|  |                                                           |  |
|  |                    |  |                  [ ARCHITECTURAL MOTIF ]                  |  |
|  |  NAVIGATION RAIL   |  |                                                           |  |
|  |  - Inbox           |  |                   HERO TITLE & SUBTITLE                   |  |
|  |  - Changes         |  |                                                           |  |
|  |  Workspace         |  |  +-----------------------------------------------------+  |  |
|  |    - Files         |  |  |                 AGENT COMPOSER CARD                 |  |  |
|  |    - Preview       |  |  |  Ask Gram...                                        |  |  |
|  |  System            |  |  |  [svg Model ▾] [main]                   [svg] [svg] |  |  |
|  |    - Diagnostics   |  |  +-----------------------------------------------------+  |  |
|  |    - Mesh Nodes    |  |                                                           |  |
|  |                    |  |  STARTER / EXAMPLE CARDS GRID                             |  |
|  |  Recent Tasks      |  |  Get started with some examples                      [x]  |  |
|  |  - [Task Row]      |  |  +---------------+  +---------------+  +---------------+  |  |
|  |                    |  |  | [16px SVG]    |  | [16px SVG]    |  | [16px SVG]    |  |  |
|  |                    |  |  | Title         |  | Title         |  | Title         |  |  |
|  |  (?) Help          |  |  | Description   |  | Description   |  | Description   |  |  |
|  |                    |  |  +---------------+  +---------------+  +---------------+  |  |
|  +--------------------+  +-----------------------------------------------------------+  |
|  ● Ready  Gram v2                                               port 7337   Terminal ^  |
+-----------------------------------------------------------------------------------------+
```

---

## 3. Design Tokens (CSS Variables)

### 3.1 Color & Surface Tokens

| Token Name | Dark Mode (Default) | Light Mode | Usage |
| :--- | :--- | :--- | :--- |
| `--bg-app` | `#08090a` | `#f3f4f6` | Outermost window frame & navigation rail |
| `--bg-stage` | `#0f1012` | `#ffffff` | Floating main canvas container |
| `--bg-surface` | `#151619` | `#f8f9fa` | Composer card, message cards, secondary panels |
| `--bg-surface-elevated` | `#1c1d21` | `#ffffff` | Modals, dropdown menus, active tabs |
| `--bg-hover` | `rgba(255, 255, 255, 0.04)` | `rgba(0, 0, 0, 0.04)` | Hover states for rows, cards, and ghost buttons |
| `--border-subtle` | `rgba(255, 255, 255, 0.06)` | `rgba(0, 0, 0, 0.07)` | Hairline card borders, stage outline, dividers |
| `--border-medium` | `rgba(255, 255, 255, 0.12)` | `rgba(0, 0, 0, 0.13)` | Hover borders, active control outlines |
| `--border-strong` | `rgba(255, 255, 255, 0.22)` | `rgba(0, 0, 0, 0.24)` | Focused input containers |
| `--text-primary` | `#ededef` | `#111315` | Primary headings, active labels, body input |
| `--text-secondary` | `#8a8f98` | `#525866` | Subtitles, card descriptions, inactive nav items |
| `--text-muted` | `#5c6066` | `#868c98` | Section headers, placeholders, timestamps |
| `--accent` | `#5e6ad2` | `#4f5bd5` | Focus rings, active selection accents |
| `--accent-subtle` | `rgba(94, 106, 210, 0.14)` | `rgba(79, 91, 213, 0.10)` | Subtle tint for selected items |
| `--success` | `#26b567` | `#16a34a` | Completed steps, online status dot |
| `--danger` | `#e5484d` | `#dc2626` | Stop action, failed steps, diff deletions |
| `--warning` | `#e5993e` | `#d97706` | Pending state, warnings |

### 3.2 Spacing & Radii Scales

```css
--radius-xs: 4px;      /* Keyboard hints, micro-badges, tree rows */
--radius-sm: 6px;      /* Buttons, nav links, dropdown pills */
--radius-md: 8px;      /* Starter cards, plan containers */
--radius-lg: 10px;     /* Composer card, timeline cards, modals */
--radius-xl: 14px;     /* Inset floating main canvas stage */
--radius-full: 9999px; /* Circular submit button, status dots */
```

---

## 4. Typography Hierarchy

* **Primary UI Font:** `-apple-system, BlinkMacSystemFont, "Inter", "SF Pro Text", "Segoe UI", sans-serif`
* **Monospace Font:** `ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, monospace`
* **Numeric Discipline:** Apply `font-variant-numeric: tabular-nums` to all ports, timestamps, line numbers, badge counts, and durations.
* **Heading Wrapping:** Apply `text-wrap: balance` to hero titles and modal headings; apply `text-wrap: pretty` to body paragraphs.

| Level | Size | Weight | Line Height | Tracking | Usage |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Hero Title** | `22px` | `500` (Medium) | `1.25` | `-0.022em` | Central stage greeting ("Welcome to Gram") |
| **Hero Subtitle** | `13.5px` | `400` (Regular) | `1.5` | `-0.005em` | Sub-hero instruction |
| **Section Header** | `11px` | `500` (Medium) | `1.3` | `+0.01em` | Sidebar group labels (`Workspace`, `Recent Tasks`) |
| **Nav / Card Title** | `13px` | `500` (Medium) | `1.4` | `-0.008em` | Sidebar items, starter card titles |
| **Body / Description**| `12.5px` | `400` (Regular) | `1.5` | `normal` | Card descriptions, message copy |
| **Monospace Meta** | `11px` | `400` (Regular) | `1.4` | `normal` | File paths, git branches, ports, logs |

---

## 5. Component Specifications

### 5.1 Inset Canvas Stage
* Sits inside the outer app shell with `margin: 6px 8px 6px 0`, `border-radius: var(--radius-xl)` (`14px`), and `border: 1px solid var(--border-subtle)`.
* Background is `var(--bg-stage)` (`#0f1012`), distinct from the `#08090a` navigation rail.

### 5.2 Agent Composer Card
* `max-width: 660px`, `border-radius: var(--radius-lg)` (`10px`), `border: 1px solid var(--border-subtle)`, `background: var(--bg-surface)`.
* **Textarea:** Borderless, `13.5px` text, `padding: 12px 14px 6px`, auto-resizing without layout shift.
* **Toolbar:**
  * Left: Custom-styled skill/model selector pill with a `12px` geometric SVG icon and custom SVG chevron (`appearance: none` on `<select>`).
  * Right: SVG attachment button and a `26px × 26px` circular submit button (`border-radius: 50%`) containing a crisp `14px` upward arrow SVG.

### 5.3 Starter Cards Grid
* 3-column grid (`gap: 10px`, `max-width: 660px`).
* Each card has `border: 1px solid var(--border-subtle)`, `border-radius: var(--radius-md)` (`8px`), `padding: 14px`, and a `15px` monochrome SVG icon at the top left.
* Hover state changes only `background: var(--bg-hover)` and `border-color: var(--border-medium)` — **never** `transform: translateY()` or `scale()`.

### 5.4 Navigation Rail
* `232px` width, `padding: 8px`.
* Top workspace switcher with a `20px × 20px` monochrome graphite badge (`#1c1d21`, `border: 1px solid var(--border-medium)`), workspace name, SVG chevron, and SVG icon actions.
* `28px` height nav rows with `14px` monochrome SVG icons (`opacity: 0.65`, `1.0` when active).

---

## 6. Motion & Micro-Interactions

* **Timing:** `120ms` to `160ms` using `cubic-bezier(0.16, 1, 0.3, 1)`.
* **No Bounce or Scale-Up:** Never scale cards or buttons up on hover (`transform: scale(1.05)` is banned).
* **No Browser Native Popups:** Never use `window.alert()`, `window.confirm()`, or `window.prompt()`. Use inline confirmation states or non-blocking status notifications.

---

## 7. Pre-Ship Verification Checklist

- [ ] **Zero Emojis:** Are 100% of icons rendered via crisp SVG paths (`stroke="currentColor"`) with zero Unicode emojis?
- [ ] **Hairline Discipline:** Are all borders `1px solid` with low-opacity tokens (`var(--border-subtle)`) and zero `dashed` borders?
- [ ] **Tabular Numbers:** Do all counters, ports, line counts, and timestamps use `tabular-nums`?
- [ ] **Custom Form Chrome:** Do `<select>` and `<input>` elements strip default OS styling (`appearance: none`)?
- [ ] **Honest States:** Are loading, empty, error, and active states handled inline without `alert()`/`confirm()`?

---

## 8. The Anti-Vibe-Coding Ban List (90 Identifiers & Mistakes to Strictly Avoid)

> **CRITICAL DIRECTIVE FOR AGENTS & ENGINEERS:**  
> The following **90 UI/UX mistakes** are the statistical fingerprints of generic, "vibe-coded" AI-generated websites ("AI slop"). When creating or modifying ANY UI component, page, or stylesheet in Gram, the agent **MUST NOT** introduce any of these patterns under any circumstances.

### Category A: Color, Lighting & Gradient Clichés (1–15)
1. **NO Purple-to-Blue / Indigo-to-Violet Gradients:** Never use `linear-gradient` shifting from indigo (`#6366f1`) to purple (`#a855f7`) or pink (`#ec4899`) on backgrounds, buttons, or borders.
2. **NO Gradient Text Headings:** Never apply `background-clip: text; color: transparent` with a color gradient on `<h1>`, `<h2>`, or hero titles.
3. **NO Floating Background Glow Blobs / Orbs:** Never place blurred radial gradient `div`s (`filter: blur(80px)`) behind hero sections or cards.
4. **NO Neon / Cyberpunk Glow Shadows:** Never use colored drop shadows like `box-shadow: 0 0 20px rgba(59, 130, 246, 0.5)`.
5. **NO Default Tailwind Blue-500 (`#3b82f6`) or Emerald-500 (`#10b981`) Splattering:** Never use raw, uncalibrated default Tailwind palette primaries for brand surfaces or avatars.
6. **NO Clashing Neutral Families:** Never mix warm grays (`stone`), cool blue-grays (`slate`), and pure grays (`zinc`) in the same view. Stick strictly to the defined `--bg-*` tokens.
7. **NO High-Chroma Rainbow Badges:** Never scatter bright green, blue, purple, yellow, and pink pill badges across a single view.
8. **NO Cream / Oatmeal Editorial Backgrounds (`#f9f6f0`) on Developer Tools:** Never default to warm beige/cream backgrounds with thin serif headings for utility interfaces.
9. **NO Faux-Glassmorphism Overuse:** Never stack multiple translucent `backdrop-filter: blur()` cards over busy backgrounds where text contrast degrades.
10. **NO Animated Conic / Rotating Gradient Borders:** Never put spinning rainbow or glowing laser borders around inputs or cards.
11. **NO Pure `#000000` with `#ffffff` Text Burn:** Avoid raw `#000` against `#fff`; use calibrated near-black (`#08090a` / `#0f1012`) and off-white (`#ededef`).
12. **NO Saturated Status Backgrounds:** Never fill an entire alert card or badge with `100%` saturated red or green; use `10%–14%` opacity tints (`--danger-subtle`, `--success-subtle`).
13. **NO Decorative Dot-Matrix or Graph-Paper Backgrounds:** Never cover the main canvas in repeating radial-gradient dots or CSS grid lines unless it is an actual node canvas.
14. **NO Random Colored Icon Bubbles:** Never place icons inside brightly colored circular backgrounds (e.g. a blue icon inside a light-blue circle) inside feature cards.
15. **NO Uncalibrated Opacity Hacks:** Never guess random hex alpha suffixes like `#3b82f633` inline; use semantic tokens.

### Category B: Emoji & Iconography Sins (16–28)
16. **NO Emojis as UI Icons:** Never use Unicode emojis (`🚀`, `⚡`, `🔍`, `📁`, `⚙️`, `🩺`, `📱`, `👁️`, `✏️`, `📥`, `📦`, `📄`, `🟢`) as navigation icons, button icons, or card graphics.
17. **NO "Sparkle" (`✨`) AI Pills:** Never put a pill badge above a hero heading reading *"✨ AI-Powered"*, *"✨ Introducing v2.0"*, or *"● Stateless Runtime Ready"*.
18. **NO ASCII / Text Arrows as Icons:** Never use literal characters like `▼`, `▲`, `›`, `✕`, `☰`, or `↻` in place of proper crisp SVG chevrons, close icons, or spinners.
19. **NO Mixed Icon Stroke Weights:** Never mix `1px`, `1.5px`, and `2.5px` stroke icons or mix filled FontAwesome icons with thin-stroke Lucide icons.
20. **NO Oversized Card Icons:** Never render `32px` or `40px` icons inside compact starter cards; keep functional icons at `14px–16px`.
21. **NO Decorative Emojis in Log Prefixes or Status Messages:** Never prefix log lines or status updates with `🚀`, `✅`, `⚠️`, or `🧠`.
22. **NO Broken / Unaligned Icon Baselines:** Never place an icon next to text without `display: inline-flex; align-items: center`.
23. **NO Unlabeled Icon-Only Buttons:** Every icon-only button must have an explicit `title` and `aria-label`.
24. **NO Spinning Gear Emojis for Loading:** Use a clean SVG spinner or subtle opacity pulse, never a rotating emoji.
25. **NO Cartoon / 3D / Isometric Illustrations:** Never embed generic 3D blobs or corporate Memphis illustrations in empty states.
26. **NO Dashed-Circle SVG Watermarks:** Avoid cheesy `stroke-dasharray` radar circles or clip-art lightning bolts as background watermarks.
27. **NO Checkmark Emoji (`✅`) in Plans:** Use a crisp `12px` SVG checkmark or semantic status dot.
28. **NO Hardcoded SVG Fill Colors:** Always bind SVG `stroke` or `fill` to `currentColor` so icons inherit text state transitions seamlessly.

### Category C: Typography & Copywriting Tells (29–42)
29. **NO AI Marketing Buzzwords:** Never write copy containing *"supercharge"*, *"unleash"*, *"next-gen"*, *"seamless"*, *"elevate"*, *"revolutionize"*, *"empower"*, or *"effortlessly"*.
30. **NO Em-Dash (`—`) Abuse in UI Copy:** Avoid stitching clauses together with em dashes in titles, subtitles, or descriptions.
31. **NO Random Italicized or Colored Emphasis Words in Headlines:** Never style a hero title like *"Build better software, *effortlessly*"*.
32. **NO All-Caps Letter-Spaced Sentences:** Reserve `text-transform: uppercase` strictly for 1–2 word micro-headers (`10px–11px`), never full sentences or buttons.
33. **NO Jarring Font-Weight Jumps:** Avoid pairing `font-weight: 800` extra-bold titles directly above `font-weight: 300` light text; use subtle steps (`400` regular, `500` medium, `600` semi-bold).
34. **NO Missing `tabular-nums` on Dynamic Numbers:** Never allow timers, port numbers, or badge counts to jitter horizontally when numbers change.
35. **NO Center-Aligned Multi-Line Body Copy in Cards:** Never center-align text inside starter cards, message cards, or logs; always left-align (`text-align: start`).
36. **NO Oversized SaaS Landing Page Headings inside App UI:** App stage titles should be `20px–24px`, never `48px–64px` marketing hero sizes.
37. **NO Cramped Line Height on Code/Logs:** Never set `line-height: 1.1` or `1.2` on multi-line code or log blocks; use `1.5` to `1.6`.
38. **NO Unbalanced Orphan Words in Titles:** Always use `text-wrap: balance` on headings so a single word doesn't wrap onto its own line.
39. **NO Generic Placeholder Copy:** Never leave *"Lorem ipsum"*, *"John Doe"*, *"Acme Corp"*, or *"$0.012 / 4.2k tokens"* hardcoded in the UI.
40. **NO Exclamation Marks in UI Labels or Alerts:** Keep system feedback calm, factual, and professional (e.g., *"Changes committed to host"* instead of *"Changes committed successfully!"*).
41. **NO Redundant Self-Referential Labels:** Avoid labeling a search input *"Search Input"* or a task list *"List of Tasks"*.
42. **NO Untruncated Overflowing Titles:** Sidebar items and file paths must use `overflow: hidden; text-overflow: ellipsis; white-space: nowrap`.

### Category D: Layout, Spacing & "Card-Ception" Tropes (43–57)
43. **NO Card-Ception (Cards Nested Inside Cards Inside Cards):** Never wrap a bordered card inside another bordered card inside a bordered panel. Use hairlines or spacing instead.
44. **NO Excessive Border Radius (`24px+` Pill Boxes):** Never apply `border-radius: 24px` or `32px` to rectangular content cards; keep cards at `8px–10px` and the main stage at `14px`.
45. **NO Outer Margin > Inner Padding in Nested Containers:** When nesting an element inside a rounded container, `inner_radius = outer_radius - padding`.
46. **NO Symmetrical Marketing Bento Grids in Utility Views:** Do not force operational data into decorative bento boxes when a clean list or split pane is clearer.
47. **NO Dashed Borders (`border: 1px dashed`) on Content Cards:** Dashed borders look like unfinished wireframes or dropzones; use `1px solid var(--border-subtle)`.
48. **NO Full-Width Stretched Reading Lines:** Never let conversation messages or prose stretch beyond `720px–760px` (`65ch–75ch`) on wide monitors.
49. **NO Uneven Padding in Bars and Headers:** Keep header and toolbar horizontal padding consistent (`12px` or `16px`) and vertically centered (`align-items: center`).
50. **NO Competing Primary Buttons:** Never place two primary-styled accent buttons side by side in the same toolbar or header.
51. **NO Cramped Composer Footers:** Always maintain clear separation between left context controls and right submission controls.
52. **NO Permanently Pinned Empty Docks:** Never leave an empty terminal or empty inspector permanently consuming 30% of the screen.
53. **NO Double Borders Between Adjacent Panels:** When two panels meet (e.g., sidebar and stage, or file tree and editor), use a single `1px` border, never two adjacent `1px` borders (`2px` seam).
54. **NO Scrollbars on Outer App Shell:** `body` and `.app-main` must be `overflow: hidden`; only designated inner scroll containers (`conversation-timeline`, `sidebar-section`, `logs-content`) may scroll.
55. **NO Unstyled Native Scrollbars:** Apply `scrollbar-width: thin; scrollbar-color: rgba(255,255,255,0.12) transparent` so scrollbars blend into the dark theme.
56. **NO Floating Action Buttons (FABs) Covering Content:** Never float circular buttons over scrollable text without reserving bottom scroll padding.
57. **NO Desktop-Only Fixed Pixel Widths Without Mobile Breakpoints:** Every split pane must gracefully collapse or convert to an overlay drawer below `900px`.

### Category E: CSS, Styling & Motion Sloppiness (58–73)
58. **NO Hover Scale / Bounce (`transform: scale(1.04)` or `translateY(-4px)`):** Elements in professional developer tools do not jump or inflate on hover.
59. **NO `transition: all 0.3s ease` Blanket Rules:** Never animate `all` over slow `300ms–500ms` durations; transition specific properties (`background-color, border-color, color, opacity`) over `120ms`.
60. **NO Native OS `<select>` Chrome Leaking:** Always set `appearance: none; -webkit-appearance: none` on `<select>` elements and render a custom SVG chevron.
61. **NO Scattered Inline Styles (`style="..."`) for Core Layout:** Define reusable semantic classes in the stylesheet rather than littering HTML templates with inline style strings.
62. **NO Missing `color-scheme: dark` on `:root`:** Always declare `color-scheme: dark` so native browser scrollbars, inputs, and selection menus match the dark surface.
63. **NO `outline: none` Without `:focus-visible` Replacement:** Never strip keyboard focus indicators without providing a crisp `:focus-visible` ring.
64. **NO Layout Shift on Active/Focus Border:** Never change `border-width` from `1px` to `2px` on hover or focus (which shifts layout by 2px); change `border-color` or `outline`.
65. **NO Harsh Box-Shadows on Flat Cards:** Keep card shadows subtle (`0 2px 8px rgba(0, 0, 0, 0.2)`) or rely strictly on `1px` surface luminance borders.
66. **NO Z-Index Wars (`z-index: 999999`):** Use a disciplined z-index scale (`10` composer, `20` work area, `30` header/strip, `50` mobile drawer, `100` modal).
67. **NO Text Selection on UI Chrome:** Apply `user-select: none` to buttons, headers, tabs, and navigation rails while keeping `user-select: text` on messages, code, and logs.
68. **NO Janky Height Animations Without Overflow Control:** Ensure collapsible drawers use `overflow: hidden` during height transitions.
69. **NO Unconstrained `<textarea>` Resize Handles:** Always set `resize: none` on auto-expanding chat composers so the ugly diagonal browser resize grip doesn't appear.
70. **NO Hardcoded Magic Pixel Offsets:** Use flexbox/grid alignment instead of `top: 13px; left: 7px`.
71. **NO Missing `min-width: 0` on Flex Children:** Always set `min-width: 0` on flex children containing text truncation or code editors so they don't blow out the viewport width.
72. **NO Flash of Unstyled Content in Empty Editors:** Style editor containers with the exact background color (`#0f1012` / `#14161a`) before Monaco initializes.
73. **NO Ignoring `prefers-reduced-motion`:** Respect `@media (prefers-reduced-motion: reduce)` by disabling non-essential transitions.

### Category F: UX, Interaction & Functional Anti-Patterns (74–90)
74. **NO Browser `alert()`, `confirm()`, or `prompt()` Dialogs:** Never block the main thread with ugly OS alert popups; use inline confirmation buttons or status banners.
75. **NO Dead-End / Dummy Buttons:** Every button in the UI (e.g., Attach context, Help, Switcher, Search) must perform a real, functional action or open a valid view.
76. **NO Fake Hardcoded Telemetry:** Never display fake token counts (`4.2k tokens`), fake costs (`$0.012`), or fake latency numbers.
77. **NO Sample / Dummy Tabs on Startup:** Never boot the workspace with fake files like `LoginPage.cs` or `Welcome.tsx` open.
78. **NO Silent Button Clicks:** Every async action (Run Task, Commit to Host, Doctor, Refresh) must immediately reflect a loading/executing state in the UI.
79. **NO Auto-Opening Heavy Panels on Fresh Tasks:** Keep the Review Work Area collapsed on fresh tasks until the user or task output calls for inspection.
80. **NO Losing User Input on Tab Switch:** Switching between Changes, Preview, and Files or opening modals must never clear the composer textarea.
81. **NO Unescaped HTML Injection (`innerHTML` XSS):** Always escape dynamic user prompts, file paths, and task titles via `escapeHtml()` before rendering.
82. **NO Missing Keyboard Shortcuts:** Support `Enter` to submit, `Shift+Enter` for newline, `⌘K` / `Ctrl+K` for search focus, and `Escape` to close modals/drawers.
83. **NO Ambiguous Empty States:** When a list is empty (e.g. 0 modified files or 0 recent tasks), explain *why* it is empty and what action populates it.
84. **NO Cluttered Multi-Submit Buttons:** Never show competing "Run Plan" and "Execute" buttons; provide a single primary submit action that switches to "Stop" while running.
85. **NO Forcing CLI Commands on the User:** Never tell the user to go run `./gram doctor` or `./gram graph` in a terminal; let the agent invoke skills autonomously or provide 1-click UI inspection.
86. **NO Destructive Actions Without Pre-Write Backups:** Committing virtual overlay changes to host must preserve `.gram/backups/` snapshots.
87. **NO Unresponsive Narrow-Window Squeezing:** Never squeeze the conversation timeline and a 520px code diff side-by-side on a `< 900px` screen.
88. **NO Hidden Error States:** If a fetch or run fails, surface the error clearly in the timeline and status strip rather than failing silently in `console.error`.
89. **NO Stale Badge Counts:** After committing changes or completing a run, immediately refresh the modified-files badge and task history list.
90. **NO Disconnected Context Controls:** When the user clicks the attach/context button in the composer, provide a functional file/symbol context selector or tag insertion.
