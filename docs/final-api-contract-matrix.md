# Final API contract matrix

| Area | Authoritative backend contract | Result |
| --- | --- | --- |
| Pricing | `GET /api/billing/pricing` | Preserved |
| Checkout | `POST /api/projects/{projectId}/billing/checkout` | Preserved |
| Payment status/history | `GET /api/projects/{projectId}/billing/status` and `/history` | Preserved |
| PayHere callback | `POST /api/billing/payhere/notify` | Preserved with signature validation |
| Admin billing | MFA-protected `AdminBillingController` | Preserved |
| OpenAPI | `/swagger/v1/swagger.json` | 200 in API integration test |

The duplicate PR #29 endpoints with identical pricing, checkout, status, callback, and admin paths were removed. Frontend/browser compatibility remains externally blocked pending an isolated frontend environment.
