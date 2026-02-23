UsageTrackingService (orchestrator)
├── UsageProcessEnumerator - Process enumeration + filtering
├── UsageMetricsSampler - CPU sampling
├── UsageSessionTracker - Session state machine
├── UsageTrackingStorage - File I/O + exports
└── ProcessFilter - System process/app detection

Models:
└── UsageTrackingService.SessionRecord - Data model


// Filter logic:
// 1. Core Windows processes (HashSet - O(1) lookup)
// 2. Service patterns (suffix checks)
// 3. Common prefixes
// 4. Windows infrastructure
// 5. Network services
// 6. WMI/instrumentation
// 7. Graphics drivers
// 8. Root/embedded
// 9. DevTools (HashSet)
// 10. Browser helpers
// 11. Microsoft background
// 12. Security products (HashSet)
// 13. Databases
// 14. Background utilities
// 15. Common indicators