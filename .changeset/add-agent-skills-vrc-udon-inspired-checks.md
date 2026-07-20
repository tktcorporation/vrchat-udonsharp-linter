---
"tktco.UdonSharpLinter": minor
---

Add new lint checks inspired by agent-skills-vrc-udon

New checks added:
- UDON032: Generic collection types (`List<T>`, `Dictionary<K,V>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`) are not supported in UdonSharp
- UDON033: LINQ (`using System.Linq;`) is not supported in UdonSharp
- UDON034: Lambda expressions, `delegate` declarations, and C# `event` fields are not supported in UdonSharp
- UDON035: Coroutines (`yield return` / `StartCoroutine()`) are not supported in UdonSharp
- UDON036: `UnityEvent.AddListener()` cannot reliably register UdonSharp methods at runtime
- UDON037: `GetComponent<UdonBehaviour>()` is not supported; use `(UdonBehaviour)GetComponent(typeof(UdonBehaviour))` instead
- UDON038: `[UdonSynced]` fields conflict with `[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]`
- UDON039: Manual sync mode with `[UdonSynced]` fields requires an explicit `RequestSerialization()` call
- UDON040: Warns when a behaviour has an excessive number of `[UdonSynced]` fields (network bandwidth budget)
- UDON041: Warns when `int[]`/`float[]` fields are synced, suggesting `byte[]`/`short[]` instead

Also added a new "Networking and Synchronization" section to the README, and unit tests covering all new checks.
