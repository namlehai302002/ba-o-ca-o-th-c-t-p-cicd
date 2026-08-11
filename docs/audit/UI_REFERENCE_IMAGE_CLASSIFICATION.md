# UI Reference Image Classification

## Source And Scope

The project owner supplied screenshots through the review conversation. They are reference evidence, not repository assets and not snapshot baselines. Their visible desktop topbar, fixed left sidebar or collapsed icon rail, wide content area and 1920-class captures identify them as desktop states. Narrow crops show only a selected portion of the same desktop shell and do not establish a mobile viewport.

## Classification

| Reference group | Classified state | Regression use | Explicit limitation |
|---|---|---|---|
| Full pages with topbar and expanded 279 px sidebar | Desktop expanded shell | Sidebar active group, page header, filters, cards, tables and dashboard composition | Does not prove tablet/mobile behavior. |
| Narrow crop showing only aligned menu icons | Desktop collapsed rail crop | Icon vertical alignment, target size and active state | Crop width is not viewport width. |
| Floating menu beside the icon rail | Desktop collapsed flyout | Anchor position, max-height, internal scroll, Escape and collision behavior | Not a mobile drawer. |
| Warehouse overview/report screenshots | Desktop management/report layout | KPI hierarchy, table/panel alignment and navigation state | Mobile/tablet must use separate emulated viewport evidence. |
| Voucher/QC/RF screenshots | Desktop workflow/error state | Operator wording, loading recovery, modal/toast placement and item identity | Device behavior requires no-device simulator or physical-device evidence. |

## Evidence Link

- Desktop rail/flyout and route regressions: `tests/visual/wms-visual-regression.spec.ts`.
- Independent mobile/tablet coverage: `tests/visual/wms-mobile-deep.spec.ts`.
- Matrix: `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv`.
- Executable report counts: `artifacts/ui-cross-device/PLAYWRIGHT_REPORT_STATS_20260713.txt`.

No finding is classified as mobile solely because a desktop reference was cropped narrowly.
