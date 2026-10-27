# Hypershell development guidance

## Project work items

At the beginning of each implementation or development work cycle, check for
`.hypershell/work-items.json` in the active project root.

- Treat the ordered list as a standing secondary backlog, not as a replacement
  for the user's current request.
- After scoping the user's request, classify the cycle as light, standard, or
  intensive. A light cycle is a simple question, investigation, or quick fix
  that is expected to finish and verify quickly without broad or high-risk
  changes.
- During a light cycle, include at most one small, safe work item even when it
  is not directly related to the primary request. Announce the incorporated
  item before changing it, and skip it when it would materially delay the
  answer, require a new product decision, expand external access, or make the
  cycle riskier.
- During a standard cycle, actively consider one compatible item. Do not force
  backlog work into intensive, high-risk, or already broad cycles.
- Prefer starred entries first, then the ordered list, with the expectation
  that a starred item should be considered within the next one to three work
  cycles.
- Pull in only work that is compatible with the current files, validation, and
  risk level. Do not try to empty the list every cycle.
- State which backlog item was incorporated, if any.
- Do not delete or rewrite tracker entries merely because related code changed.
  The user controls removal from the list unless they explicitly ask the agent
  to update it.
