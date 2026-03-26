# mr-cerebral-artery-research-release

This repository contains the core implementation and demo assets for reproducing the research described in the manuscript:

**Structured Dual-View Interactive Framework for Cerebral Artery Exploration in Mixed Reality: Integrating 2D Abstract Layouts and 3D Models**


## Overview

The released prototype follows a lightweight three-layer structure:

- **3D vessel view** (`VesselViewer.cs`)  
  Loads SWC-based vessel data, generates explicit 3D vessel geometry, and supports segment-level interaction.

- **2D topology view** (`TreeLayoutBezierRenderer.cs`)  
  Loads the abstract vascular layout, renders the topology-aware 2D view, and supports interactive segment selection.

- **Study control layer** (`StudyController.cs` and supporting scripts)  
  Coordinates task flow, endpoint guidance, progress updates, timing, and head-movement logging for study reproduction.

These components are connected through a linked interaction workflow: user actions in either the 2D or 3D view update the shared segment state, synchronize highlighting/selection across views, and trigger study-side progress updates.For a more detailed description of the released research architecture, see `docs/architecture.md`.

In addition to the Unity-based linked-view prototype and demo assets, this repository also provides the user-study metrics and R Markdown analysis materials corresponding to the statistical evaluations reported in Chapters 4 and 5 of the manuscript, see `user_study/`.

## Included Components

### Core Unity Scripts

- **`VesselViewer.cs`**  
  Generates explicit 3D vascular representations from SWC centerline data and supports segment-level interaction.

- **`TreeLayoutBezierRenderer.cs`**  
  Renders the 2D topology-aware abstract layout and supports interactive synchronization with the 3D view.

### Study Reproduction Scripts

- **`StudyController.cs`**
- **`HeadPathTracker.cs`**
- **`EndpointMarkers.cs`**
- **`TreeEndpointMarkers.cs`**
- **`UIStudyPanel.cs`**

### Demo Assets

- **`demo_case.swc`**  
  A demo SWC file used as input for the 3D vessel rendering.

- **`treeLayoutforVesselviewer.json`**  
  A lightweight bridge file for branch mapping / synchronization.

### User Study Data and Statistical Analysis

- one CSV file containing the experimental measurements
- R Markdown source files for Completion Time, Head Movement Distance, Path Strategy Divergence, and Subjective Task Difficulty
- knitted HTML reports corresponding to each analysis stage

## Research Focus

This repository focuses on the methodological core of the paper:

- **SWC-based explicit 3D vessel construction.**
- **Segment-level abstraction and branch mapping.**
- **Topology-guided 2D layout rendering.**
- **Linked 2D–3D highlighting and selection.**
- **Study-oriented task reproduction logic.**

## Minimal Input Files

To run the demo, the following files are required:

- One **swc** file.
- One **2D layout JSON** file.

## Quick Start Guide

### Environment

- **Unity version**: 2022.3.42f1c1
- **MRTK version**: MRTK2.8
- **Target hardware**: HoloLens 2

### Research Release Setup

This repository is a paper-oriented research release rather than a turnkey reproduction package.  
It is intended to help readers inspect the core linked 2D–3D visualization logic, understand the study-control workflow, and explore a minimal demo configuration when the documented Unity/MRTK dependencies are available.For additional setup notes, environment assumptions, and known limitations of this research release, see `docs/scene_setup.md`.

## Limitations

Please review `docs/limitations.md` before making any replication claims. Key limitations include:

- This is a research-oriented release.
- Hospital source imaging data are not included.
- Preprocessing from raw medical images is not fully automated.

## Demo Video

A short demo video is provided in the `media/` folder to illustrate:

- 3D-only interaction.
- 2D+3D linked interaction.
- Synchronized feedback behavior.

## Contact

For questions regarding the repository or manuscript, please contact:

**Cong Liu**  
[congliu@gdut.edu.cn]

**Jianqing Mo**  
[momolon@gdut.edu.cn]
