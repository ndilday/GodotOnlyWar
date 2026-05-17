# OnlyWar Test Project

This project starts with fast regression tests for the systems called out in the TDD:

- wound arithmetic and healing
- skill math
- Gaussian helper math and seeded RNG behavior
- mission check selection rules
- battle soldier clone-state preservation

Next high-value additions:

1. Save/load round-trip tests using a temporary SQLite save.
2. Data validation tests for hardcoded skill/template names.
3. `ForceGenerator` and `SubsectorBuilder` tests with compact fixtures.
4. Seeded multi-turn simulation smoke tests once more global RNG usage is isolated.
