# Godot host infrastructure

This tree is part of the root `OnlyWarGodot` host project. It is intentionally outside the
headless modules because its presentation layer owns Godot types and startup/path wiring.

- `Presentation/` contains the host-owned presentation models, layout solvers, palettes, styles,
  icon services, and screen-specific presentation builders.
- `Composition/` contains Godot startup and user-path wiring.

The host namespace is `OnlyWar.Host`; gameplay policy, domain models, persistence, and simulation
remain in the projects under `Modules/`. New Godot-facing helpers belong here rather than in the
legacy root `Helpers`, `Models`, `Builders`, or `Composition` directories.
