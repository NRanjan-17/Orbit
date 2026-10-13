# Code signing (SignPath Foundation)

Orbit is open source (MIT), so it can use **SignPath Foundation**, which signs open-source projects for free with
a publicly trusted certificate. Signed builds avoid most SmartScreen warnings and are generally allowed by Smart
App Control. Eligibility and steps are decided by SignPath; check their current terms at signpath.org.

The release workflow is already prepared: it uploads the unsigned zips, asks SignPath to sign them, waits, and
publishes the signed zips. **Until the settings below exist, releases are simply published unsigned.**

## 1. Apply
1. Make sure the repository is public, has the MIT licence (it does), a README describing the app (it does), and
   that releases are built by GitHub Actions (`.github/workflows/release.yml`).
2. Apply for the SignPath Foundation open-source programme on signpath.org with the repository URL.
3. After approval you get a SignPath organisation, a **project** and a **signing policy** (usually
   `release-signing`), and you install the SignPath GitHub app on the repository.

## 2. Artifact configuration (in SignPath)
The workflow uploads one GitHub artifact containing the release zips. Tell SignPath what to sign inside it.
A starting point (adjust to SignPath's current schema during onboarding):

```xml
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <!-- Orbit-<version>-win-x64.zip / -win-arm64.zip -->
    <zip-file path="Orbit-*-win-*.zip" max-matches="unbounded">
      <pe-file path="Orbit.exe"><authenticode-sign /></pe-file>
      <pe-file path="Orbit.dll"><authenticode-sign /></pe-file>
      <pe-file path="Orbit.Core.dll"><authenticode-sign /></pe-file>
      <pe-file path="Orbit.Platform.dll"><authenticode-sign /></pe-file>
    </zip-file>
    <!-- orbit-cli-<version>-win-x64.zip -->
    <zip-file path="orbit-cli-*-win-x64.zip">
      <pe-file path="orbit-cli.exe"><authenticode-sign /></pe-file>
    </zip-file>
  </zip-file>
</artifact-configuration>
```
Third-party files (the .NET runtime and Windows App SDK DLLs) are already signed by Microsoft and are left alone.

## 3. GitHub settings
Repository → Settings → Secrets and variables → Actions:

| Kind | Name | Value |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | API token of a SignPath CI user with submitter rights |
| Variable | `SIGNPATH_ORGANIZATION_ID` | your SignPath organisation ID |
| Variable | `SIGNPATH_PROJECT_SLUG` | the project slug, e.g. `Orbit` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | e.g. `release-signing` |

The token is never written to the repository or logs.

## 4. Release
Push a tag (`git tag v0.2.0 && git push origin v0.2.0`). The workflow submits the signing request; depending on the
policy a SignPath approver (you) approves it, and the signed zips are published.

Check a signed build: right-click `Orbit.exe` → Properties → **Digital Signatures**, or
`Get-AuthenticodeSignature .\Orbit.exe` (Status should be `Valid`).
