---
"tktco.UdonSharpLinter": minor
---

Add new lint checks inspired by udon-analyzer

New checks added:
- UDON026: SendCustomEvent method name validation - Detects typos and missing methods in SendCustomEvent, SendCustomEventDelayedSeconds, SendCustomEventDelayedFrames, and SendCustomNetworkEvent calls
- UDON027: Null conditional operator (?.) detection - Detects usage of unsupported ?. operator
- UDON028: Null coalescing operator (??, ??=) detection - Detects usage of unsupported ?? and ??= operators
- UDON029: Async/await detection - Detects usage of unsupported async methods and await expressions
- UDON030: Goto/label statement detection - Detects usage of unsupported goto and labeled statements

Also added:
- Test project with unit tests for new checks
- CI workflow for running tests on PRs
