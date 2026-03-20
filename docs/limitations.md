# Limitations of This Research Release

## Scope statement

This repository is a **paper-oriented research reproduction release** prepared to support inspection, evaluation, and partial reproduction of the methods reported in the manuscript.

It is **not** a product-grade medical software package and should not be interpreted as a fully automated clinical workflow.

---

## 1. No redistribution of raw hospital imaging data

The original medical imaging datasets used in the study are not redistributed in this repository.

Reasons include:

- hospital data protection policies
- retrospective clinical data handling constraints
- ethical and institutional restrictions on public redistribution

Accordingly, this release uses **derived demo assets** rather than source clinical imaging volumes.

---

## 2. The public release does not provide a full end-to-end preprocessing pipeline

The manuscript describes a broader workflow that begins with medical image segmentation, manual refinement, centerline tracing, abstraction, layout generation, and MR deployment.

However, the public repository does **not** package all of these steps into a single automated pipeline from raw medical images to final interactive Unity deployment.

Instead, the public release starts from:

- a derived demo SWC file
- a derived 2D layout JSON
- the Unity scripts implementing the linked 2D–3D interaction framework

This boundary is intentional and reflects the methodological focus of the paper.

---

## 3. Limited portability of the original research prototype

The original system was developed in a Unity + MRTK2 + HoloLens 2 research environment.

As a result:

- scene setup may require manual assignment of prefabs, materials, or references
- project-specific Unity configuration may not be fully encapsulated in scripts alone
- behavior may differ depending on Unity, MRTK, or hardware versions

The public release therefore prioritizes methodological clarity over turnkey deployment.

---

## 4. Demo assets are derived research materials

The provided SWC and layout JSON files are **derived demo assets** prepared for research reproduction purposes.

They are intended to:

- illustrate the data structures expected by the released scripts
- demonstrate the interaction logic described in the paper
- provide a stable demo case for inspection and testing

They should not be interpreted as full clinical datasets.

---

## 6. Experimental reproduction is partial

The repository includes study-oriented logic such as:

- task control
- endpoint markers
- UI panel support
- head-movement logging

However, exact reproduction of the original study may still depend on:

- the original HoloLens setup
- experimental room conditions
- participant-facing instructions
- non-code scene configuration details

The release should therefore be understood as enabling **methodological and interface reproduction**, not guaranteed byte-for-byte reconstruction of the entire study environment.

---

## 7. Intended use

This repository is intended for:

- academic inspection
- research comparison
- interaction design reference
- method-oriented reproduction

It is **not** intended for:

- diagnosis
- treatment planning in real clinical practice
- regulatory or commercial deployment
- direct clinical decision-making
