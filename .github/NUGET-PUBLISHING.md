# NuGet release security

## External protections are a release prerequisite

The workflow isolates publishing, but repository files cannot configure GitHub
rulesets, environment protections, or NuGet trusted publishers. Merging a PR or
passing branch CI does not verify those external settings. An administrator must
inspect them before a release is authorized. Do not create a tag or rerun an old
tag workflow to test whether protection exists.

Coordinate a release pause while changing these settings. Review queued and
in-progress release runs first. Security-setting changes, publisher-policy changes,
credential revocation, and an actual release need their own explicit approval.
Do not disable ordinary PR/branch CI as part of this procedure.

## Protect release tags without weakening creation control

Keep the existing tag-creation restriction and its trusted-maintainer bypass.
Add a separate active tag ruleset for release-tag immutability:

```json
{
  "name": "Release tag immutability",
  "target": "tag",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": {
    "ref_name": {"include": ["refs/tags/*.*.*"], "exclude": []}
  },
  "rules": [{"type": "update"}, {"type": "deletion"}]
}
```

This glob covers the workflow's strict X.Y.Z release tags; it is not a version
validator. Do not add update/deletion restrictions only to a creation ruleset
whose bypass list permits tag creators to bypass every rule. The separate rule
must have no bypass actors, including the tag-creation team. Existing tags remain
unchanged; corrections use a new version. Preserve master protections requiring
review of workflow changes and blocking force pushes and deletion. A matching tag
name alone does not prove trusted ancestry.

## Configure the release environment explicitly

Create/protect `nuget-release` before any publish job reaches it. Referencing a
missing environment in a workflow can create it without protection. Verify:

- Deployment mode is **Selected branches and tags**:
  `protected_branches: false`, `custom_branch_policies: true`.
- Exactly one allowed deployment pattern has **tag** type and name `*.*.*`;
  there are no branch rules. A branch rule with the same text is not equivalent.
- Required reviewers are explicitly selected and have repository read access.
  Ensure someone other than the initiating actor is available to approve.
- Prevent self-review is enabled and administrator bypass is disabled.

The administrator must confirm the reviewer identities or team; the tag-creation
bypass does not automatically select environment reviewers. GitHub requires only
one of the selected reviewers to approve. Review the event, tag, master ancestry,
workflow diff, and tested artifact before approving a deployment.

Rules match the workflow's `GITHUB_REF`, not a checked-out commit. Do not use
unrestricted deployment or rely on **Protected branches only**: without branch
protections, that mode can allow all branches. Keep unrelated environments intact.

## Bind NuGet trust to this repository and its packages

Inspect Trusted Publishing while authenticated as an authorized NuGet owner. The
workflow authenticates as `Smoke`; verify the actual policy owner and package
ownership rather than inferring them from that username. Required GitHub claims:

| Field | Value |
| --- | --- |
| Repository owner | `TheSmokeTeam` |
| Repository | `QaaS.Runner` |
| Workflow filename | `ci.yml` (not `.github/workflows/ci.yml`) |
| Environment | `nuget-release` (not empty) |

Scope publishing to these package IDs from this repository's packable projects:

- `QaaS.Runner`
- `QaaS.Runner.Assertions`
- `QaaS.Runner.Infrastructure`
- `QaaS.Runner.Sessions`
- `QaaS.Runner.Storage`

Do not include downstream or sibling repositories' packages. Prefer exact package
scopes; if the interface accepts only one exact pattern per policy, use separate
policies with identical repository/workflow/environment claims. Grant only the
operations needed to publish new versions. Confirm any need to create a new
package ID separately, and revisit this list when packable projects change.

Inventory matching policies and record their IDs, owners, scopes, and claims.
Obtain approval to tighten or remove each environment-empty or broader overlapping
policy. Adding a restrictive policy beside a broad policy leaves the broad
authorization path available. Preserve unrelated publishers. Public package
metadata and skipped publishing jobs cannot verify private policy configuration.

## Review legacy credentials separately

Inventory repository, environment, and organization credential sources available
to this repository. In particular, check `NUGET_AUTH_TOKEN`; the current `ci.yml`
uses OIDC and does not reference this secret. Do not print or copy secret values.

Identify any underlying NuGet key, its owner, scope, expiry, and other consumers.
After the trusted-publisher configuration is verified, obtain separate approval
for revoking the identified key and deleting an unused GitHub secret. Deleting a
GitHub secret does not revoke the NuGet credential. Do not create a replacement
long-lived key or remove unrelated credentials as a shortcut.

## Read back settings before authorizing a release

These GitHub CLI commands are read-only; they do not create an environment or
change a policy. Use an account authorized to inspect repository settings:

```powershell
$repo = 'TheSmokeTeam/QaaS.Runner'
gh api "repos/$repo/rulesets?includes_parents=true&per_page=100" --paginate
gh api "repos/$repo/environments/nuget-release"
gh api "repos/$repo/environments/nuget-release/deployment-branch-policies" --paginate
```

Read the detail endpoint `repos/$repo/rulesets/RULESET_ID` for each relevant ID
returned by the list, including inherited rulesets. Check active enforcement,
matching refs, rule types, and bypass actors; a ruleset name or list entry is not
proof of its contents. Treat a denied or incomplete response as unverified, not
as an empty or successful configuration. Recheck environment protection fields
and typed deployment policies, then inspect NuGet claims/scopes in its management
interface. Record evidence without secret values and resolve overlapping grants.

## Workflow behavior and release authorization

Build/test has read-only contents permission and no OIDC. Only a tag **push**
with strict X.Y.Z syntax (no leading zeros), matching the checked-out event
commit and reachable from origin/master, can pack and enable publishing.
Manual dispatch is validation-only. A successful master/PR run with publishing
skipped is expected; it proves neither environment approval nor NuGet login.

The fresh publish runner has no repository checkout or build/restore execution.
It downloads `nuget-packages` from the same workflow run, authenticates through
the protected environment, and pushes the tested packages. Empty artifacts and
each individual push failure fail the job. Coverage and `restored-packages`
metadata/artifacts remain available for PackageMirror.

After external protections are verified, obtain separate release authorization
and use a new reviewed X.Y.Z tag containing the hardened workflow on master
ancestry. Re-running historical tag workflows uses their old code and is not a
rollout. Retry a failed release only after checking protections and the exact
workflow version. Security tests and documentation PRs do not publish packages.

## References

- [GitHub rules and bypass semantics](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)
- [GitHub deployment environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
- [GitHub typed deployment policies](https://docs.github.com/en/rest/deployments/branch-policies)
- [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
