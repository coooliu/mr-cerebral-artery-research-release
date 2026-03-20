# Scene Setup

This document describes the **minimal scene wiring** used in the research-release demo.

It is intended to help readers reconnect the released scripts and runtime assets for inspection of the linked 2D–3D prototype. It is **not** a full reconstruction guide for the original internal Unity/HoloLens project.

## Scope

The released demo scene is organized around these core systems:

- **`Vessel`** — 3D vessel rendering (`VesselViewer`)
- **`tree`** — 2D topology rendering (`TreeLayoutBezierRenderer`)
- **`StudySystem`** — task flow and progress control (`StudyController`)
- **`EndpointSystem`** — 3D endpoint markers (`EndpointMarkers`)
- **`TreeEndpointSystem`** — 2D endpoint markers (`TreeEndpointMarkers`)
- **`HeadPathTrackerGO`** — head-motion logging (`HeadPathTracker`)

The scene also includes two task-panel configurations:

- **`TaskControlPanel3D`** for the **3D-only** condition
- **`SlateBlank`** for the **2D+3D** condition

## Main scene structure

In the sample scene:

- `StudyController` is attached to **`StudySystem`**
- `VesselViewer` is attached to **`Vessel`**
- `TreeLayoutBezierRenderer` is attached to **`tree`**
- `EndpointMarkers` is attached to **`EndpointSystem`**
- `TreeEndpointMarkers` is attached to **`TreeEndpointSystem`**
- `HeadPathTracker` is attached to **`HeadPathTrackerGO`**

Within `SlateBlank`:

- `LeftPane` hosts the **2D+3D task panel**
- `RightPane` hosts the **`tree`** object and **`TreeBackPlate`**

## Inspector wiring

The sample scene shows the following key references:

### StudyController

`StudyController` references:

- `panel`
- `headTracker`
- `vessel`
- `endpointMarkers`
- `treeEndpointMarkers`
- `tree`
- `panel3DOnlyRoot`
- `panel2D3DRoot`
- `panel3DOnly`
- `panel2D3D`

### VesselViewer

`VesselViewer` is configured on `Vessel` and uses materials for default, transparent, outline, and selection rendering. The sample scene also keeps MRTK manipulation-related components on the vessel object.

### TreeLayoutBezierRenderer

`TreeLayoutBezierRenderer` is configured on `tree` and references:

- `nodePrefab`
- `nodeParent`
- `linePrefab`
- `contentBackPlate`

In the sample scene, the 2D graph is placed on **`TreeBackPlate`** under `RightPane`.

### Endpoint systems

- `EndpointMarkers` references the 3D vessel and start/end marker prefabs.
- `TreeEndpointMarkers` references the 2D tree, the slate backplate, and 2D start/end marker prefabs.

### HeadPathTracker

`HeadPathTracker` samples the **Main Camera** transform in the sample scene.

## Runtime data requirements

The current scripts expect the following runtime assets:

### Resources

`VesselViewer` loads:

- SWC text asset: `demo_case`
- branch-mapping JSON: `treeLayoutforVesselviewer`

These must be placed in a Unity **`Resources`** folder.

## Minimal setup steps

1. Open the demo scene.
2. Verify that the core objects listed above are present.
3. Reconnect Inspector references if any scene links are missing after export.
4. Put the SWC and 2D layout JSON files into a `Resources` folder.
5. Reassign prefabs or materials if asset references are broken.
6. Confirm that:
   - the 3D vessel renders,
   - the 2D topology graph appears,
   - endpoint markers show in both views,
   - linked selection works,
   - the task panel updates progress, time, and attempts.

## Package assumptions

The provided `manifest.json` indicates a Mixed Reality / OpenXR-based Unity setup including MRTK, OpenXR, URP, and TextMeshPro.

> This scene configuration is provided as part of a paper-oriented research release. It documents the minimal wiring used to inspect the linked 2D–3D prototype and study-control logic, but it is not a full one-click reconstruction of the original internal Unity/HoloLens experiment environment.
