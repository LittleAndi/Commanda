# Versioning workflow

## Versioning & Releases (nbgv CLI)

We use the `nbgv` CLI and publish on tags. No separate release branches are required.

Install:

```bash
dotnet tool install -g nbgv
```

## Typical workflow

Assume the current version in `version.json` is `0.3.0-alpha` or something.

To release the stable `0.3.0`, follow the steps below.

Workflow ends with us setting the next alpha in `main`.
This is how we try to prevent releasing a stable version from a feature branch.

```bash
# 0) make sure local main is exactly what’s on origin
git checkout main
git pull origin main
git fetch origin --prune --tags

# 1) create a release branch first
git checkout -b release/0.3.0

# 2) change version.json to the stable version and commit (on the release branch)
nbgv set-version 0.3.0
git add version.json
git commit -m "Release 0.3.0"

# sanity check
nbgv get-version

# 3) push branch and PR it
git push -u origin release/0.3.0

# open PR -> merge in GitHub UI

# 4) after merge: update local main and tag the merge result
git checkout main
git pull origin main
git fetch origin --prune --tags

# sanity check that main now resolves to 0.3.0
nbgv get-version

# tag HEAD (computed version) and push the tag explicitly
nbgv tag
git push origin --tags
# or: git push origin refs/tags/v0.3.0

# 5) bump main to next prerelease via a new PR branch
git checkout -b chore/bump-0.4.0-alpha
nbgv set-version 0.4.0-alpha
git add version.json
git commit -m "Start 0.4.0-alpha"
git push -u origin chore/bump-0.4.0-alpha

# open PR -> merge

```

If we want to release a Release Candidate (RC) version during feature development, we just tag a commit in the feature branch with a tag like `0.3.0-rc.1` and push that. This will be pushed to nuget.org as `0.3.0-rc-0001`.
