# Accessibility requirements

Candidate coverage applies to authoring, review, preview and export navigation. Confirm standard/conformance level, browsers, devices and supported combinations in OD-07.

| Area | Behavior and verification |
| --- | --- |
| Keyboard | Complete the main workflow without a pointer. Provide equivalent graph/drag controls through lists, route menus or keyboard actions. |
| Focus | Keep logical ordering and visible focus. Move focus appropriately on validation and dialogs, and return it to the triggering control on close. |
| Semantics | Name buttons, fields and editor controls. Associate field errors with inputs and announce job/validation status changes. |
| Visual design | Check text/interactive contrast, text scaling and responsive reflow. Communicate blockers, suggestion status and errors using text or semantics alongside color. |
| Motion | Respect reduced-motion preference and keep all essential state changes readable without animation. |
| Input | Provide a text/manual path alongside voice. Explain permission failure and allow transcript correction. |
| Preview | Keep target profile and simulation status accessible. Test mock flows separately from the authoring viewport. |

Collect automated checks and manual keyboard/screen-reader results. Cover long Tamil and English content, narrow layouts, empty/error states and unavailable microphone permissions.

Source: BRD §§8.4, 10 and FR-045.
