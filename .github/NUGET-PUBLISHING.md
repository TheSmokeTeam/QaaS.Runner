# NuGet release security

## Required setup before merging or publishing

Keep this change in draft until an administrator verifies these settings. This
workflow alone cannot stop a writer from modifying a workflow on another branch.
No live policies, environments, or credentials are changed by this PR.

1. Create/protect the GitHub environment `nuget-release`. Select **Selected
   branches and tags**, allow release **tags** matching `*.*.*`, and allow no
   branches. Rules match the workflow's `GITHUB_REF`, not a checked-out commit.
   The glob is not a version validator. Do not use unrestricted deployment or
   rely on "Protected branches only": without branch protections it allows all
   branches. Require independent reviewers, prevent self-review, and disable
   protection bypass where available. Review event, tag, ancestry and workflow
   diff before approving deployment, including manual runs of modified workflows.
2. Protect creation, updates and deletion of release tags with a ruleset and
   protect master/workflow changes with review. Restrict tag creation to trusted
   release maintainers; otherwise independent environment review is essential.
   A matching tag name does not establish trusted ancestry.
3. In NuGet Trusted Publishing for Smoke, bind the policy to owner
   `TheSmokeTeam`, this exact repository, workflow filename `ci.yml`, and
   environment **`nuget-release`** (exactly the job environment). Scope the policy
   to only this repository's package IDs, including every packable project.
   Tighten or remove any old environment-empty/broader matching policy; retaining
   it beside the restricted policy preserves the bypass. Review other publishing
   credentials and policies for equivalent bypasses.
4. Verify settings first, then merge this workflow; coordinate a release pause
   because tightening the policy first intentionally blocks the old workflow.
   Create a new reviewed X.Y.Z tag containing this workflow on master ancestry.
   Re-running historical tag workflows uses their old code and is not a rollout.
   Verify ordinary PR/branch/manual CI without publishing. Perform an actual
   release only under separate release authorization after configuration review.

## Workflow behavior

Build/test has read-only contents permission and no OIDC. Only a tag **push**
with strict X.Y.Z syntax (no leading zeros), matching the checked-out event
commit and reachable from origin/master, can pack and enable publishing.
Manual dispatch is validation-only; Framework's old release_tag input is removed.
Retry a failed tag run using GitHub's rerun feature after verifying protections.

The fresh publish runner has no repository checkout or build/restore execution.
It downloads `nuget-packages` from the same workflow run, authenticates through
the protected environment, and pushes the tested packages. Empty artifacts and
each individual push failure fail the job. Coverage and `restored-packages`
metadata/artifacts remain available for PackageMirror. No packages are published
by the security tests or by opening this PR.

## References

- https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
- https://docs.github.com/actions/reference/workflows-and-actions/deployments-and-environments
