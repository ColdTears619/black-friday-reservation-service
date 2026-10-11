# Black Friday Reservation Service

A .NET 8 service for limited-time sales across multiple stores (tenants). Customers can hold a product for a short time, then confirm the reservation. SQL Server transactions protect the sale quantity when many requests arrive together. A separate worker releases expired holds.

This is a working reservation flow. Payment, order processing, retry and dead-letter handling, caching, and Docker Compose are still future work. Confirmation currently saves a pending processing job; no worker consumes that job yet.

Start with the [documentation index](doc/README.md). It covers architecture, project behavior, project structure, trade-offs, and ideas for improvement, in that order.

- [Setup, configuration, and seed data](doc/setup-and-seed.md)
- [Swagger checks, automated tests, and the capacity demo](doc/testing.md)

The API runs at `http://localhost:5080` in development. Swagger is available at `http://localhost:5080/swagger`.
