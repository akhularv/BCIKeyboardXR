# visionOS Aesthetic Research for BCIKeyboardXR

Date: 2026-05-13  
Scope: Research-only pass for applying an Apple Vision Pro / visionOS-inspired visual system to the Unity UGUI prototype.

## Executive Direction

BCIKeyboardXR should move away from the current pastel-glass look toward a quieter spatial-computing surface language:

- translucent, low-opacity glass surfaces instead of colored fills
- layered highlights and shadows instead of thick borders
- soft halo focus feedback instead of saturated rim glows
- SF Pro-like typography, medium weights, and generous hit targets
- spring-like hover/selection motion with short, damped responses

For this project, the safest implementation path is "fake glass" with layered UGUI `Image` objects, not true backdrop blur. True blur is possible in Unity through render features or third-party packages, but it adds dependency, pipeline, and performance risk for a take-home prototype.

## Apple HIG Findings

### Materials and Glass

Apple describes a material as a visual effect that creates depth, layering, and hierarchy between foreground and background. In visionOS, windows use a system-defined **glass** material that lets light, environment, virtual content, and nearby surroundings show through while preserving foreground contrast.

Key HIG terms:

- **Glass**: adaptive material for visionOS windows; it brightens/darkens based on surroundings and does not map to a fixed flat color.
- **Translucency over opacity**: Apple explicitly recommends translucent windows in visionOS so people remain grounded in their environment.
- **Thin material**: recommended for interactive elements like buttons and selected items.
- **Regular material**: recommended to visually separate app sections, such as sidebars or grouped content.
- **Thick material**: used when a darker, more visually distinct element is needed.
- **Vibrancy**: foreground text, symbols, and fills receive adjusted contrast over material backgrounds. Apple lists `label`, `secondaryLabel`, and `tertiaryLabel` vibrancy levels.
- **Specular highlights**: not specified as numeric UI tokens in the public HIG, but the material guidance and visionOS demos show top-edge/high-angle light response as a core part of the glass read.

Sources:
- Apple HIG Materials: https://developer.apple.com/design/Human-Interface-Guidelines/materials
- Apple HIG Buttons, visionOS: https://developer.apple.com/design/human-interface-guidelines/buttons

## Numeric Visual Tokens to Use

Some exact component measurements, especially corner radii and shadow recipes, are easier to inspect in Apple’s official visionOS design resources than in prose HIG pages. Apple’s design resources page points to official visionOS Figma/Sketch kits. The values below combine the user-provided target tokens with Apple HIG direction and common visionOS kit conventions.

### Corner Radius

Recommended BCIKeyboardXR mapping:

| Element | Radius |
|---|---:|
| Phrase tiles / large cards | 22 px |
| Word tiles | 18 px |
| Keyboard keys | 14 px |
| Top-bar buttons | 16 px |
| Small indicators / progress details | 8 px |

Rationale: visionOS favors rounded rectangles and capsules for gaze stability. Apple notes that more rounded controls are easier to look at steadily, and recommends circular/capsule forms for many buttons.

### Borders

Recommended implementation:

- border weight: 0.5-1 px
- border color: `#FFFFFF`
- border alpha: 20-30%
- use border as a hairline edge, not a high-contrast outline

BCIKeyboardXR token:

```csharp
Color glassBorder = new Color(1f, 1f, 1f, 0.25f);
```

### Shadows

Apple does not publish a single public numeric shadow recipe for visionOS glass. The observed direction is layered, low-alpha, and atmospheric. Use two UGUI shadow layers:

| Layer | Color | Alpha | Blur Equivalent | Offset |
|---|---|---:|---:|---:|
| Ambient lift | `#7E90A8` or black-blue | 8% | 24 px | Y -8 px |
| Contact shadow | `#4A5870` or black-blue | 12% | 4 px | Y -1 px |

UGUI has no built-in blur radius for `Image`, so approximate blur with:

- scaled shadow images behind tiles
- soft rounded sprites with larger transparent padding
- lower alpha for the ambient layer
- optional second tighter image for contact

### Padding

Recommended tile padding:

