# design/KIT.md template

Copy this into `design/KIT.md` and fill every section. Numbers come from
the reference (`design-from-app`) or from the user's choices, in epx.
Write the reason next to any value that looks odd, so nobody "fixes" it
later.

```markdown
# <App> design kit

## Feel
Three words: <calm, precise, warm>. Like <reference app, if any>.

## Tokens

### Palette
| Name | Value |
| --- | --- |
| PaletteGray50 | #F9FBFC |
| PaletteNight | #1E1F22 |
| PaletteBlue600 | #287ABC |
| PaletteBlue400 | #4C95D3 |

### Meaning
| Token | Light | Dark | High contrast | Used for |
| --- | --- | --- | --- | --- |
| InkTextBrush | Gray900 | Snow | WindowText | body text |
| InkAccentBrush | Blue600 | Blue400 | Highlight | primary buttons, links, checks |
| InkSelectionBrush | Blue100 | Blue900 | Highlight | selected row fill |
| InkSelectedTextBrush | Blue600 | Blue400 | HighlightText | selected row text |

### Scales
| Token | Value | Why |
| --- | --- | --- |
| KitSidebarWidth | 240 | measured |
| KitRowHeight | 36 | measured; Windows lists use 40, the reference is denser |
| KitRadius | 6 | measured |

### Motion
| Token | Duration | Used for |
| --- | --- | --- |
| KitMotionHover | 0.083 s | hover and press fills |

## Type
| Role | Font | Size | Weight | Used for |
| --- | --- | --- | --- | --- |
| KitTitleText | Segoe UI Variable | 24 | semibold | page titles |
| KitBodyText | Segoe UI Variable | 14 | regular | task titles |

## Icons
Source: <Segoe Fluent Icons / drawn PathIcons / open set and its license>.
Size <16>, stroke <1>, filled when selected: <yes/no>.

## Window
Title bar: <TitleBar control, kit background, search in Content>.
Caption buttons: <system, kit colors>. Backdrop: <Mica / solid InkSurfaceBrush>.
Default size <1100 x 720>, visible <1087 x 714> at 125%.
Sidebar: <kit ListView rows / NavigationView restyled>.

## Parts

### Sidebar row
Anatomy: icon 16, gap 12, label (KitLabelText), optional count.
Size: height 36, margin 4 x 1, padding 12, radius 6.
| State | Fill | Text | Icon |
| --- | --- | --- | --- |
| rest | none | InkText | InkTextSecondary |
| hover | InkHover | InkText | InkTextSecondary |
| pressed | InkHover | InkText | InkTextSecondary |
| selected | InkSelection | InkSelectedText | InkSelectedText, filled |
| focused | system focus rectangle, InkFocus | InkText | InkTextSecondary |
| disabled | none, 40% | InkText | InkTextSecondary |

### <next part>
...
```
