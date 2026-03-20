# Architecture

This document summarizes the **minimal runtime architecture** of the research-release prototype.

It is intended to clarify how the released scripts are organized and how the linked 2D–3D interaction is coordinated. It is **not** a full description of the original internal Unity/HoloLens project.

## Scope

The released prototype is organized into three main layers:

- **3D vessel view** — explicit vessel rendering and segment interaction
- **2D topology view** — abstract tree layout and segment interaction
- **study control layer** — task flow, progress tracking, timing, and logging support

Together, these layers implement the linked-view interaction described in the paper-oriented research release.

## Main components

### 3D vessel view

The 3D view is centered on **`VesselViewer`**.

Its responsibilities include:

- loading vessel centerline / branch data,
- generating explicit 3D vessel geometry,
- supporting segment-level hover and selection,
- updating 3D highlight state in response to linked interaction.

This is the main runtime component for the 3D anatomical representation.

### 2D topology view

The 2D view is centered on **`TreeLayoutBezierRenderer`**.

Its responsibilities include:

- loading the abstract tree layout,
- drawing the 2D topology view,
- supporting hover and click interaction in 2D,
- synchronizing 2D state with the 3D vessel view.

This is the main runtime component for the topology abstraction.

### Study control layer

The study control layer is centered on **`StudyController`**.

Its responsibilities include:

- loading task definitions,
- managing condition-specific UI flow,
- starting and resetting task timing,
- tracking attempts and completion state,
- coordinating endpoint markers and panel updates.

This layer supports the experimental workflow rather than the core rendering method itself.

## Supporting components

The released prototype also includes several helper components:

- **`UIStudyPanel`** — updates task UI, button state, and rating controls
- **`HeadPathTracker`** — measures head-movement distance during a task
- **`EndpointMarkers`** — places start/end markers in the 3D vessel view
- **`TreeEndpointMarkers`** — places start/end markers in the 2D topology view

These scripts support study reproduction and task guidance.

## Interaction flow

The prototype uses a linked-view interaction pattern across the 2D and 3D representations.

At a high level:

1. the user interacts with either the 3D vessel view or the 2D topology view,
2. the corresponding segment state is updated,
3. the linked view is synchronized,
4. the study layer updates progress, timing, and task-completion status.

This means the 2D and 3D views are not independent modules. They are designed to operate as a coordinated interaction pair.

## Data flow

The released scripts assume three main categories of runtime data:

- **3D vessel input** for geometry construction,
- **mapping data** connecting vessel branches to topology segments,
- **2D layout data** for the abstract tree rendering.

In the current research-release code, these assets are loaded from Unity `Resources`, as described in `scene_setup.md`.
