# Local Parser UI refresh — implementation report

**Scope:** local Parser Agent desktop UI only
**Server / parser extraction logic:** unchanged

## Problem addressed

Manual verification showed that the Results workspace was functionally useful but visually weak on different window sizes:

- star-sized result columns collapsed instead of exposing horizontal scrolling;
- column headers wrapped into multiple lines and table values visually merged;
- the right details pane had a fixed ratio and could not be resized by the operator;
- the results table did not show the last local parse time;
- buttons, tabs, card padding and global spacing were too large for a workbench-style desktop tool;
- filter controls were oversized and visually stuck together.

## Implemented

- Reduced global desktop typography and spacing to a compact workbench scale.
- Reduced default button height/padding, card padding, tab size, row height, header padding and secondary text.
- Results filters are a wrapping compact toolbar with explicit spacing.
- Added Reset filter action.
- Results columns now have explicit pixel/minimum widths instead of star widths.
- Results grid keeps the first column frozen and uses horizontal scrolling when the viewport is narrower than the full table.
- Added Парсинг column with last observation time and a full timestamp tooltip.
- Replaced the fixed table/details ratio with a real GridSplitter.
- The details pane width is persisted locally and restored on the next launch.
- Separate-window details mode remains unchanged.
- The side details card was compacted without reducing its information content.
- Outer margins and vertical whitespace throughout the workspace were reduced.

## Defaults

- Main UI font: 13.5 px.
- DataGrid: 13 px.
- DataGrid headers: 12 px.
- Main title: 24 px.
- Details title: 17 px.
- DataGrid row height: 38 px.
- Side details default width: 430 px, persisted range 320–760 px.

A separate user font-scale setting is intentionally not added in this iteration. The first goal is a stable compact layout; scale control can be added later only if real usage shows it is still needed.

## Tests added/updated

- result columns must not use star sizing;
- first result column remains frozen;
- the Parse column and splitter are present;
- reset-filter control is present;
- side pane width is persisted across controller restarts.

Automated tests/build are intentionally not executed by the implementation task; manual build and UI verification remain with the owner.
