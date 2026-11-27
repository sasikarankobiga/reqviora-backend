# Final test report

| Check | Result |
| --- | --- |
| Debug build | PASS, 0 warnings/errors |
| Release build | PASS, 0 warnings/errors |
| xUnit regression | PASS: 314 passed, 0 failed, 1 skipped |
| API integration | PASS: 186 passed |
| Infrastructure | PASS: 98 passed, 1 skipped |
| Application | PASS: 30 passed |
| Contract suites | PASS: 236 checks |
| EF snapshot consistency | PASS: PostgreSQL and SQL Server |
| Dependency audit | PASS: no vulnerable packages |
| Git diff whitespace | PASS |

The sole skipped test requires configured native PostgreSQL and SQL Server services. Docker, Gitleaks, native database smoke tests, and backup/restore are not available locally and require CI/staging evidence.
