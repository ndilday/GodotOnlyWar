# Soldier name pools

These UTF-8, one-name-per-line files provide separate candidate pools for
Space Marine given names and surnames:

- `given_names.txt` contains 1,100 given names.
- `surnames.txt` contains 2,200 surnames.
- `canon_collision_blacklist.txt` records recognizable Warhammer 40,000 names
  excluded during generation.

The pools mix classical, Gothic, northern European, eastern European,
Semitic, central/southern Asian, east Asian, and invented far-future forms.
They are deliberately not tied to one canonical Chapter or recruiting world.
Surnames are built from phonetic stems and endings rather than English martial
or landscape compounds, keeping the pool culturally varied without making one
fantasy naming style dominate.

The entries are synthetic names assembled from curated components. They are
not copied from a published character list. Exact matches are compared
case-insensitively against the blacklist, and the generator rejects duplicate
entries and overlap between the two output pools.

`NameGenerator` embeds the given-name and surname files into the game assembly.
It independently shuffles each pool with Fisher-Yates and draws without
replacement. A pool is reshuffled only after it is exhausted. Player chapter
generation resets both pools before assigning names, so a standard 1,000-Marine
founding has no repeated given names or surnames.

## Regeneration

Run:

```powershell
& <path-to-node.exe> Tools\NamePools\generate_name_pools.mjs
```

The pool-generation tool is deterministic. Regenerating it rewrites all three
text files. Runtime draw order is deterministic when `RNG.Reset(seed)` is
followed by `NameGenerator.Reset()`.
