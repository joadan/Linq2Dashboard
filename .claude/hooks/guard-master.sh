#!/usr/bin/env bash
# PreToolUse hook for the Bash and PowerShell tools: refuse a git commit or
# git push while master is checked out. Every change lands through a pull
# request (CLAUDE.md, "Git workflow"). Exit 2 blocks the call and returns the
# message to the assistant; anything else lets the call through.
input=$(cat)
# Only a git command that starts a shell statement counts, so the words in a
# heredoc, a commit message or a comment do not trigger the guard.
start='(^|"command":"|\n|[;&|(][[:space:]]*)'
if ! printf '%s' "$input" | grep -qE "${start}git[[:space:]]+(-C[[:space:]]+[^[:space:]]+[[:space:]]+)?(commit|push)([[:space:]]|\\n|\"|$)"; then
  exit 0
fi
branch=$(git symbolic-ref --short -q HEAD 2>/dev/null)
if [ "$branch" = "master" ]; then
  echo "Blocked: master is checked out. Create a branch (git switch -c <name>), commit there, push it and open a pull request with 'gh pr create'." >&2
  exit 2
fi
exit 0
