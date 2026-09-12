# winget manifests

The manifest for submitting AVASvgMaker to the Windows Package Manager, so that

```
winget install Harlock123.AVASvgMaker
```

finds it. It is kept here so the version that was submitted is recorded alongside the release
it describes; the package itself lives in Microsoft's repository, not this one.

## Submitting

The manifest has to be opened as a pull request against
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Copy the version folder into
a clone of that repository at the same path it has here - the path is part of what is
validated - and open the PR:

```
manifests/h/Harlock123/AVASvgMaker/<version>/
```

Microsoft's pipeline then installs the package on a clean machine and runs it. A first
submission is reviewed by a person as well, which takes a few days.

## What it says

The Windows build is a zip holding one self-contained `AVASvgMaker.exe`, so the manifest is
`InstallerType: zip` with `NestedInstallerType: portable`. winget unpacks it and puts
`avasvgmaker` on the PATH. Nothing is written to Program Files and nothing is registered as
installed software, which is what portable means and what this app is: there is no installer
to run.

Both architectures shipped for Windows are listed, x64 and arm64.

## For the next release

Three things change: `PackageVersion` in all three files, the two `InstallerUrl`s, and the two
`InstallerSha256`s. `ReleaseNotesUrl` and `ReleaseDate` want updating too.

The hashes are of the files the release actually serves, and must be exactly those - winget
refuses the download otherwise. Take them from the URLs rather than from a local build, since
a rebuild of the same source does not produce the same bytes:

```sh
curl -sL <InstallerUrl> | sha256sum
```

winget wants them in upper case.