- phrase tiles: 24 px horizontal, 16-20 px vertical
- word tiles: 18-20 px horizontal, 12-14 px vertical
- keyboard keys: centered text, 10-14 px effective inner padding
- composition bar: 30-36 px horizontal

Apple HIG button guidance also states visionOS hit regions should be at least **60 x 60 pt**, with centers at least **60 pt apart**, and 4 pt of padding around controls measuring 60 pt or larger to prevent hover overlap.

### Colors

Background:

| Token | Hex | Use |
|---|---|---|
| background edge | `#E4E8EE` | outer gradient |
| background base | `#EEF1F5` | dominant field |
| background center | `#F4F6FA` | radial center |
| top shade | `#DCE2EA` at 5% alpha | top vertical falloff |

Glass:

| Token | Hex / Alpha | Use |
|---|---|---|
| base glass | `#FFFFFF` at 40% | main tile surface |
| hover glass | `#FFFFFF` at 55% | hover brightness boost |
| warm action tint | `#FFFAEE` at 42-45% | Space/Back/Enter subtle variant |
| inner highlight | `#FFFFFF` 25% -> 0% | top gradient overlay |
| border | `#FFFFFF` at 25% | hairline edge |
| halo warm | `#FFF8E8` at 40% | hover/dwell focus |
| halo completion | `#B8D8FF` or `#A0D8FF` | dwell completion |
| primary text | `#1F2735` | dark blue-gray text |
| cursor | `#4070C0` | composition cursor |
| ghost text | `#1F2735` at 35% | ghost preview |

Avoid:

- saturated sandy red rim as the main focus state
- gold action-key fills
- opaque white slabs

## Typography Findings

Apple HIG says SF Pro is the system font for visionOS. It also notes visionOS uses bolder versions of Dynamic Type body/title styles and recommends 2D text for legibility.

Relevant public HIG values:

| visionOS Style | Default Weight | Point Size | Leading | Emphasized Weight |
|---|---|---:|---:|---|
| Body | Regular | 22 pt | 24.5 pt | Semibold |
| Caption 1 | Regular | 19 pt | 21.5 pt | Semibold |
| Caption 2 | Regular | 18 pt | 20.5 pt | Semibold |
| Footnote 1 | Regular | 17 pt | 19.5 pt | Semibold |
| Title 2, AX3 example | Regular | 36 pt | 38.5 pt | Semibold |

BCIKeyboardXR typography mapping:

| Element | Font | Size | Weight | Tracking |
|---|---|---:|---|---:|
| Phrase tiles | TMP default font | 28 pt | Medium/Bold styling | -0.5 |
| Word tiles | TMP default font | 24 pt | Regular styling | -0.3 |
| Keyboard keys | TMP default font | 28 pt | Medium/Bold styling | 0 |
| Composition committed | TMP default font | 36 pt | Regular styling | 0 |
| Composition ghost | same | 36 pt | Regular 400 | 0, 35% alpha |
| Action keys | same | 16 pt | Semibold/Bold styling | +1.0, all caps |
| Reset/Speak buttons | same | 14 pt | Semibold/Bold styling | +1.0, all caps |

Implementation note:

- The implementation uses `TMP_Settings.defaultFontAsset`, which resolves to LiberationSans SDF in a default TextMeshPro installation.

Sources:
- Apple HIG Typography: https://developer.apple.com/design/human-interface-guidelines/typography
- Apple Design Resources: https://developer.apple.com/design/resources/

## Interaction and Animation Findings

Apple’s public SwiftUI spring API defines:

```swift
.spring(response: 0.5, dampingFraction: 0.825, blendDuration: 0)
```

Definitions:

- `response`: approximate stiffness/duration in seconds.
- `dampingFraction`: drag as a fraction of critical damping.
- damping ratio 1.0 gives no oscillation; lower values add bounce.

Additional Apple spring references:

- `interactiveSpring(response: 0.15, dampingFraction: 0.86, blendDuration: 0.25)`
- UIKit spring damping ratio: values closer to 1.0 decelerate smoothly without oscillation.

BCIKeyboardXR spring-equivalent animation tokens:

