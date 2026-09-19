# Changelog

## 0.1.0

First package. It carries the runtime contracts the Concerned Cat companions are being moved onto, and nothing else:
no patch, no prefab, no command, no world state, no file of its own. Installed by itself it loads, says so, and stops.

* The package itself, as a new kind of thing in this repository: a **library**, which several mods may reference,
  rather than a **product**, which never references another. The rules that make that safe are enforced by the
  repository validator rather than left to a comment. A consuming mod references it with the library's DLL kept out
  of its own package, pins it on the storefront, and declares a hard dependency on it, so a missing library is a clear
  message at load instead of a failure later.
* It depends on no Concerned Cat mod, and the validator fails the build if it ever does.

Nothing in this release has been observed in game.
