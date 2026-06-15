# Graph2VR Demo Notes 2026

These notes document the demo-oriented changes added after the original 1.x workflow. They are meant as a quick map for rebuilding, testing, and explaining the current development state.

## Query and SPARQL workflow

- Visual query patterns can now be previewed from the menu before execution.
- The query preview panel shows the currently generated SPARQL for the selected or nearest graph.
- The preview panel updates when the graph/query pattern changes.
- Query logging can be toggled from the settings menu.
- When enabled, query logging writes a timestamped session log before the query is sent, so a crash after dispatch should not lose the query.
- Query logs can be exported as text files and are included in serialized application save states.
- `settings.txt` can now contain readable multi-line string values for long SPARQL queries.
- Expansion queries keep the original two-step behavior: one data query for graph triples and one refinement query for labels/images/types.
- If an expansion refinement query times out or fails, Graph2VR falls back to the data-only query where possible.

## Layout and grouping

- Barnes-Hut 3D layout was added for larger graph layouts.
- A Louvain-style cluster layout was added to group densely connected parts of a graph.
- Grouped query results can be arranged as separate graph clusters.
- Order information from result rows is preserved for grouped query layouts where possible.
- Duplicate triples created while rebuilding grouped results are collapsed before graph construction.

## Media nodes

- Image predicates still work as before: images decorate nodes instead of becoming ordinary graph structure.
- 3D model predicates were added for model-backed nodes.
- Runtime OBJ loading is supported.
- Runtime STL loading is supported.
- OBJ materials are loaded when an adjacent material file can be resolved.
- Model nodes are scaled through settings and are not billboarded toward the user.
- Model nodes can be rotated manually so the model can be inspected from all sides.

## Save state coverage

The serialized application state now includes the additional runtime information needed for the new features:

- query log state
- node model URLs and loaded model metadata
- node rotation state
- variable/query pattern state used by query preview and grouped queries
- ordering/grouping-related graph state

N-Triples export remains a separate graph-data export and does not aim to preserve application UI state.

## Settings file notes

The project-local `Assets/_FilesToCopy/Settings.txt` is intentionally ignored by Git because it may contain private endpoints or local database configuration. For demo builds, create or export the settings file locally and copy it next to the Windows executable or to the Quest app data directory:

`sdcard/Android/data/com.Graph2VR.Graph2VR/files`

New settings used by the demo features include:

- `queryLoggingEnabled`
- `modelNodeSize`
- `modelPredicates`

## Known demo risks

- Public SPARQL endpoints such as DBpedia can be slow or timeout, especially for broad incoming expansions or grouped queries.
- The refinement query can be slower than the data query because it asks for labels, images, and type information.
- Quest standalone builds use a different runtime settings location than Windows builds.
- Installing a renamed APK with the same Android package identifier still replaces the existing Graph2VR app.
- Controller recovery after Quest standby may still need follow-up work after the demo.
- DotNetRDF endpoint handling is currently implemented around the existing Virtuoso workflow; GraphDB and other endpoint APIs need dedicated compatibility work.
