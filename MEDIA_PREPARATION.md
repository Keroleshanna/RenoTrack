# MEDIA_PREPARATION.md — Preparing Approved Photos for the Website

**Status:** Living document, introduced in Phase 13 Slice 5a (**D106**).

**Audience:** the operator who publishes a company's photos, working in that company's **private content repository**. This file describes the product's process and rules only: no real photo, file name, approval record or provenance detail belongs in this repository (D100).

---

## 1. The boundary

```
private source photos ──► approval register ──► crop spec ──► RenoTrack.MediaPrep ──► pack media/ ──► startup verification ──► /medien/
 (outside every repo)     (private repo)        (private repo)   (this repo's tool)     (derivatives only)   (the Website)            (allowlist)
```

| Never in any repository | In the private content repository | In this repository |
|---|---|---|
| Source photos (they may carry GPS coordinates) | The approval register, the crop specification, the prepared derivatives in the pack's `media/`, the pack's `site.json` | The tool, the rules, synthetic test graphics |

- **The Website never reads a source photo and never processes an image at run time.**
- **It serves only files the pack's `Site:Media` list names, after verifying every one at startup.**
- **A file placed in `media/` without being listed is never served.** Do not rely on this: keep `media/` to derivatives only.

## 2. Before a photo may be prepared: approval and privacy

Record each photo in the **private approval register** before it gets an id. Suggested fields:

| Field | Purpose |
|---|---|
| Register reference | The opaque value used as `SourceRef` in `site.json` (lowercase ASCII, digits, hyphens, ≤ 40). |
| Source file name | Where the original lives, outside every repository. |
| Approved by, and when | The owner's approval of this photo for publication. |
| Approved uses | Hero, which service, or later projects. |
| People | "None identifiable", or a reference to written consent for each identifiable person. |
| Privacy checklist | Completed, see below, with the date. |

**Privacy checklist** — every item checked on the source and again on the crop:
- [ ] No identifiable person without recorded consent: faces, reflections in mirrors, glass and tiles.
- [ ] No house number, street sign, number plate, company sign of a customer or neighbour.
- [ ] No documents, screens, calendars, post, names or labels readable at full size.
- [ ] No customer's personal belongings that identify them: photos on walls, certificates, children's names.
- [ ] Nothing that locates the property: distinctive views out of windows, landmarks.
- [ ] The photo shows the company's own work.
- [ ] It is a photograph of that work: not stock, not AI-generated, not a rendering.
- [ ] Any caption states no place, date or customer detail unless the owner has confirmed it and it is safe to publish.

A photo that fails any item is not prepared. Crop the problem out only if the result still shows the work honestly.

## 3. The crop specification

A JSON file in the private repository. Coordinates are pixels of the photo **as it is displayed**, after its EXIF orientation is applied.

```json
{
  "Items": [
    {
      "Id": "badezimmer-wandfliesen-01",
      "Source": "IMG_1234.jpg",
      "Crop": { "X": 0, "Y": 210, "Width": 4032, "Height": 2688 },
      "SocialCrop": { "X": 0, "Y": 540, "Width": 4032, "Height": 2117 }
    }
  ]
}
```

- **`Id`:**
  - lowercase ASCII letters and digits with single hyphens, ≤ 60 characters;
  - describes what the photo shows;
  - no customer name, place, street, date or keyword chain.
- **`Source`:** a file name inside the source directory, never a path.
- **`Crop`:** exactly **3:2**, to within one pixel of rounding, and at least 1600 pixels wide. Photos are never enlarged.
- **`SocialCrop`:** optional, exactly **1200:630**, at least 1200 pixels wide. Supply one only for a photo that will be a page's `og:image` (the homepage hero, a service's photo).
- There is no automatic cropping. The operator chooses the crop, and the crop is what the owner approves.

## 4. Running the tool

```bash
dotnet run --project tools/RenoTrack.MediaPrep -c Release -- <crop-spec.json> <source-directory> <pack>/media
```

- **Output per item:** `{Id}-480.webp`, `{Id}-960.webp`, `{Id}-1600.webp`, `{Id}-480.jpg`, `{Id}-960.jpg`, `{Id}-1600.jpg`, plus `{Id}-og.jpg` when a social crop is given.
- **It never overwrites a file.** A replacement photo is a **new id**; the old id's files and list entry are removed.
- **It prints a size report** with every file, its bytes, its budget and `ok` or `OVER BUDGET`.
- **Exit codes:**
  - `0` — everything written and within budget.
  - `1` — refused: invalid specification, missing source, crop out of bounds, or file already present. Nothing further was written.
  - `3` — files written, but at least one is over its budget. Follow §6. **Never re-encode one photo at lower quality.**