| Interaction | Duration | Curve |
|---|---:|---|
| Hover scale up | 0.30 s | OutBack, overshoot ~0.5 or custom damped spring |
| Hover scale down | 0.25 s | InOutQuart |
| Halo fade in | 0.20 s | OutQuart |
| Selection punch | 0.35 s | OutBack, overshoot ~1.2, `1.0 -> 0.96 -> 1.0` |
| Commit fly label | 0.40 s | OutQuart |
| Reset fade down | 0.25-0.30 s | OutQuart |
| Cursor blink | 1 Hz | alpha fade over 100 ms, not hard toggle |

DOTween equivalents if DOTween setup is complete:

```csharp
rect.DOScale(1.03f, 0.30f).SetEase(Ease.OutBack, 0.5f);
halo.DOFade(0.40f, 0.20f).SetEase(Ease.OutQuart);
rect.DOScale(1f, 0.25f).SetEase(Ease.InOutQuart);
labelRect.DOMove(target, 0.40f).SetEase(Ease.OutQuart);
```

Coroutine fallback equivalents:

- `EaseOutQuart(t) = 1 - pow(1 - t, 4)`
- `EaseInOutQuart(t) = t < 0.5 ? 8t^4 : 1 - pow(-2t + 2, 4) / 2`
- lightweight OutBack approximation:

```csharp
static float EaseOutBack(float t, float overshoot = 1.70158f)
{
    float c1 = overshoot;
    float c3 = c1 + 1f;
    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
}
```

Sources:
- SwiftUI spring API: https://developer.apple.com/documentation/swiftui/animation/spring%28response%3Adampingfraction%3Ablendduration%3A%29
- SwiftUI Spring struct: https://developer.apple.com/documentation/SwiftUI/Spring
- UIKit spring animation: https://developer.apple.com/documentation/uikit/uiview/animate%28withduration%3Adelay%3Ausingspringwithdamping%3Ainitialspringvelocity%3Aoptions%3Aanimations%3Acompletion%3A%29

## Selection Ring / Halo Direction

Current BCIKeyboardXR dwell ring is too much like a hard progress meter. For a visionOS-style feel, use a soft focus halo:

- duplicate rounded tile shape behind/around the tile
- scale: 1.05x
- color at hover start: `#FFF8E8`, alpha 0 -> 0.40
- color at dwell completion: shift toward `#B8D8FF` or `#A0D8FF`
- progress reveal: keep radial fill semantics, but render the visible element as a diffuse halo, not a thin hard bar
- no saturated sandy red except possibly for debugging

UGUI implementation approach:

```text
TileRoot
├── ShadowAmbient
├── ShadowContact
├── HaloProgress (filled radial, soft sprite, scaled 1.05x, raycast off)
├── BackgroundGlass (white 40%)
├── InnerHighlight (top gradient)
├── BorderHairline (white 25%)
└── Label
```

If a true shape mask is too expensive, the existing radial `Image.Type.Filled` can drive alpha/fill on a rounded soft sprite. The result should read as a halo blooming around the tile rather than as a literal instrumentation ring.

## Unity Glass Implementation

### True Blur Options

True glass requires backdrop blur. In Unity UI this usually means:

- URP/HDRP custom render feature that copies the back buffer and blurs it.
- UI image material sampling a global blurred screen texture.
- third-party packages that add UI blur components.

References found:

- Unified Blur, open-source MIT, Unity 6+ render graph support: https://github.com/lukakldiashvili/Unified-Universal-Blur
- mob-sakai UIEffect, open-source MIT, broad UI effects including blur: https://github.com/mob-sakai/UIEffect
- zephyo UI Blur LWRP/URP 2020, open-source UI blur shader: https://github.com/zephyo/UI-Blur-LWRP-2020
- Unity Asset Store UI Blur, free but Asset Store EULA: https://assetstore.unity.com/packages/vfx/shaders/ui-blur-173331
- UGUI Canvas Blurred Background, paid Asset Store package: https://assetstore.unity.com/packages/2d/gui/ugui-canvas-blurred-background-fast-translucent-ui-blur-hdrp-urp-260862

### Recommended for This Prototype

Do not add a blur dependency for Iteration 4. Use fake glass:

