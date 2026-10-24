# Branding

Product name: **Lumivane**. Lumi (light) and vane (a surface that turns with the wind): light that follows what is on screen.

## Name screening (24 October 2026)
Method: web search, GitHub and domain lookups, and the Justia US trademark search index. This is a collision scan, not a legal clearance. Registry status and class numbers were not available from the index. A search of the USPTO, EUIPO and DPMA registers in classes 9, 11 and 42 is still required before any commercial use.

| Name | US trademark index | Web, apps | Verdict |
|---|---|---|---|
| Lumora | 16 exact records, including mobile software and LED lighting | Several similar apps | Reject |
| Luxora | Exact records for lamps and lighting; the similar Luxor mark is a known lighting-control brand | Lighting products | Reject |
| Veyra | Exact records for lighting fixtures and electronic locks; variants for scheduling and AI software | | Reject |
| Spectra | Not screened further: a very common word in software and hardware | | Reject |
| Ambienta | One exact record (financial services); a lighting mark with a different structure | A mobile app of that name | Weak |
| Illumara | No exact record | An industrial LED lighting product line of that name | Reject |
| **Lumivane** | **No record** | **No web or app results; lumivane.com is a parked, for-sale listing** | **Selected** |

Residual risks: the GitHub account `lumivane` exists (no public repositories), so the repository lives under its owner's account; the `.com` domain is not owned by the project; similar-sounding marks in other registers were not searched.

## Separation from Philips Hue
No Philips, Hue, Signify or Ambilight term in the name, bundle identifier, icon or domain. "Philips Hue" appears only to state compatibility, with a non-affiliation notice in the README and in Settings. Internal module names `HueCore` and `HueKit` refer to the protocol.

## Bundle identifier
`com.lumivane.app`. It keys the Keychain item, privacy grants and saved settings, so it must not change after the first public release. Reverse-DNS naming expects a domain the publisher controls: buy lumivane.com, or set another identifier in `scripts/brand.env` before the first release.

## Visual identity
- Icon: an L built from a single light bar, warm to cool gradient, with a cool light dot; dark tile. Source `branding/icon/concepts.py` (concept C), master `AppIcon-1024.png`, built to `Resources/AppIcon.icns` by `make_icns.py`. Checked at 16 to 256 px on dark and light backgrounds.
- The same mark is used in the sidebar, onboarding and as the menu bar template (a dot appears while syncing).
- Accent gradient `Brand.warm` to `Brand.blue`; rounded system type for headings.
- Name, identifier and version are set in `scripts/brand.env` and `Sources/Lumivane/Design.swift` (`Brand`).
