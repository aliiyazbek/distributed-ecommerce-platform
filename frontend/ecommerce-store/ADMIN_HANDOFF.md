# Admin frontend handoff

The three Admin pages are frontend-only. No API requests, authentication, database,
or persistent storage are implemented. Product submissions are not saved; order
status changes last while App remains mounted and reset on refresh.

## Integration points (endpoint names are illustrative)

| Operation | Existing integration point | Integration work |
| --- | --- | --- |
| Add product (`POST /products`) | `src/pages/admin/productForm.js`: `submitProduct(product)` | Replace the local stub with the agreed request. It currently returns `{ saved: false, product }`, a frontend result, not a proposed backend response. Adapt the real response before returning to the UI; only report success after confirmed creation. |
| Load orders (`GET /orders`) | `src/App.jsx`: `orders` state initialized from `mockOrders` | Load and normalize API data here (or a small service called here). Add loading, failure/retry and cancellation handling; keep loading/failure distinct from the existing empty state. Pass the resulting array to AllOrders. |
| Load details (`GET /orders/:id`) | `src/App.jsx`: lookup by `route.orderId` | Details currently come from the same complete array. Add a separate detail fetch if list data is incomplete/paginated. Do not display Not Found until loading completes and absence is confirmed. Key by the canonical order ID, not the display order number. |
| Update status (`PATCH /orders/:id/status`) | `src/services/orderService.js`: `updateOrderStatus(order, status)` | Replace the local immutable result with the agreed request. Return a normalized updated order; App replaces that order in shared state after success. Throw on failure so the existing error UI preserves the previous saved status. Replace the session-only success copy in AdminOrderDetails after real persistence is connected. |

Do not assume these paths, methods, payloads, or response formats are confirmed.
The current view models describe UI requirements only.

## Current frontend conventions to confirm with Ali

- IDs: `order.id` is a string used in hash routes and lookups; `orderNumber` is
  display-only. `customerId` is a display string. Normalize API IDs to strings at
  the boundary. Do not send a display order number in place of the canonical ID.
- Items: `items[].id` is currently a mock row key, not a confirmed product ID.
  Agree on line-item ID versus product ID. Product creation currently has no ID.
- Statuses are case-sensitive: Pending, Shipped, Delivered, Cancelled. Agree on
  enum mapping and permitted transitions; the local demo allows any transition.
- Product input: trimmed name/description, positive numeric price, category label,
  non-negative safe-integer stock, and optional File/null image. Category values
  are local labels; agree on category IDs and the source of available categories.
- Uploads: PNG/JPEG/WebP, maximum 5 MiB locally. Confirm required/optional image,
  limits, upload mechanism and server-side validation. Use multipart only if the
  agreed endpoint requires it. Frontend MIME checks are not server validation.
- Money: UI currently uses USD and two decimals; details sum unit price times
  quantity, with cent rounding. Confirm currency, decimal/minor-unit format,
  rounding, discounts, tax and shipping. Real order totals should follow the
  backend's authoritative calculation, not assume this simple mock sum.
- `itemCount` currently means sum of quantities, not number of distinct lines.
- Dates are preformatted mock labels with no timezone. Agree on timestamps,
  timezone and locale before formatting live data.
- Agree on field-validation errors, request failures, pagination and concurrency
  behavior. No auth transport or credentials contract is assumed here.

## Audit fixes

- Rejected/corrupt images now expose the existing Remove image control, allowing
  the optional image/error to be cleared without selecting another file.
- Image errors have an alert role for assistive technology.
- The All Orders hash with a trailing slash resolves to All Orders.
- No CSS, navbar styling, dimensions, colors or demo helpers were changed.

## Verification and limits

Run from the project directory:

```sh
npm.cmd run lint
npm.cmd run build
node --test tests/admin-audit.test.mjs
```

`npm.cmd` avoids the PowerShell script-execution restriction on this machine.
Use ordinary `npm` in shells where it is available.

The audit tests cover validation, upload selection/removal/error recovery, demo
helpers, submission success/failure/busy guards, all six orders, totals, routes,
empty/not-found states and all four saved statuses. They use real React server
rendering and extracted handlers with controlled state/DOM mocks. They do not
replace browser interaction tests: canvas painting, image decoding, object-URL
effect cleanup, native file dialogs, CSS layout, keyboard focus, history events
and browser console output still need a browser pass.

No browser automation tool was available during this audit. Responsive CSS was
reviewed (shared breakpoints and horizontal table scrolling), not visually
verified. Some small secondary/inactive text uses pale Figma colors; contrast
needs accessibility review. Colors were preserved as requested. Inter is declared
but not bundled, so Arial is used when Inter is unavailable.

Fetch loading/error states do not exist because data is synchronous and local.
AllOrders has an empty state; details has Not Found; both mutation forms have
busy/success/error handling. Product drafts reset when navigating away. Unknown
non-order hashes currently fall back to Add Product. Products/Sign out remain
disabled by scope. Unused starter assets/App.css are left untouched.

## Handoff checklist

- [ ] Confirm endpoints, IDs, statuses, categories, uploads, dates and money rules.
- [ ] Replace mocks/stubs with adapters to the agreed API contracts.
- [ ] Add list/detail fetching states and correct server-confirmed success copy.
- [ ] Verify server validation errors, failures, stale updates and refresh behavior.
- [ ] Run lint, build and audit tests after integration.
- [ ] Test uploads, status Save/list synchronization, back/forward navigation,
  mobile widths, keyboard/screen-reader behavior and console output in a browser.
