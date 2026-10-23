# Design System

_Status: draft. Owner: Yeakub (UI/UX)._

- Source of truth: Figma variables → design tokens → Style Dictionary → `frontend/packages/ui`.
- Base: Tailwind + shadcn/ui (Radix). All components documented in Storybook.
- Use only library components and tokens. No raw hex colors or ad-hoc spacing (enforced by lint).
- Accessibility: WCAG 2.2 AA. All text translatable via `packages/i18n`.
