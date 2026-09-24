# PDB ball-and-stick import

PDB URLs now use the existing model route alongside OBJ and STL. No new Unity packages,
native libraries, SteamVR, or dotNetRDF changes are required. The project remains on
Unity 2021.2.15f1. This is a bounded protein viewer, not a general chemistry toolkit.

## Usage

Associate a node with a URL using the configured model predicate, for example:

```turtle
<https://example.org/insulin> <http://graph2vr.org/model> <https://files.rcsb.org/download/4INS.pdb> .
```

Use Graph2VR's existing model display action. `.pdb` extensions are case-insensitive
and query strings/fragments are allowed. Existing model sizing and saved model URLs
apply. File URLs work through the existing loader where the platform permits access;
desktop file paths will not be accessible on a standalone headset.

4INS is **pig insulin**, not human insulin. The importer displays the coordinates
present in the first model of the file, not a selected monomer or a generated biological
assembly. Choose an appropriate entry/coordinate subset when making a poster and cite
the exact structure. There is no chain-selection UI in this initial implementation.

## Representation and scope

- Element-colored balls and single sticks, with the two halves of a bond colored by atom.
- Ordinary C# parser separated from the Unity mesh renderer; no new runtime dependencies.
- Heavy-atom connectivity templates for all 20 standard amino acids. Peptide bonds
  require adjacent parsed residues in the same chain/TER segment and a plausible C-N distance.
- Explicit CONECT records supplement templates; duplicate bonds are collapsed.
- Cysteine SG pairs at 1.8–2.3 Angstroms produce disulfides only when each sulfur has
  one such candidate. This is a geometric fallback, not symmetry/LINK/SSBOND interpretation.
- Present hydrogens on standard residues attach to a nearby heavy atom in that residue;
  missing hydrogens are not generated. Bond orders, aromaticity and protonation are not inferred.
- Unknown residues/ligands/metals/nucleotides use explicit CONECT only; absent connectivity
  produces unconnected balls and a diagnostic. Metal coordination is not guessed.
- The first MODEL is shown. Alternate positions use the highest summed occupancy conformer
  per residue (lexicographic tie-break), plus shared blank positions. Waters are omitted.
- No mmCIF, compressed PDB, crystallographic symmetry, biological assembly generation,
  surfaces, ribbons, or trajectories yet. Unsupported chemistry is not silently distance-bonded.
- Coordinates convert from PDB Angstroms to Unity orientation and existing model normalization.
- Input is limited to 8 MiB of text and 10,000 displayed atoms. Downloading is still handled
  by the existing buffered model loader; this is not a network payload limit.
- Meshes are grouped by element and split below 60,000 vertices. No per-atom GameObjects,
  colliders or behaviours. Generated meshes/materials are disposed when the model is destroyed.
  Parsing and mesh creation currently happen on the main thread; large models can cause a pause.

## Verification

The standalone parser checks require a .NET 8 SDK, not Unity or additional packages:

```powershell
dotnet run --project Tests/Pdb/PdbTests.csproj
```

The bundled 4INS fixture was retrieved from https://files.rcsb.org/download/4INS.pdb
on 2026-09-24 (structure entry: https://www.rcsb.org/structure/4INS). Its SSBOND annotations
provide an independent check of the six disulfides. Optionally pass another insulin PDB
file after `--` (the fixture checks expect unambiguous, local SSBOND annotations).
Tests cover decimal culture, standard connectivity, chain/TER breaks, coordinate gaps,
disulfides, alternate positions, multiple models, CONECT, element alignment, waters,
hydrogens and malformed input. They do not establish visual or Quest compatibility.

Before release, use Unity **2021.2.15f1** to verify: load the same structure in a trusted
molecular viewer; compare chains, atom/bond counts and disulfides; inspect the 3D model,
scale, save/reload and replacement; then build Android ARM64/IL2CPP and run on Quest.
Check stereoscopic rendering, load time, frame rate and memory on the device. Also smoke-test
OBJ/STL loading. Do not open this worktree in Unity 6 as part of the PDB feature.

References: https://www.wwpdb.org/documentation/file-format-content/format33/sect9.html
and https://www.wwpdb.org/documentation/file-format-content/format33/sect10.html
