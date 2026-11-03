---
title: uLayout adapter
sidebar_position: 3
---

# uLayout adapter

`Foundation.UI.ULayout` is an optional adapter for [uLayout](https://github.com/pokeblokdude/uLayout)
(`com.pokeblokdude.ulayout`) 1.7.1 or newer. It compiles only when that package is installed and adds no dependency to the
core UI assembly. It provides an **aspect ratio fitter** for uLayout.

## Hierarchy

```text
Container (uLayout Layout)
  Slot (uLayout LayoutItem + ULayoutAspectRatioFitter)
    Content (RectTransform, Image, view, or a nested Layout)
```

Configure the **Slot** with the usual uLayout sizing, flex weights and margins. To fill the allocated space, select Grow on
both axes. Assign the direct child **Content** to the fitter. *Aspect Ratio* is width / height; *Alignment* is normalized from
bottom-left (0, 0) to top-right (1, 1), default center. The fitter contains the content within the slot without cropping.

In `ContainContent` mode:

- uLayout exclusively owns the slot, and the fitter exclusively owns the content's anchors, position and dimensions,
  preserving its pivot.
- Do not add Unity's `AspectRatioFitter` or `ContentSizeFitter` to Content.
- If Content has a uLayout `LayoutItem` or `Layout`, use Fixed sizing on both axes so it does not resize itself.
- Do not put a `Layout` on the Slot: it would also arrange Content.
- Content should have unit scale and no local rotation for rectangular containment.
- The slot retains its allocated bounds, including letterbox space, when the parent positions siblings or calculates
  `FitContent`. This separation avoids a feedback loop between aspect fitting and Grow or wrapping.

The fitter works in Edit Mode and Play Mode, responds to layout completion, container resizing, runtime ratio changes and
reparenting, and avoids repeated transform writes once the result is stable. A plain `RectTransform` parent also works. It does
not infer the aspect ratio from a sprite or texture.

## Height from width (an adaptive square)

Select **Fit Mode = Height From Width**, then press **Configure Slot: Grow Width / Fixed Height** in the inspector. Set Aspect
Ratio to **1** for a square. uLayout assigns the slot width after container padding and item margins, and the fitter sets the
slot height to width / ratio. The vertical layout measures this height before arranging following items, including when the
parent uses `FitContent` for its height.

This mode also works without a Content child (for example an `Image` directly on the Slot). Grow Y must be off, because it gives
height control to the parent rather than to the aspect ratio. Existing components keep `ContainContent` mode.
