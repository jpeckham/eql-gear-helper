# EQL Reverse BiS Item Lookup (Console)

This console app queries `eqlegendstools.com`'s BiS gear endpoint and lets you type an item name.
It then reports whether that item appears in best-in-slot tables and where it ranks for
each matching class.

## Build & run

- Requirements: .NET 8 SDK
- From the repository root:

```powershell
dotnet run
```

Type an item name at the prompt. Press Enter on an empty line to exit.

## What the app does

- Fetches `/api/bis-gear` data for each class (`Bard`, `Beastlord`, ... , `Wizard`) using
  request headers compatible with the website.
- Searches every class result set for item-name matches (normalized, case-insensitive).
- Shows:
  - AC and notable stats
  - Overall rank + percentile for each class
  - Slot-specific rank + percentile per slot
- Gives a quick recommendation label:
  - Top-tier candidate
  - Strong BiS-adjacent option
  - Probably niche for current BiS lists

## Notes

- The site/API limits `classes=` to up to three values per call, so this app queries one class
  at a time and merges results.
