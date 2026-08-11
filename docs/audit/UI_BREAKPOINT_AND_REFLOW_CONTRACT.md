# UI Breakpoint And Reflow Contract

## Supported Matrix To Verify

| Class | Viewports |
|---|---|
| Desktop/laptop | 1280×720, 1366×768, 1440×900, 1536×864, 1920×1080 |
| Large desktop | 2560×1440 when environment supports it |
| Tablet | 768×1024, 820×1180, 1024×768, 1180×820 |
| Mobile | 320×568, 360×800, 375×812, 390×844, 412×915, 430×932 |
| Zoom | 100%, 110%, 125%, 150%, 200%; 320 CSS px/400% accessibility reflow |
| Browser | Chromium; Edge enterprise target; Firefox/WebKit only when declared supported |

## Shell Contract

- Desktop above 1024 px: expanded sidebar or 76 px collapsed rail; main-content offset must match.
- Collapsed desktop groups open a collision-aware flyout with internal scrolling and reachable first/last item.
- At or below 1024 px: one off-canvas drawer, backdrop and body-scroll lock; no simultaneous desktop rail/flyout interaction.
- Topbar, toast, validation and focus target must not cover each other.
- No body-level horizontal overflow; wide tables may scroll only inside an explicit data container.

## Component Contract

- Cards reflow by available width and never use fixed height that clips Vietnamese text.
- Primary actions wrap/stack or move into an accessible overflow menu; they do not disappear.
- Tables preserve record identity and required business columns through contained scroll/detail disclosure.
- Forms retain labels, validation focus, submit recovery and virtual-keyboard access.
- Modal/drawer/popover/tooltip/toast remain inside the viewport with focus management and Escape recovery.
- Touch controls meet at least WCAG 2.2 target-size rules; primary warehouse controls target 44×44 CSS px.

## Evidence Rules

- User-provided images are desktop references only.
- Every route/role/state/viewport row records functional, console/network, screenshot/diff and manual-review status.
- No screenshot baseline update is allowed while functional assertions, unexpected errors or manual review are incomplete.
- A desktop pass never implies tablet or mobile pass.

## Verified Build 2026-07-13

- Warehouse overview targeted reflow passed at 1440x900, 1366x768, 768x1024 and 390x844.
- Authenticated desktop Chromium passed 68 tests with one intentional mobile-only skip.
- Mobile-deep Chromium passed 424 tests across 360x740, 390x844, 430x932 and 768x1024.
- Public authentication passed 12 tests across desktop, laptop, tablet and mobile.
- No-device scanner/camera/print simulation passed 10 tests at 1440x900 and 390x844.
- Seven isolated operational roles passed navigation visibility and direct-route authorization checks at the default 1280x720 viewport.
- Firefox, WebKit, physical-device validation, screen-reader validation, 200% text zoom and full manual screenshot sign-off are not proven by these runs and remain outside any UI-complete claim.
