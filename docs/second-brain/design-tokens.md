# Design tokens

The candidate Blueprint carries project-defined design tokens. Actual authoring-app branding and exact token names or values require their own design decision.

| Token group | Intended data | Verification |
| --- | --- | --- |
| Color | Semantic surface, foreground, primary, border, error and disabled colors | Check supported contrasts and state cues |
| Typography | Font choices and roles, sizes, weights and line heights | Verify scale, wrap and clipping for representative copy |
| Spacing and layout | Space scale, layout widths, alignment and responsive behavior | Verify changes across selected preview profiles |
| Shape and depth | Supported radius, border and elevation values | Ensure editor preview agrees with generated output |
| Motion | Supported duration, easing and transition presets | Honor reduced motion and surface unsupported transitions |

Persist approved token choices or draft edits with the relevant design snapshot. Resolve shared tokens consistently in the renderer and editor. Report broken references and avoid silent visual substitution. Coffee Shop's dark theme is an example acceptance target. It's independent of the REQVIORA authoring theme.

Source: BRD §§4, 8, 10 and Appendix A. Token grouping is a handoff proposal.
