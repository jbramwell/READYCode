# Releasing READYCode

A release produces three things from one version number:

| Output | Built by | Goes to |
|---|---|---|
| `READYCode.msi` (x64) and `READYCode-arm64.msi` | **Build and Release** GitHub workflow, when you push a `vX.Y.Z` tag | GitHub Releases |
| `ReadyCode.Packaging_X.Y.Z.0_x64_arm64_bundle.msixupload` |You, locally, in Visual Studio | Microsoft Store (Partner Center) |

The version lives in exactly one place: `ReadyCodeVersion` in [Directory.Build.props](Directory.Build.props).
It flows into the app, both MSIs, and the MSIX package. Don't edit the `Version` in
`ReadyCode.Packaging/Package.appxmanifest` - it's a placeholder the build replaces.

---

## 1. Before you start

- [ ] `main` has everything you want to ship and the latest **Build and Test** run on `main` is
      green on both x64 and ARM64 (GitHub → Actions).
- [ ] Decide on any open Dependabot PRs. A FluentFTP major update needs testing against a real
      C64 Ultimate before merging.
- [ ] Run the UI smoke tests (about 45 seconds, hands off the keyboard):
      `.\ReadyCode.UITests\run-ui-tests.ps1`
- [ ] Manually check what no automated test covers, on a real device and in VICE:
      Load/Run on the C64 Ultimate and VICE, the C64U FTP explorer, mounting disk images, and the
      debugger.

## 2. Set the version and release notes

- [ ] Choose the version `X.Y.Z`. **The Microsoft Store requires every submission to have a
      higher version than the last one, and the fourth part is always 0** - so a Store-only
      resubmission still needs a new `Z` (e.g. 2.5.0 → 2.5.1).
- [ ] Set `<ReadyCodeVersion>X.Y.Z</ReadyCodeVersion>` in `Directory.Build.props`.
- [ ] Update the top of `CHANGELOG.md`. The release workflow publishes **the first `## [` section**
      as the GitHub release notes, so it must be the one for this release:
      - heading exactly `## [vX.Y.Z] - YYYY-MM-DD`, with today's date
      - `### New Features` / `### Improvements` / `### Bug Fixes` as needed
- [ ] Commit (e.g. `Release vX.Y.Z`), push to `main`, and wait for **Build and Test** to pass.

## 3. Tag → GitHub release with both MSIs

- [ ] Tag the commit you just pushed, and push the tag:
      ```
      git tag vX.Y.Z
      git push origin vX.Y.Z
      ```
- [ ] Watch **Build and Release** in GitHub Actions. It:
      1. fails immediately if the tag doesn't match `ReadyCodeVersion` (see *Fixing a bad tag* below)
      2. runs the unit tests
      3. publishes the app for win-x64 and win-arm64 (self-contained, ReadyToRun)
      4. builds `READYCode.msi` and `READYCode-arm64.msi`
      5. creates the GitHub release with both MSIs and your changelog section as its notes
- [ ] Open the new release on GitHub and check: **both** MSIs are attached, and the notes are the
      section you wrote.
- [ ] Install-test the MSIs: the ARM64 one on an ARM64 PC, and the x64 one on an x64 PC (it also
      installs on ARM64, running under emulation). Launch the app and check **Help > About** shows
      vX.Y.Z. *(Windows SmartScreen warns about an unknown publisher - expected, the MSIs aren't
      code-signed.)*

## 4. Build the Microsoft Store package (.msixupload)

This needs Visual Studio with the MSIX packaging tools - `dotnet build` can't build the
packaging project.

- [ ] In Visual Studio, open `ReadyCode.sln` and select the **Release** configuration.
- [ ] Right-click **ReadyCode.Packaging** → **Publish** → **Create App Packages...**
      - **Microsoft Store** (under the existing app name)
      - Architectures: **x64** and **ARM64** both checked, configuration **Release**
      - Leave **Automatically increment** *unchecked* - the version comes from
        `Directory.Build.props`
      - Optionally run the **Windows App Certification Kit** when offered - it catches most
        problems the Store's own certification would reject
- [ ] Check the output in `ReadyCode.Packaging\AppPackages\`: the file name must be
      `ReadyCode.Packaging_X.Y.Z.0_x64_arm64_bundle.msixupload` - both architectures, and your
      version.

  <details>
  <summary>Command-line alternative (same result, no wizard)</summary>

  From a **Developer PowerShell for VS** in the repo root:

  ```powershell
  msbuild ReadyCode.Packaging\ReadyCode.Packaging.wapproj /restore /m `
    /p:Configuration=Release /p:Platform=x64 /p:AppxBundle=Always `
    "/p:AppxBundlePlatforms=x64|arm64" /p:UapAppxPackageBuildMode=StoreUpload
  ```
  </details>

- [ ] If the package build fails with MakeAppx errors about files it can't find (typically after
      a .NET version change), delete these stale folders and build again:
      `ReadyCode.Packaging\obj`, `ReadyCode.Packaging\bin`, `ReadyCode\bin\x64`, `ReadyCode\bin\arm64`,
      `ReadyCode\obj\x64`, `ReadyCode\obj\arm64`.

## 5. Submit to the Microsoft Store

- [ ] In Partner Center, open READYCode and **start a new submission** (Update).
- [ ] **Packages**: upload the `.msixupload`. Check the package list shows version `X.Y.Z.0` for
      both **x64** and **arm64**.
- [ ] **Store listings**: update *What's new in this version* from your changelog section.
- [ ] Submit for certification, and watch for the certification result email.

## 6. Afterwards

- [ ] Once the Store update is live, install it from the Store and check **Help > Copy Diagnostic
      Info** reports `Installed as: MSIX package` and the new version.

---

## Fixing a bad tag

If **Build and Release** fails - wrong version, a test failure, a build problem - nothing has
been published yet (the release is the last step). Delete the tag, fix the problem on `main`,
and tag again:

```
git tag -d vX.Y.Z
git push origin :refs/tags/vX.Y.Z
```

If a release *was* created with a problem, also delete it on GitHub (Releases → the release →
Delete) before re-tagging.
