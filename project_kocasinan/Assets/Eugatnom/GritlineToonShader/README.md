# Gritline

Thank you for using **Gritline Toon Shader**, a stylized toon shader designed to give your models a bold, graphic novel-inspired look. This guide covers its features, customization options, and how to integrate it into your Unity project.

---

## FEATURES
---

**Graphic-Inspired Lighting**  
Achieve a stylized, gritty toon aesthetic with sharp contrast between light and shadow, ideal for cel-shaded characters or environments.

**Stylized Edge Highlighting**  
Optional edge detection to add outlines and exaggerate geometry in a comic book style.

**Back-Face Culling Outline Method**  
Gritline uses back-face culling with inverted normals to render outlines. This approach creates a scaled-up version of the model’s back faces in a solid color to form a clear silhouette around the object.  
To use this effect, the outline must be applied as a **second material** on the same Mesh Renderer, placed **below** the main material in the material list.  
Advantages: It's performance-friendly, Shader Graph compatible, and produces clean, geometry-accurate outlines without relying on post-processing or UVs.  
Limitations: It depends on model geometry, requires rendering the mesh twice, only outlines the outer silhouette (not internal edges), and can break if normals are incorrect.

**Shadow Bands and Tone Control**  
Fine-tune lighting steps and banding to mimic 2D shading styles with multiple cutoff levels and smooth blending.

**Surface Texture Overlay**  
Apply an optional grunge or halftone texture to the surface for extra visual grit, without altering the underlying model.

**Full Customization**  
Easily adjust color bands, outline thickness, ramp textures, and texture overlays from the Inspector.

**Built in Shader Graph**  
100% created in Unity Shader Graph for full transparency and easy modification.

---

## TECHNICAL DETAILS
---

- Performance: Optimized for both mobile and desktop platforms.  
- Rendering Pipelines: Designed for Unity’s Universal Render Pipeline (URP).  
- Customization: All parameters exposed in the Unity Inspector for quick iteration.

---

## GETTING STARTED
---

1. Import the Gritline shader package into your Unity project.  
2. Create a new material and assign the Gritline shader.  
3. Apply the material to any mesh or model.  
4. Use the Inspector panel to adjust colors, outlines, and overlay textures.  
    * Outlines must be applied as a **second material** on the same Mesh Renderer, placed **below** the main material in the material list.  
5. Press play to preview the toon-shaded result.

---

## SUPPORT
---

If you have any questions, need help, or want to report a bug, please reach out through the contact info provided in the asset store listing.

Thank you for choosing **Gritline Toon Shader**! We hope it adds just the right edge to your project.
