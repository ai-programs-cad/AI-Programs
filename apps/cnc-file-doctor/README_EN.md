# CNC File Doctor 0.7.0

Extract the whole folder and run CNC File Doctor.exe. Offline Windows DXF diagnosis and reviewed ARC/LINE approximation; no CAD application or separately installed Python required. Requires .NET Framework 4.5+; current-machine testing only.

The Windows interface creates results inside each source directory: Parts/Parts_1, Parts/Parts_2, etc. Existing folders are never overwritten. Files from different source directories receive separate result folders. Ready DXFs are directly in these folders. The Processed files button opens the selected result folder; Open report follows the selected row. EXACT stores original copies, Reports stores evidence, Quarantine stores unverified candidates. Recursive discovery excludes previous results. Deliberately selecting a result directory loads its direct DXFs only.

Create CAM_ARCS writes -CNC.dxf candidates. It requires smooth joins and certified deviation before reducing ARC/LINE count; unrestricted global optimality is not proven. Max. deviation, mm accepts presets or a custom decimal comma/point value (default 0.01 mm). Smaller values generally require more arcs and time. Final CUT closure within 0.001 mm is mandatory, alongside the existing stricter engine checks. No automatic endpoint moves or connecting lines are added. UP/DOWN bends are excluded from closure. Default CUT layer is 0. Supported conversion is a limited planar millimetre ASCII/UTF-8 subset: LINE/ARC/CIRCLE/SPLINE and straight zero-width CUT LWPOLYLINE. Unsupported data are rejected. Review candidates in the target CAM system before cutting.

Split into layers opens a criteria dialog: color, linetype, lineweight, entity type. A separate -LAYERS.dxf preserves geometry while resolving ByLayer properties and TrueColor. Original visibility/frozen/locked/plot/material differences are preserved. Supports top-level LINE, ARC, CIRCLE, SPLINE, LWPOLYLINE, ELLIPSE, TEXT, POINT, SOLID, TRACE, 3DFACE. Grouping compares effective top-level properties regardless of direct, ByLayer or ByBlock assignment; linetypes compare pattern rather than name. ByBlock output tokens remain symbolic. External default lineweight is not guessed or equated to an arbitrary numeric value. Unsupported annotation/block/compound entities in mixed drawings are preserved byte-for-byte on original layers and explicitly reported as a partial split. Files with no supported entities are not offered as successful no-op splits. This groups by appearance; it does not infer cutting/engraving/bending roles.

Sources, tests, synthetic samples and build script are included. PartCraft engine origins and local optimization extensions are recorded in docs. Validation includes 99 inherited tests, 55 application tests, native UI integration and copies of real drawings. Clean-Windows/CAM acceptance, signing, installer and Free/Pro licensing remain outstanding. Python license is in runtime/LICENSE.txt; no license has been assigned to the user's source code.

Explicit-output CLI is retained: runtime/python.exe -I src/doctor.py --input FILE_OR_FOLDER --output NEW_FOLDER [--convert] [--tolerance-mm 0.01] [--cut-layer 0]. This compatibility mode retains a CAM_ARCS subfolder and requires a new output directory outside recursively selected inputs. Source-folder automatic naming applies to the Windows interface.


## Diagnostic preview 0.7.0

The large drawing pane resizes with the form and its divider is draggable. Wheel: zoom; left/middle drag: pan; double click or Fit drawing: reset. Next mark focuses an issue or changed segment. Source DXF / Result switches the displayed file. Display-only spline sampling is independent of the conversion certificates.

Red exclamation diamonds locate detected issues, with hover explanations. Amber indicates source splines/polylines; green indicates handles actually replaced by geometric conversion. Unfixed issues remain red, including quarantined candidates. Markers toggles these overlays. TrueColor and the basic ACI palette are contrast-adjusted for display; other indexed colors currently use a neutral fallback. Source red lines alone are not error markers. Preview never changes the DXF. Diagnostics are bounded, can be partial, and do not constitute a complete independent manufacturing/intersection audit.





## Contour roles

Supported closed CUT contours are classified during inspection and after conversion: outer, hole, island. Disjoint parts and deeper nesting are supported. Contours toggles cyan outer, amber hole and violet island colors in the preview. Indices, parent contour and depth appear in the report/details. Classification does not change DXF geometry or layers; unresolved, open or intersecting contours are not certified.


## Single closed contour entities

Successful CAM_ARCS jobs now create a ready -CONTOURS.dxf containing one closed LWPOLYLINE per contour, with signed bulges preserving arcs. Circles remain single CIRCLE entities. Holes remain separate loops. Open DXF launches the ready file in the Windows-associated app. ARC/LINE reference geometry is retained under Reports/*-analytic.dxf. Written geometry, smooth junctions, intersection exclusion and nesting are checked. Added deviation must fit the remaining overall tolerance. Incompatible styles/references/metadata or failed proof can prevent joining, with an explicit explanation; this does not automatically heal arbitrary gaps.


## Interface 0.7.0

Process DXF performs approximation, checks and closed contour joining. The result inspector shows status, outer/hole/island counts, closed entity count, measured deviation and source preservation. Checks passed still requires target CAM review. Elapsed time is displayed while processing. Drop DXFs or folders into the window. Ctrl+O adds files, Ctrl+Enter processes, F5 inspects. Full source paths are available in queue tooltips. Russian and English, resizing and a minimum 1100×820 window were checked. External CAD/CAM apps were not automatically launched in tests. Processing code is identical to 0.6.2.