1. **Base layer**: rounded white image, alpha 0.40.
2. **Inner highlight**: top gradient texture, white 0.25 at top to 0 by 40% height.
3. **Hairline border**: rounded edge image, white 0.25.
4. **Ambient shadow**: scaled rounded image, blue-black 0.08, offset down 8 px.
5. **Contact shadow**: scaled rounded image, blue-black 0.12, offset down 1 px.
6. **Hover halo**: scaled rounded/fill image, warm white/cyan, raycast off.

This keeps performance stable at 60 fps, avoids render-pipeline dependencies, and matches the visual direction well enough for a desktop AAC take-home.

## Open-Source / Reference Notes

Useful to study, not necessarily import:

- `Unified-Universal-Blur`: MIT, Unity 6+ blur render feature, good if the project later adopts URP pipeline blur.
- `mob-sakai/UIEffect`: MIT, mature Unity UI effect package; useful reference for how UI effects attach to UGUI components.
- `zephyo/UI-Blur-LWRP-2020`: older URP/LWRP blur shader approach, useful conceptually.
- Asset Store blur packages are under Asset Store EULA or paid licenses; do not copy code/assets without importing under the correct license.
- SwiftUI glass examples are useful visually, but direct code patterns do not translate to UGUI beyond material layering and spring timing.

## Specific Implementation Plan After Approval

1. Add new `UiTheme` tokens for visionOS colors, radii, text sizes, and alpha values.
2. Replace current gold action-key styling with warm-tinted glass.
3. Extend tile construction in `PhraseTile`, `WordTile`, and `KeyTile`:
   - ambient/contact shadows
   - base glass
   - top highlight
   - hairline border
   - soft halo progress
4. Update `PremiumTileAnimator`:
   - spring-like hover scale
   - halo fade instead of rim glow
   - background alpha boost 0.40 -> 0.55
   - selection punch 1.0 -> 0.96 -> 1.0
5. Update `DwellSelectable`:
   - treat progress image as halo progress, not hard ring
   - use warm white/cyan colors
6. Update background texture:
   - `#EEF1F5` base
   - `#F4F6FA` center
   - `#E4E8EE` edge
   - optional 1% procedural noise
7. Update composition bar:
   - glass layers and stronger highlight
   - soft-blue cursor `#4070C0`
   - smooth cursor alpha fade
8. Typography:
   - prefer SF Pro TMP asset if present
   - otherwise TMP fallback
   - set weights and tracking per table above
9. Preserve prediction, dwell, word/phrase commit, and layout proportions.

## Already Fixed During Research

The visible `h&amp;` bug came from HTML-style ampersand escaping inside the composition bar rich-text renderer. TMP rich text uses tags, but it does not need `&` encoded as `&amp;` for literal display in this context. The fix was to stop escaping `&` while continuing to neutralize `<` and `>` in user-entered text.

Verified with:

```bash
dotnet build Assembly-CSharp.csproj
```

Build passes.

## Implementation Notes

Implementation began after approval with three explicit modifications:

- Typography now targets TextMeshPro's default font asset while preserving the approved sizes, tracking, and regular/bold styling.
- Phrase and word sizes were updated to the approved override: phrase tiles 28 pt Medium, word tiles 24 pt Regular.
- Dwell progress is implemented as a filled radial halo using `Image.Type.Filled`, `FillMethod.Radial360`, clockwise from 12 o'clock. It uses a rounded soft tile shape scaled outside the tile, not a hard ring.
- The composition cursor now uses a sine-modulated alpha breath between 0.4 and 1.0 at 1.2 Hz.

Deviations and rationale:

- True backdrop blur was not added. The implementation uses layered UGUI fake glass to avoid new render-pipeline dependencies and preserve 60 fps with flicker animation.
- DOTween is declared as a package dependency, but DOTween setup is editor-only. The spring-like hover and selection motion currently uses coroutine fallback math (`OutBack`, `OutQuart`, `InOutQuart`) so the project continues to compile before DOTween’s Utility Panel setup is completed.
- The separate external font dependency was removed before submission cleanup to avoid manual font-generation requirements.