## 5. Publishing in the pack

Add the item to `Site:Media` in the pack's `site.json` and reference it (see `CONTENT_PACK.md` §3.3):

```json
"Media": [
  {
    "Id": "badezimmer-wandfliesen-01",
    "Alt": "Bad mit großformatigen hellgrauen Wandfliesen und bodengleicher Dusche",
    "SourceRef": "reg-2026-014",
    "HasSocialImage": true
  }
],
"Home": { "HeroImage": "badezimmer-wandfliesen-01" }
```

**Alt text describes the actual image for accessibility, and also gives search engines and other machine consumers useful context. It is not a keyword field.** Write what a person would need to know if they could not see the photo, in one plain sentence, without "Bild von" or "Foto von".

Then start the site. **Startup refuses to run** if any listed derivative is:
- missing;
- over its byte budget;
- not the format its extension says;
- not exactly its specified size;
- carrying any metadata: EXIF (including GPS), XMP, IPTC, comments, or anything appended after the image.

The message names the configuration key and the file. An unreferenced list entry also fails startup, because every listed photo is published.

## 6. Encoding settings and byte budgets

### 6.1 Settings (pinned, D106)

| Setting | Value |
|---|---|
| Library | SkiaSharp **4.152.0** exactly (and its Linux native assets) |
| Colour | decoded to sRGB; output carries **no** colour profile |
| Orientation | EXIF orientation applied before anything else |
| Resampling | downscale only: 2× box halvings (bilinear at exactly one half) while at least twice the target, then one Mitchell cubic resample to the exact size |
| WebP | lossy, quality **80**, no alpha |
| JPEG | quality **82**, 4:2:0 chroma subsampling, baseline |
| Social image | JPEG with the same settings, 1200 × 630 |
| Sizes | 480 × 320, 960 × 640, 1600 × 1067 (3:2); 1200 × 630 |

The same source, crop specification and tool version produce byte-identical files on the same platform. Changing any setting or the SkiaSharp version is an ADR change, and repeats §6.3.

### 6.2 Budgets — **provisional**

| Derivative | Budget (1 KB = 1,024 bytes) |
|---|---|
| 1600 wide, each format | 350 KB |
| 960 wide, each format | 160 KB |
| 480 wide, each format | 60 KB |
| Social image | 250 KB |

These values are **not yet validated against real media**. They are frozen only by §6.3, and this table then records the final values with their evidence.

### 6.3 Validating and freezing the budgets

These are **evidence-based performance budgets, validated against the real owner-approved media set**. Run this on the operator's machine, as part of the private QA before Slice 5a closes, and again whenever the settings change:

1. **Generate** every owner-approved derivative with the pinned tool and settings.
2. **Measure** from the tool's report: for each derivative width and format, record the maximum, 95th percentile and median bytes. **Only these aggregates** go into `PHASE13_PROGRESS.md`; no photo, file name or register detail does.
3. **Inspect visually** every real photograph at 100% and at its displayed size, at 1× and 2× pixel density. Look for banding, blocking, smeared grout lines and tile texture, colour shift and halos. Inspect tile work first: its fine repeating edges are the hardest case.
4. **Resolve globally, never per photo:**
   - **Visible compression artefacts:** raise the global quality setting and repeat from step 1 for the whole set.
   - **A file over budget that looks right:** raise that derivative's budget to cover the real set with modest headroom, and record why.
   - **Never** lower one photo's quality, reduce its dimensions or change its format to fit a limit.
5. **Freeze** the final settings and budgets in D106 and in §6.1/§6.2, with the measured figures. Update the constants in `MediaDerivatives.cs` / `MediaPipeline.cs` and their tests in the same change.

## 7. What the application guarantees, and what it cannot

| Guaranteed by code | Only the approval process can guarantee |
|---|---|
| Only listed, verified derivatives are served | The photo is the company's own real work |
| No EXIF/GPS/XMP/IPTC/comments in any served file | No identifiable person without consent |
| Exact sizes, formats and byte budgets | No house number, plate or other identifying detail |
| Every photo has alternative text and a source reference | The alternative text and caption are true |
| Photos are never cropped by CSS; the approved crop is what is shown | The owner approved it |
