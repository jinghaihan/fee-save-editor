# Validation and releases

The build workflow runs Go save-engine and GUI tests, language and layout checks,
static analysis, the complete CLI regression suite, and release-script checks.
Separate Windows, Apple Silicon macOS, Intel macOS and Linux jobs build the GUI
and CLI and test their extracted packages. Linux checks native startup under
Xvfb with Mesa Vulkan.

Package checks verify executable permissions, macOS signatures and icons, bundle
versions, dependency licenses, CLI editing and source preservation. Each extracted
GUI must remain running without an early exit; the check terminates only its own
process. GUI layout and editing behavior are tested separately.

A release always rebuilds and checks the four platform packages. Complete business
validation may be reused only when the release commit changes VERSION alone and
its exact parent has a successful build from this repository's main branch.
Source or workflow changes require fresh validation. Failed, cancelled or forked
builds cannot satisfy the publication gate.

Run focused local checks for the changed behavior. Complete cross-platform
validation runs in CI. Use `python scripts/release.py patch` to publish a patch
release after the changes are committed and pushed.
