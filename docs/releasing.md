# Releasing

Versioning is automated with [release-please](https://github.com/googleapis/release-please),
driven by [Conventional Commits](https://www.conventionalcommits.org/) on `main`.
There are two independently versioned components:

| Component | Covers | Tag format | Current source of truth |
|-----------|--------|------------|------------------------|
| `app` | backend + frontend (and everything outside `firmware/`) | `app-vX.Y.Z` | git tag |
| `firmware` | `firmware/` only | `firmware-vX.Y.Z` | git tag |

Version fields in `package.json` / `Cargo.toml` are not maintained; the git
tag is the version. `.release-please-manifest.json` tracks the last released
version per component.

## How a release happens

1. Work merges into `main` as usual (feature branch → PR → merge). The
   commit type decides the next version:

   | Commit type | Bump |
   |-------------|------|
   | `fix:` | patch |
   | `feat:` | minor |
   | `feat!:` / `BREAKING CHANGE:` footer | major (minor while < 1.0.0) |
   | `docs:`, `ci:`, `chore:`, `refactor:`, `test:` | none |

   Which component is affected follows from the paths a commit touches:
   `firmware/**` counts for `firmware`, everything else for `app`
   (`firmware/` is excluded from `app` via `exclude-paths`). A commit
   touching both areas counts for both — so keep firmware commits
   strictly inside `firmware/` or they show up in the app changelog too.

2. The `Release` workflow runs on every push to `main`. release-please
   opens (or updates) one release PR per component with a releasable
   change pending, e.g. `chore(main): release app 1.1.0`. The PR contains
   only the changelog and manifest update and keeps accumulating until it
   is merged.

3. **Merging the release PR is the release.** That is the only manual
   step. On merge, release-please creates the tag and a GitHub release
   with the changelog section.

To skip the computed version and force a specific one, land a commit with
a `Release-As: 2.0.0` footer.

## What gets published

- **App release:** the `Release` workflow builds the container image with
  the version baked in (`APP_VERSION` build arg → env) and pushes it to
  GHCR as `:X.Y.Z` and `:latest`. This is the only path to the registry —
  ordinary merges to `main` only build-validate the image (`Image build`
  workflow) and push nothing. `:latest` therefore means "latest release",
  not "latest merge".

  To run the stack from a published image instead of building locally,
  layer `docker-compose.release.yml` over the base compose file:

  ```sh
  docker compose -f docker-compose.yml -f docker-compose.release.yml up -d
  ```

  `APP_IMAGE_TAG` in `.env` pins a specific release (default: `latest`).
- **Firmware release:** tag + GitHub release with changelog only. No
  binary is published on purpose: WiFi credentials and the broker
  address are baked in from the gitignored `config.toml` at build time,
  so a generic artifact would not run anywhere.

## Notes

- Release PRs are created by the Actions bot, and GitHub never auto-runs
  CI for PRs created with the workflow token. Release PRs touch no code,
  so no pipelines are expected on them anyway; changelog-only changes are
  excluded from the workflow path filters.
- Releasing one component while the other's release PR is open leaves the
  open PR with a conflict in `.release-please-manifest.json` (release-please
  only refreshes a release PR when its own content changes). It heals
  itself once the stale component gets its next releasable commit, or
  resolve it by hand: rebase the `release-please--branches--main--components--<component>`
  branch onto `main`, keep the newest version for both components in the
  manifest, and force-push.
