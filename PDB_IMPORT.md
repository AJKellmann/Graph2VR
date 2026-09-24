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
  surfaces or trajectories yet. Secondary-structure ribbons use explicit HELIX/SHEET annotations (see below). Unsupported chemistry is not silently distance-bonded.
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

## Media visibility and labels

Settings > New nodes: images/models defaults to on. Each node captures this default when
it is created; changing the setting never changes existing nodes, even if their media
URLs arrive later. The menu preference persists locally via PlayerPrefs
and takes precedence over autoShowNodeMedia in Settings.txt. Each node with known media has
Show as abstract node / Show image/model. Every node's display state is retained in saved
graphs. Older saves with an unspecified state capture the current default when loaded.

Media discovery and RDF edges remain unchanged, including media on both ends of the edge.
When automatic display is off, URLs are retained without starting new image/model downloads.
Existing in-flight downloads may finish, but hidden nodes will not display the result.
Loaded media are deactivated and retained in memory for switching back. Models take display
precedence over images if both exist. Visible 3D models intentionally hide their labels (also for stages). Image and abstract nodes keep labels; missing or
empty RDF labels fall back to a readable URI component (including CUI query identifiers).

## PDB representations

Settings > `PDB settings (new nodes)` opens a submenu with directly selectable defaults
for nodes created afterwards. The active choice is blue and marked `(default)`.
Selection keeps this submenu open; Back returns to Settings. Bonds (0) is the initial default; Atoms (1), Residues (2), Chains (3), Cartoon (4) follow.
The saved preference overrides `pdbRepresentation` in Settings.txt. Existing nodes stay
unchanged. On a PDB node, `PDB: ... (change)` opens the five choices independently of
the existing abstract/media switch. The selected representation is saved with the node;
older saves use Bonds. OBJ/STL models have no PDB representation menu.

- Atoms: element-colored atom balls without sticks.
- Bonds: the original element-colored balls and sticks.
- Residues: one ball at each standard amino acid's C-alpha, connected by backbone links,
  colored by chain. Ligands, metals and residues lacking C-alpha are omitted.
- Chains: smoothed Catmull-Rom backbone tubes colored by chain. This is a schematic
  trace, not secondary-structure cartoons, molecular surfaces or atomic geometry.
- Cartoon: chain-colored helix ribbons and beta-strand arrows over a thin backbone tube.
  Uses PDB HELIX/SHEET annotations, including residue numbers and insertion codes.
  Missing annotations remain tubes; malformed annotations are skipped with a warning.
  No secondary structure is inferred from coordinates. Annotated segments shorter than
  two connected C-alpha positions also remain tubes.

Backbone segments follow identified peptide bonds and respect chain IDs, TER and missing
C-alpha atoms. C-alpha-only files do not have inferred peptide bonds, so their residues
remain disconnected. Structures without standard C-alpha residues cannot use Residues,
Chains or Cartoon; a warning is logged and an existing representation is retained.

Changing representation reuses parsed coordinates without another download, replaces
the generated meshes/materials, and retains coordinate scale and center. The PDB file
remains a single Graph2VR layout node. Mesh generation is synchronous and may briefly
pause on large structures. Verify all five modes visually and on Quest before release.

## Saving and loading

Quick save and application-state `.g2v` saves retain each node's PDB representation,
media visibility, candidate URLs, node transform and exact model-local scale/offset.
The latter also survives saving an abstract node before its model has loaded again.
Older saves without the optional transform use their existing model-size fallback.
PDB/OBJ/STL file contents are not embedded: their URLs must remain accessible when
models are loaded again. Downloaded images are embedded as PNG as before.
N-Triples exports contain RDF triples only, not presentation or local preferences.
The defaults for newly created nodes are persisted separately in local PlayerPrefs.
`Tests/SaveStateChecks.cs` tests BinaryFormatter round trips against the actual compiled
`ApplicationState.NodeState` type, including omitted optional fields for legacy saves.
Compile it as a standalone console executable with Unity's C# compiler and mscorlib,
then run with Unity's bundled Mono, passing the compiled project DLL and the
`Editor/Data/Managed/UnityEngine` directory, followed by `Assets/Plugins/dotNetRDF`, as arguments. It also checks plain, language-tagged, typed and URI-looking literal restoration. It uses only in-memory
streams and does not read or overwrite user saves. It does not exercise Unity rendering.