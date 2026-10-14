# Building Graph2VR releases

Use Unity **2021.2.15f1** with Android Build Support and Windows Build Support.
Configure compatible SDK, NDK r21d and JDK 8 paths under Preferences > External Tools.

The release version is `PlayerSettings.bundleVersion`. Android updates also require a
monotonically increasing `PlayerSettings.Android.bundleVersionCode`, independent of
the visible version. Version 1.4.0 uses Android version code 2.

In a licensed Unity editor use **Graph2VR > Build release > Android APK** or
**Windows x64**. The Android command builds an ARM64 IL2CPP APK. Windows builds x64
using the project's configured scripting backend. The enabled Build Settings scenes
are included. The normal scene settings are preserved; no private demo endpoints
are injected. HTTP access remains available for local SPARQL servers and media.

Outputs default to `Builds/<version>/Android` and `Builds/<version>/Windows`.
An optional `Library/Graph2VRBuildOutput.txt` file can specify an absolute output root.
With a correctly licensed batch editor, `-executeMethod Graph2VRBuild.Android` or
`Graph2VRBuild.Windows` can also be used with the corresponding `-buildTarget`.

For local automation, put `Android` or `Windows` in `build-request.txt` in the output
root. The editor consumes the request outside Play mode, switches target if needed,
and writes `<platform>-result.txt`. This mechanism does not supply a Unity license.

For distribution, zip the Windows executable together with its data directory,
runtime folders and DLLs. Exclude `*DoNotShip*` and backup/debug folders. Verify APK
signing, manifest version/architecture and SHA-256 hashes. Keep signing keys private.
Replacing an installed APK requires the same signing certificate and a suitable
version code; do not uninstall an existing app without preserving needed saves.

Before a release is considered device-tested, install and test loading, controllers,
media, save/load, frame rate and memory on the target headset.
