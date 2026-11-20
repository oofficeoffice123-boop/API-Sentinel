# API endpoint guide

The API uses JSON DTOs and JWT bearer authentication except for health and auth routes. Full OpenAPI documentation is available at `/swagger` while the application is running.

| Route | Methods | Purpose |
|---|---|---|
| `/api/health` | GET | Liveness check |
| `/api/auth/register`, `/api/auth/login` | POST | Register/sign in, issue a 15-minute access JWT, and set an HttpOnly refresh cookie |
| `/api/auth/refresh` | POST | Rotate the refresh cookie and issue a new access JWT |
| `/api/account/logout` | POST | Revoke all active tokens and clear the refresh cookie |
| `/api/admin/bootstrap` | POST | Assign the first Admin role using the configured bootstrap key; authenticated |
| `/api/admin/users` | GET | Search and page user accounts; Admin role required |
| `/api/admin/users/{id}/role` | PUT | Assign User or Admin; Admin role required |
| `/api/apis` | GET, POST | Search/page saved APIs and create an API |
| `/api/apis/{id}` | PUT, DELETE | Update or delete an owned API |
| `/api/apis/{id}` | GET | Read an owned API configuration, hiding secret header values |
| `/api/apis/{id}/extraction` | PATCH | Configure or clear collection response extraction |
| `/api/apis/import-openapi` | POST | Import supported operations from OpenAPI JSON or YAML |
| `/api/ai/summary` | POST | Summarize up to 100 recent execution metadata records with Groq |
| `/api/apis/{apiId}/tests` | GET | List tests for an owned API |
| `/api/collections` | GET, POST, DELETE by ID | Manage user collections |
| `/api/collections/{id}/run` | POST | Execute saved requests in order |
| `/api/environments` | GET, POST, PUT, DELETE by ID | Manage user environments and active environment |
| `/api/environments/{id}/variables` | PUT; variable DELETE by ID | Set environment values and encrypted secrets |
| `/api/test-cases` | GET, POST, DELETE by ID | Manage test definitions |
| `/api/execute` | POST | Execute a request or saved test and persist a run |
| `/api/runs` | GET | Page execution history |
| `/api/monitors` | GET, POST, PUT, DELETE by ID | Manage recurring test schedules |
| `/api/monitors/{id}/run-now` | POST | Queue an immediate monitor run |
| `/api/monitors/{id}/history` | GET | Page monitor history |
| `/api/alerts` | GET; `/api/alerts/{id}/read` PUT | Read and update owned alerts |
| `/api/reports/dashboard` | GET | User-scoped real-data dashboard metrics |
| `/api/ai/generate` | POST | Generate a validated test suggestion |
| `/api/ai/analyze/{runId}` | POST | Analyze an owned execution |
| `/api/ai/recommendations/{apiId}` | POST | Recommend tests for an owned API |

OpenAPI import accepts `{ "specification": "<JSON or YAML document>", "collectionId": 1 }`. It imports the first HTTP(S) server and up to 200 GET/POST/PUT/PATCH/DELETE operations, using each path and summary/operationId. Path parameters become `{{variable}}` URL templates; query parameters are stored as normalized editable rows, and safe header parameters are stored as header templates. JSON request bodies use the first explicit example, a schema example/enum, or a bounded sample derived from the schema. HTTP Basic/Bearer and header API-key schemes are recognized, but credentials must be entered after import. Unsupported OAuth, cookie/query API keys, ambiguous security alternatives, non-JSON body formats, and other unsupported elements are reported. Response assertions are not imported. Resource list endpoints are paginated; API listing accepts `page` and `search`, history accepts `page`.

One active environment is allowed per user. Use `{{NAME}}` in request URLs, bodies, headers, query parameter names and values, or credentials; an unresolved variable fails before any outbound connection. Secret variables are encrypted at rest and omitted from environment responses. Execution history retains the request template URL instead of the resolved URL. For collection chaining, configure an API's extraction JSON path and runtime variable name; after that response succeeds, later requests in sort order can use that variable.

Test case `schemaJson` accepts a JSON schema subset with `type`, `required`, `properties`, `items`, and `enum`. Supported types are object, array, string, number, integer, boolean, and null. Unsupported keywords or executable extensions are rejected; validation is deterministic and applied to manual and scheduled test execution. Assertion paths use JSON paths such as `$.user.id` for bodies and `header:Content-Type` for response headers. Assertion outcomes and safe response headers are saved as normalized run evidence and returned from `/api/runs`; cookie and authorization response headers are omitted from persisted history. Saved APIs persist request body/content type, a 1–120 second timeout, and normalized header and query parameter rows. Query parameters can be enabled or disabled and retain existing URL query strings. Secret header and query values are encrypted at rest and omitted from GET responses; blank secret fields on update preserve their stored values.