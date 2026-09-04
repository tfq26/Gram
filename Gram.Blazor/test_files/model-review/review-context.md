# Code Review Context

## File under review

TenantUsageLedger.cs is part of Gram's quota and billing subsystem. It records model usage for a multi-tenant AI application.

## Project

Gram is an AI assistant application with web, desktop, and mobile clients. All clients use the same backend services. The backend may run on multiple processes against shared PostgreSQL storage, while local development commonly uses SQLite.

## System

The ledger sits between the model gateway and the persistence layer:

1. A request is authenticated and assigned a tenant and user.
2. The model gateway estimates input/output token usage.
3. The ledger reserves quota before provider execution.
4. The provider executes.
5. Successful usage is recorded and billed.
6. Failed requests release their reservation.
7. Usage is read by dashboards and quota enforcement.

## Required invariants

- Tenant isolation is mandatory. Data, cache entries, quota windows, and billing must never cross tenant boundaries.
- A request may be retried with the same RequestId. Retries must not double-charge or double-count usage.
- Reservation and release operations can occur concurrently across backend processes.
- Quota windows are defined by the tenant's configured business timezone, not the server timezone.
- Token and charge calculations must use the provider's actual rates and preserve enough precision for billing.
- A successful record must survive process restarts through the repository layer.
- In-memory state may be used as a short-lived optimization, but it is not authoritative.
- The code should be safe when model calls fail after reservation and when two requests race for the remaining quota.

## Review task

Identify exactly four defects:

- two obvious defects visible from the code alone;
- one subtle defect requiring careful code reasoning;
- one defect that is only apparent when applying the project/system context above.

For each defect, provide the method/line area, impact, and a minimal fix. Do not rewrite the file and do not invent requirements outside this context.

