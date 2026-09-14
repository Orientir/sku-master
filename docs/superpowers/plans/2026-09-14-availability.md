# Supplier availability implementation plan

Approved scope: independent Ukrainian module, site CSV + supplier XLS/XLSX, availability by SKU only, optional multi-status scope, individual and bulk status edits, CSV/XLSX export. No server or changes to exclusions.

- [x] Add Core availability rows, comparison, validated atomic bulk edits and export projection. Test exact SKU matching, leading zeroes, empty statuses, duplicate/conflicting SKU and invalid bulk edits.
- [x] Add independent Desktop state/settings in LocalAppData/ SkuMaster/availability. Reuse existing file adapters and atomic export; invalidate on input changes, preserve edits on output setting changes, protect inputs and unsaved edits.
- [x] Add bridge commands and native file/save routing. Test real CSV/XLSX workflows and module isolation.
- [x] Add third React module using shared components: two file fields; all/status scope; four stat filters; paginated table with selection, copy and status edits; settings/warnings/help tabs; bottom export controls. Selection applies to all filtered rows across pages and clears on scope/filter changes.
- [x] Build/typecheck, run full .NET suite and inspect actual WebView UI. Document behavior and deliver local build; deployment is a separate explicit request.
